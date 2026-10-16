import type { UploadFile } from "@cichlids/client-core";

/** The part of the camera permission state that decides what the camera screen shows. */
export interface CameraPermissionState {
  granted: boolean;
  canAskAgain: boolean;
  status: string;
}

export type CameraStage = "checking" | "explain" | "denied" | "ready";

/**
 * What the camera screen shows for a permission state: nothing yet while it is unknown, the
 * explanation before the first request, the calm hint once the member has denied the camera, and
 * the viewfinder when it is granted.
 */
export function cameraStage(permission: CameraPermissionState | null): CameraStage {
  if (permission === null) return "checking";
  if (permission.granted) return "ready";
  return permission.status === "denied" ? "denied" : "explain";
}

/** The picture the camera returns: a file address on a device, a data address in the browser. */
export interface CameraPicture {
  uri: string;
}

/**
 * The file the upload sends for a camera picture. A device picture is sent by its file address;
 * the data address of a browser picture becomes a file of the image type the address declares.
 */
export async function shotToUploadFile(picture: CameraPicture, now: number = Date.now()): Promise<UploadFile> {
  const name = `time-lapse-${now}.jpg`;
  if (!picture.uri.startsWith("data:")) return { uri: picture.uri, name, type: "image/jpeg" };
  return (await fetch(picture.uri)).blob();
}

/** The address an image view shows for a file: a device file by its address, a browser file by an object address. */
export function previewUri(file: UploadFile): string {
  return file instanceof Blob ? URL.createObjectURL(file) : file.uri;
}

/** Frees the object address of a preview; other addresses stay untouched. */
export function releasePreview(uri: string): void {
  if (uri.startsWith("blob:")) URL.revokeObjectURL(uri);
}
