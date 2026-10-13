// Story: TASK-3.41
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { Draft } from "@cichlids/client-core";

const stored = vi.hoisted(() => new Map<string, string>());

vi.mock("../lib/localStore", () => ({
  localStore: {
    getItem: vi.fn(async (key: string) => stored.get(key) ?? null),
    setItem: vi.fn(async (key: string, value: string) => void stored.set(key, value)),
    removeItem: vi.fn(async (key: string) => void stored.delete(key)),
  },
}));

const { MAX_SAVED_DRAFTS, formForResumedDraft, parseSavedUploadInput, savedUploadInput, toSavedPhoto } = await import("./savedUploadInput");

const form = { title: "Aulonocara im Morgenlicht", description: "Frisch umgesetzt", topic: "tanks" as const };
const photo = { uri: "file:///photo.jpg", name: "photo.jpg", type: "image/jpeg" };

function draft(overrides: Partial<Draft> = {}): Draft {
  return {
    id: 42,
    title: null,
    description: null,
    topic: "cichlids",
    createdAt: "2026-10-01T10:00:00Z",
    image: { thumb: null, small: null, medium: null, large: null },
    ...overrides,
  } as Draft;
}

beforeEach(() => {
  stored.clear();
});

describe("the upload input of a member", () => {
  it("is there again after a sign-out for the member who typed it", async () => {
    await savedUploadInput.rememberDraft("account-a", "42", form);

    const saved = await savedUploadInput.load("account-a");

    expect(saved?.drafts).toEqual([{ draftId: "42", form }]);
    expect(formForResumedDraft(draft({ title: "Server title" }), saved)).toEqual(form);
  });

  it("stays hidden from another member on the same device, whose input replaces it", async () => {
    await savedUploadInput.rememberDraft("account-a", "42", form);

    expect(await savedUploadInput.load("account-b")).toBeNull();
    expect(await savedUploadInput.load(null)).toBeNull();

    await savedUploadInput.rememberDraft("account-b", "7", form);
    expect(await savedUploadInput.load("account-a")).toBeNull();
  });

  it("keeps the input of several drafts and forgets a published or discarded one", async () => {
    await savedUploadInput.rememberDraft("account-a", "1", form);
    await savedUploadInput.rememberDraft("account-a", "2", { ...form, title: "Second" });
    await savedUploadInput.forgetDraft("account-a", "1");

    const saved = await savedUploadInput.load("account-a");
    expect(saved?.drafts.map((entry) => entry.draftId)).toEqual(["2"]);

    await savedUploadInput.forgetDraft("account-a", "2");
    expect(stored.size).toBe(0);
  });

  it("drops the draft typed in longest ago beyond the limit", async () => {
    for (let id = 1; id <= MAX_SAVED_DRAFTS; id += 1) await savedUploadInput.rememberDraft("account-a", String(id), form);
    await savedUploadInput.rememberDraft("account-a", "1", { ...form, title: "Typed again" });
    await savedUploadInput.rememberDraft("account-a", "new", form);

    const ids = (await savedUploadInput.load("account-a"))?.drafts.map((entry) => entry.draftId) ?? [];
    expect(ids).toHaveLength(MAX_SAVED_DRAFTS);
    expect(ids).not.toContain("2");
    expect(ids.slice(-2)).toEqual(["1", "new"]);
  });

  it("keeps a picked photo until its draft exists", async () => {
    await savedUploadInput.rememberPhoto("account-a", photo, form);
    expect((await savedUploadInput.load("account-a"))?.photo).toEqual({ file: photo, form });

    await savedUploadInput.rememberDraft("account-a", "42", form);
    expect((await savedUploadInput.load("account-a"))?.photo).toBeNull();
  });
});

describe("toSavedPhoto", () => {
  it("keeps a native photo and leaves out a browser file", () => {
    expect(toSavedPhoto(photo)).toEqual(photo);
    expect(toSavedPhoto(new Blob(["x"]))).toBeNull();
    expect(toSavedPhoto(null)).toBeNull();
  });
});

describe("parseSavedUploadInput", () => {
  it("rejects missing and malformed values and repairs unknown topics", () => {
    expect(parseSavedUploadInput(null)).toBeNull();
    expect(parseSavedUploadInput("{")).toBeNull();
    expect(parseSavedUploadInput(JSON.stringify({ drafts: [] }))).toBeNull();
    expect(
      parseSavedUploadInput(
        JSON.stringify({ accountId: "a", drafts: [{ draftId: "1", form: { ...form, topic: "x" } }, { form }, null], photo: { file: {} } }),
      ),
    ).toEqual({ accountId: "a", drafts: [{ draftId: "1", form: { ...form, topic: "cichlids" } }], photo: null });
  });
});

describe("formForResumedDraft", () => {
  it("uses the draft's own texts without kept input for it", async () => {
    await savedUploadInput.rememberDraft("account-a", "7", form);
    const saved = await savedUploadInput.load("account-a");

    expect(formForResumedDraft(draft({ title: "Server title" }), saved).title).toBe("Server title");
    expect(formForResumedDraft(draft({ title: "Server title" }), null).title).toBe("Server title");
  });
});
