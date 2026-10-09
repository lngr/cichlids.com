import type { Draft } from "@cichlids/client-core";
import { UPLOAD_TOPICS, initialUploadForm, type UploadFormState, type UploadTopic } from "./uploadForm";

/** The draft id from the upload screen's route parameter, or null when none is given. */
export function draftParam(value: string | string[] | undefined): string | null {
  const raw = Array.isArray(value) ? value[0] : value;
  const trimmed = raw?.trim();
  return trimmed ? trimmed : null;
}

/** The draft with the given id, or null when the list has none with that id. */
export function findDraft(drafts: readonly Draft[], id: string): Draft | null {
  return drafts.find((draft) => String(draft.id) === id) ?? null;
}

/** The image for the upload screen's preview: the largest variant up to medium size. */
export function draftPreviewUri(draft: Draft): string {
  return draft.image.medium ?? draft.image.large ?? draft.image.small ?? draft.image.original;
}

/** The image for a draft list entry: the smallest variant available. */
export function draftThumbnailUri(draft: Draft): string {
  return draft.image.thumb ?? draft.image.small ?? draft.image.medium ?? draft.image.original;
}

/** The draft's trimmed title, or null when it has none or only whitespace. */
export function draftTitle(draft: Draft): string | null {
  const title = draft.title?.trim();
  return title ? title : null;
}

/**
 * The publish form for a resumed draft: the draft's title and description (empty when it has
 * none) and its topic when users can publish under it, otherwise the default topic.
 */
export function formForDraft(draft: Draft): UploadFormState {
  const form = { ...initialUploadForm(), title: draft.title ?? "", description: draft.description ?? "" };
  const topic = UPLOAD_TOPICS.find((candidate): candidate is UploadTopic => candidate === draft.topic);
  return topic ? { ...form, topic } : form;
}
