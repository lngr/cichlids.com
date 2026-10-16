import type { VideoUploadSource } from "@cichlids/client-core";

/** A video the member chose: its file name and the bytes, readable by range. */
export interface PickedVideo extends VideoUploadSource {
  fileName: string;
}

export interface VideoPickButtonsProps {
  /** Test id prefix; the buttons are named with the suffixes -library and -record. */
  testID?: string;
  disabled?: boolean;
  onPicked(video: PickedVideo): void;
  onError(error: unknown): void;
}

/** A picked video backed by a Blob or File. */
export function blobVideo(blob: Blob, fileName: string): PickedVideo {
  return { fileName, size: blob.size, slice: async (start, end) => blob.slice(start, end) };
}
