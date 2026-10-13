import type { Draft, UploadFile } from "@cichlids/client-core";
import { localStore } from "../lib/localStore";
import { formForDraft } from "./drafts";
import { DEFAULT_TOPIC, UPLOAD_TOPICS, type UploadFormState, type UploadTopic } from "./uploadForm";

export const SAVED_UPLOAD_INPUT_KEY = "cichlids.upload.input";

/** At most this many drafts keep their typed input; the draft typed in longest ago goes first. */
export const MAX_SAVED_DRAFTS = 20;

/** A photo the native picker returned, which the device can open again by its URI. */
export interface SavedPhoto {
  uri: string;
  name: string;
  type: string;
}

/** The input typed for one uploaded draft. */
export interface SavedDraftInput {
  draftId: string;
  form: UploadFormState;
}

/**
 * The upload input of one member kept on the device: the typed title, description and topic per
 * uploaded draft, the most recently typed last, and a picked photo whose upload has not finished,
 * with its input.
 */
export interface SavedUploadInput {
  accountId: string;
  drafts: SavedDraftInput[];
  photo: { file: SavedPhoto; form: UploadFormState } | null;
}

/** The picked file as a photo the device can open again, or null for a browser file, which a reload loses. */
export function toSavedPhoto(file: UploadFile | null): SavedPhoto | null {
  if (!file || file instanceof Blob) return null;
  return { uri: file.uri, name: file.name, type: file.type };
}

/** Reads serialized upload input; anything missing or malformed yields null. */
export function parseSavedUploadInput(raw: string | null): SavedUploadInput | null {
  if (!raw) return null;
  let value: unknown;
  try {
    value = JSON.parse(raw);
  } catch {
    return null;
  }
  if (typeof value !== "object" || value === null) return null;
  const { accountId, drafts, photo } = value as Record<string, unknown>;
  if (typeof accountId !== "string" || accountId === "") return null;
  const savedDrafts: SavedDraftInput[] = [];
  for (const entry of Array.isArray(drafts) ? drafts : []) {
    const draftId = (entry as { draftId?: unknown } | null)?.draftId;
    const form = parseForm((entry as { form?: unknown } | null)?.form);
    if (typeof draftId === "string" && draftId !== "" && form) savedDrafts.push({ draftId, form });
  }
  let savedPhoto: SavedUploadInput["photo"] = null;
  if (typeof photo === "object" && photo !== null) {
    const { file, form } = photo as Record<string, unknown>;
    const parsedFile = parsePhoto(file);
    const parsedForm = parseForm(form);
    if (parsedFile && parsedForm) savedPhoto = { file: parsedFile, form: parsedForm };
  }
  return { accountId, drafts: savedDrafts, photo: savedPhoto };
}

function parseForm(value: unknown): UploadFormState | null {
  if (typeof value !== "object" || value === null) return null;
  const { title, description, topic } = value as Record<string, unknown>;
  return {
    title: typeof title === "string" ? title : "",
    description: typeof description === "string" ? description : "",
    topic: UPLOAD_TOPICS.find((candidate): candidate is UploadTopic => candidate === topic) ?? DEFAULT_TOPIC,
  };
}

function parsePhoto(value: unknown): SavedPhoto | null {
  if (typeof value !== "object" || value === null) return null;
  const { uri, name, type } = value as Record<string, unknown>;
  return typeof uri === "string" && typeof name === "string" && typeof type === "string" ? { uri, name, type } : null;
}

/** The form for a draft the screen opens: the input kept for that draft, else the draft's own texts. */
export function formForResumedDraft(draft: Draft, saved: SavedUploadInput | null): UploadFormState {
  return saved?.drafts.find((entry) => entry.draftId === String(draft.id))?.form ?? formForDraft(draft);
}

// Store operations run one after another, so no change interleaves with another in flight.
let pending: Promise<unknown> = Promise.resolve();

function inOrder<T>(operation: () => Promise<T>): Promise<T> {
  const next = pending.then(operation, operation);
  pending = next.catch(() => undefined);
  return next;
}

async function read(): Promise<SavedUploadInput | null> {
  return parseSavedUploadInput(await localStore.getItem(SAVED_UPLOAD_INPUT_KEY).catch(() => null));
}

/** Applies a change to the member's input; the input of another member on this device is dropped. */
function update(accountId: string, change: (input: SavedUploadInput) => SavedUploadInput): Promise<void> {
  return inOrder(async () => {
    const current = await read();
    const own = current?.accountId === accountId ? current : { accountId, drafts: [], photo: null };
    const next = change(own);
    if (next.drafts.length === 0 && !next.photo) await localStore.removeItem(SAVED_UPLOAD_INPUT_KEY);
    else await localStore.setItem(SAVED_UPLOAD_INPUT_KEY, JSON.stringify(next));
  });
}

/**
 * The upload input kept on this device across reloads and sign-outs until the draft is published
 * or discarded. Only the member who entered it gets it back.
 */
export const savedUploadInput = {
  load(accountId: string | null): Promise<SavedUploadInput | null> {
    return inOrder(async () => {
      const saved = accountId ? await read() : null;
      return saved?.accountId === accountId ? saved : null;
    });
  },
  /** Keeps the input typed for an uploaded draft; the draft's photo is no longer pending. */
  rememberDraft(accountId: string, draftId: string, form: UploadFormState): Promise<void> {
    return update(accountId, (input) => {
      const drafts = [...input.drafts.filter((entry) => entry.draftId !== draftId), { draftId, form }];
      return { ...input, drafts: drafts.slice(-MAX_SAVED_DRAFTS), photo: null };
    });
  },
  /** Keeps a picked photo whose upload has not finished. */
  rememberPhoto(accountId: string, file: SavedPhoto, form: UploadFormState): Promise<void> {
    return update(accountId, (input) => ({ ...input, photo: { file, form } }));
  },
  /** Forgets the input of a published or discarded draft. */
  forgetDraft(accountId: string, draftId: string): Promise<void> {
    return update(accountId, (input) => ({ ...input, drafts: input.drafts.filter((entry) => entry.draftId !== draftId) }));
  },
};
