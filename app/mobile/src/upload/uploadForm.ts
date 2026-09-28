import type { PublishPostRequest, UploadFile } from "@cichlids/client-core";

/** Longest accepted title, matching the API's publish validation. */
export const TITLE_MAX_LENGTH = 200;

/** The topics a user can publish a photo under. */
export const UPLOAD_TOPICS = ["cichlids", "tanks", "offtopic"] as const;

export type UploadTopic = (typeof UPLOAD_TOPICS)[number];

export const DEFAULT_TOPIC: UploadTopic = "cichlids";

export interface UploadFormState {
  title: string;
  description: string;
  topic: UploadTopic;
}

export function initialUploadForm(): UploadFormState {
  return { title: "", description: "", topic: DEFAULT_TOPIC };
}

/** A draft can be published with a title of 1 to 200 characters after trimming. */
export function canPublish(form: UploadFormState): boolean {
  const title = form.title.trim();
  return title.length > 0 && title.length <= TITLE_MAX_LENGTH;
}

/** The publish request body: trimmed title, trimmed description or null when blank, the topic. */
export function toPublishRequest(form: UploadFormState): PublishPostRequest {
  const description = form.description.trim();
  return {
    title: form.title.trim(),
    description: description.length > 0 ? description : null,
    topic: form.topic,
  };
}

/** The part of an image picker asset that the native upload needs. */
export interface PickedAsset {
  uri: string;
  fileName?: string | null;
  mimeType?: string | null;
}

/**
 * Turns a native picker asset into the multipart file object React Native's FormData sends. The
 * picker returns JPEG output when a quality is set, so JPEG is the fallback when it omits the name
 * or type.
 */
export function toUploadFile(asset: PickedAsset): UploadFile {
  return {
    uri: asset.uri,
    name: asset.fileName || "photo.jpg",
    type: asset.mimeType || "image/jpeg",
  };
}

/**
 * The text to show for a failed upload or publish: the API's own message for a 400 response
 * (not an image, too large, too many pixels, invalid title), else the given fallback, since other
 * failures carry technical text such as a status line or a network error.
 */
export function uploadErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && (error as { status?: unknown }).status === 400 && error.message) {
    return error.message;
  }
  return fallback;
}
