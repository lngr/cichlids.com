import { describe, expect, it, vi } from "vitest";
import { ApiError } from "./client.js";
import { uploadVideo, type VideoUploadSession, type VideoUploadTransport } from "./videoUpload.js";

const CHUNK = 8;

function source(size: number) {
  return { size, slice: async (start: number, end: number) => ({ start, end }) as unknown as Blob };
}

function server(size: number, overrides: Partial<VideoUploadTransport> = {}) {
  let offset = 0;
  const received: Array<[number, number]> = [];
  const session = (): VideoUploadSession => ({ id: 7, offset, size, chunkSize: CHUNK });
  const transport: VideoUploadTransport = {
    create: vi.fn(async () => session()),
    status: vi.fn(async () => session()),
    append: vi.fn(async (_id: number, at: number, chunk: Blob) => {
      const { start, end } = chunk as unknown as { start: number; end: number };
      if (at !== offset) throw new ApiError("mismatch", 409, "video_upload_offset_mismatch");
      received.push([start, end]);
      offset = end;
      return offset;
    }),
    complete: vi.fn(async () => ({ id: 99, state: "processing" }) as never),
    ...overrides,
  };
  return { transport, received, serverOffset: () => offset };
}

const options = { sleep: async () => {}, maxRetries: 3 };
const request = { target: "tank" as const, tankId: 1, size: 20, fileName: "a.mp4" };

describe("uploadVideo", () => {
  it("sends the file in chunks of the session's size, reports progress and completes", async () => {
    const { transport, received } = server(20);
    const progress: number[] = [];

    const video = await uploadVideo(transport, source(20), request, { ...options, onProgress: (done, total) => progress.push(done / total) });

    expect(received).toEqual([[0, 8], [8, 16], [16, 20]]);
    expect(progress).toEqual([0, 0.4, 0.8, 1]);
    expect(video.id).toBe(99);
    expect(transport.complete).toHaveBeenCalledWith(7);
  });

  it("asks the server for the offset after a lost connection and carries on from there", async () => {
    let failures = 1;
    const base = server(20);
    const append = base.transport.append;
    base.transport.append = vi.fn(async (id, at, chunk) => {
      if (at === 8 && failures-- > 0) {
        await append(id, at, chunk);
        throw new ApiError("Network error", 0);
      }
      return append(id, at, chunk);
    });

    await uploadVideo(base.transport, source(20), request, options);

    expect(base.transport.status).toHaveBeenCalledTimes(1);
    expect(base.received).toEqual([[0, 8], [8, 16], [16, 20]]);
  });

  it("resends a chunk the server never received", async () => {
    let failures = 2;
    const base = server(20);
    const append = base.transport.append;
    base.transport.append = vi.fn(async (id, at, chunk) => {
      if (at === 8 && failures-- > 0) throw new ApiError("Network error", 0);
      return append(id, at, chunk);
    });

    await uploadVideo(base.transport, source(20), request, options);

    expect(base.received).toEqual([[0, 8], [8, 16], [16, 20]]);
  });

  it("gives up after the configured number of failures in a row", async () => {
    const base = server(20, { append: vi.fn(async () => { throw new ApiError("Network error", 0); }) });

    await expect(uploadVideo(base.transport, source(20), request, options)).rejects.toMatchObject({ status: 0 });

    expect(base.transport.append).toHaveBeenCalledTimes(4);
  });

  it("resynchronizes after an offset mismatch", async () => {
    const base = server(20);
    const append = base.transport.append;
    let first = true;
    base.transport.append = vi.fn(async (id, at, chunk) => {
      if (first) { first = false; throw new ApiError("mismatch", 409, "video_upload_offset_mismatch"); }
      return append(id, at, chunk);
    });

    await uploadVideo(base.transport, source(20), request, options);

    expect(base.transport.status).toHaveBeenCalledTimes(1);
    expect(base.received).toEqual([[0, 8], [8, 16], [16, 20]]);
  });

  it("does not retry a refusal and cancels nothing the server owns", async () => {
    const base = server(20, { append: vi.fn(async () => { throw new ApiError("no", 400, "video_format_unsupported"); }) });

    await expect(uploadVideo(base.transport, source(20), request, options)).rejects.toMatchObject({ code: "video_format_unsupported" });

    expect(base.transport.append).toHaveBeenCalledTimes(1);
    expect(base.transport.complete).not.toHaveBeenCalled();
  });

  it("stops on a refusal at completion", async () => {
    const base = server(20, { complete: vi.fn(async () => { throw new ApiError("long", 422, "video_too_long"); }) });

    await expect(uploadVideo(base.transport, source(20), request, options)).rejects.toMatchObject({ code: "video_too_long" });
  });

  it("continues an existing session without creating a new one", async () => {
    const base = server(20);
    await base.transport.append(7, 0, { start: 0, end: 8 } as unknown as Blob);

    await uploadVideo(base.transport, source(20), request, { ...options, sessionId: 7 });

    expect(base.transport.create).not.toHaveBeenCalled();
    expect(base.received).toEqual([[0, 8], [8, 16], [16, 20]]);
  });

  it("stops when the signal is aborted", async () => {
    const controller = new AbortController();
    const base = server(20);
    const append = base.transport.append;
    base.transport.append = vi.fn(async (id, at, chunk) => { controller.abort(); return append(id, at, chunk); });

    await expect(uploadVideo(base.transport, source(20), request, { ...options, signal: controller.signal })).rejects.toMatchObject({ name: "AbortError" });

    expect(base.transport.complete).not.toHaveBeenCalled();
  });

  it("completes an upload that already holds every byte", async () => {
    const base = server(20);
    base.transport.status = vi.fn(async () => ({ id: 7, offset: 20, size: 20, chunkSize: CHUNK }));

    await uploadVideo(base.transport, source(20), request, { ...options, sessionId: 7 });

    expect(base.transport.append).not.toHaveBeenCalled();
    expect(base.transport.complete).toHaveBeenCalled();
  });
});
