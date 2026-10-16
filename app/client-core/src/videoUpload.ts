import { ApiError } from "./client.js";
import type { CreateVideoUploadRequest, VideoDto } from "./generated/types.gen.js";

/** An open upload session: the bytes the server holds so far and the size of the chunks it expects. */
export interface VideoUploadSession {
  id: number | string;
  offset: number | string;
  size: number | string;
  chunkSize: number | string;
}

/** The calls the uploader needs; the API client provides them. */
export interface VideoUploadTransport {
  create(request: CreateVideoUploadRequest): Promise<VideoUploadSession>;
  status(id: number): Promise<VideoUploadSession>;
  /** Sends one chunk that starts at the offset and returns the offset after it. */
  append(id: number, offset: number, chunk: Blob): Promise<number>;
  complete(id: number): Promise<VideoDto>;
}

/** The file to upload: its size and a way to read a byte range. */
export interface VideoUploadSource {
  size: number;
  slice(start: number, end: number): Promise<Blob>;
}

export interface UploadVideoOptions {
  /** Continues an existing session instead of opening one. */
  sessionId?: number;
  /** Failures in a row (lost connection) after which the upload gives up. */
  maxRetries?: number;
  onProgress?: (uploaded: number, total: number) => void;
  signal?: AbortSignal;
  sleep?: (milliseconds: number) => Promise<void>;
}

const OFFSET_MISMATCH = "video_upload_offset_mismatch";
const wait = (milliseconds: number) => new Promise<void>(resolve => setTimeout(resolve, milliseconds));

function abortError() {
  return Object.assign(new Error("The upload was aborted."), { name: "AbortError" });
}

/**
 * Uploads a video in the chunks the server asks for and completes it. A lost connection or an
 * offset mismatch is answered by asking the server how many bytes it holds and continuing from
 * there; the wait between attempts grows with the failures in a row. A refusal of the server
 * (for example an unsupported file or a video that is too long) ends the upload at once.
 */
export async function uploadVideo(
  transport: VideoUploadTransport,
  source: VideoUploadSource,
  request: CreateVideoUploadRequest,
  options: UploadVideoOptions = {},
): Promise<VideoDto> {
  const { maxRetries = 5, onProgress, signal, sleep = wait } = options;
  const check = () => { if (signal?.aborted) throw abortError(); };
  check();
  let session = options.sessionId === undefined ? await transport.create(request) : await transport.status(options.sessionId);
  const id = Number(session.id);
  const size = Number(session.size);
  const chunkSize = Number(session.chunkSize);
  let offset = Number(session.offset);
  let failures = 0;
  onProgress?.(offset, size);
  while (offset < size) {
    check();
    try {
      offset = await transport.append(id, offset, await source.slice(offset, Math.min(offset + chunkSize, size)));
      failures = 0;
      onProgress?.(offset, size);
    } catch (error) {
      const resumable = error instanceof ApiError && (error.status === 0 || error.code === OFFSET_MISMATCH);
      if (!resumable || failures >= maxRetries) throw error;
      failures += 1;
      check();
      if (error.status === 0) await sleep(Math.min(1000 * 2 ** (failures - 1), 15000));
      try {
        session = await transport.status(id);
        offset = Number(session.offset);
      } catch (statusError) {
        if (!(statusError instanceof ApiError && statusError.status === 0)) throw statusError;
      }
    }
  }
  check();
  return transport.complete(id);
}
