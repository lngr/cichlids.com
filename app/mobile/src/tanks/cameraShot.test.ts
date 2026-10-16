import { describe, expect, it, vi } from "vitest";
import { cameraStage, previewUri, releasePreview, shotToUploadFile } from "./cameraShot";

describe("cameraStage", () => {
  it("waits for the permission state before deciding", () => {
    expect(cameraStage(null)).toBe("checking");
  });
  it("opens the camera once the permission is granted", () => {
    expect(cameraStage({ granted: true, canAskAgain: true, status: "granted" })).toBe("ready");
  });
  it("explains the camera use before the first request", () => {
    expect(cameraStage({ granted: false, canAskAgain: true, status: "undetermined" })).toBe("explain");
  });
  it("shows the calm hint once the permission is denied, also when the system cannot ask again", () => {
    expect(cameraStage({ granted: false, canAskAgain: false, status: "denied" })).toBe("denied");
    expect(cameraStage({ granted: false, canAskAgain: true, status: "denied" })).toBe("denied");
  });
});

describe("shotToUploadFile", () => {
  it("hands a native picture to the upload by its file address", async () => {
    expect(await shotToUploadFile({ uri: "file:///cache/a.jpg" }, 1_700_000_000_000)).toEqual({
      uri: "file:///cache/a.jpg", name: "time-lapse-1700000000000.jpg", type: "image/jpeg",
    });
  });
  it("turns the data address of a browser picture into a file of the declared image type", async () => {
    const file = await shotToUploadFile({ uri: "data:image/jpeg;base64,/9j/4AAQ" }, 5);
    expect(file).toBeInstanceOf(Blob);
    const blob = file as Blob;
    expect(blob.type).toBe("image/jpeg");
    expect([...new Uint8Array(await blob.arrayBuffer())].slice(0, 3)).toEqual([0xff, 0xd8, 0xff]);
    expect(((await shotToUploadFile({ uri: "data:image/png;base64,iVBORw0KGgo=" })) as Blob).type).toBe("image/png");
  });
});

describe("previewUri", () => {
  it("shows a native file by its address and a browser file through an object address", () => {
    expect(previewUri({ uri: "file:///a.jpg", name: "a.jpg", type: "image/jpeg" })).toBe("file:///a.jpg");
    const create = vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:x");
    const revoke = vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});
    const uri = previewUri(new Blob(["x"]));
    expect(uri).toBe("blob:x");
    releasePreview(uri);
    releasePreview("file:///a.jpg");
    expect(create).toHaveBeenCalledTimes(1);
    expect(revoke).toHaveBeenCalledExactlyOnceWith("blob:x");
    create.mockRestore();
    revoke.mockRestore();
  });
});
