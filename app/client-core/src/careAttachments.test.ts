import { createActor, waitFor } from "xstate";
import { expect, it, vi } from "vitest";
import { ApiError, createCichlidsClient } from "./client.js";
import { MAX_PHOTOS, NOTE_MAX_LENGTH, TODO_MAX_LENGTH, careAttachmentsMachine, isValidNote, normalizeNote, normalizeTodoText, todoListMachine } from "./careAttachments.js";

const entry = { id: 7, tankId: 1, authorProfileId: 2, kind: "water_change" as const, occurredAt: "2026-10-15T12:00:00Z", recordedAt: "2026-10-15T12:00:00Z",
  amount: null, values: null, note: null, photos: [], todos: [] };
const todo = { id: 3, tankId: 1, careEntryId: 7, text: "Buy conditioner", done: false, doneAt: null, createdAt: "2026-10-15T12:00:00Z" };
const effects = () => ({ note: vi.fn(async () => entry), addPhoto: vi.fn(async () => entry), removePhoto: vi.fn(async () => entry), addTodo: vi.fn(async () => todo) });

it("limits note, to-do text and photo count and normalizes input", () => {
  expect([NOTE_MAX_LENGTH, TODO_MAX_LENGTH, MAX_PHOTOS]).toEqual([2000, 200, 6]);
  expect(normalizeNote("  hello \n")).toBe("hello");
  expect(normalizeNote("   ")).toBeNull();
  expect(isValidNote("n".repeat(2000))).toBe(true);
  expect(isValidNote(" " + "n".repeat(2000) + " ")).toBe(true);
  expect(isValidNote("n".repeat(2001))).toBe(false);
  expect(normalizeTodoText("  Buy salt ")).toBe("Buy salt");
  expect(normalizeTodoText("  ")).toBeNull();
  expect(normalizeTodoText("t".repeat(201))).toBeNull();
  expect(normalizeTodoText("t".repeat(200))).toBe("t".repeat(200));
});

it("runs one attachment change at a time and counts the revision", async () => {
  const fx = effects();
  const actor = createActor(careAttachmentsMachine, { input: fx }).start();
  actor.send({ type: "SAVE_NOTE", entryId: 7, note: "after the change" });
  actor.send({ type: "ADD_TODO", entryId: 7, text: "ignored while busy" });
  await waitFor(actor, s => s.matches("idle"));
  expect(fx.note).toHaveBeenCalledWith(7, "after the change");
  expect(fx.addTodo).not.toHaveBeenCalled();
  expect(actor.getSnapshot().context.revision).toBe(1);
  actor.send({ type: "ADD_PHOTO", entryId: 7, file: new Blob(["x"]) });
  await waitFor(actor, s => s.context.revision === 2);
  actor.send({ type: "REMOVE_PHOTO", entryId: 7, photoId: 9 });
  await waitFor(actor, s => s.context.revision === 3);
  actor.send({ type: "ADD_TODO", entryId: 7, text: "Buy conditioner" });
  await waitFor(actor, s => s.context.revision === 4);
  expect(fx.removePhoto).toHaveBeenCalledWith(7, 9);
  expect(fx.addTodo).toHaveBeenCalledWith(7, "Buy conditioner");
  actor.stop();
});

it("keeps the error of a failed change until the next change starts", async () => {
  const fx = effects();
  fx.addPhoto.mockRejectedValueOnce(new ApiError("full", 409, "care_photo_limit_reached"));
  const actor = createActor(careAttachmentsMachine, { input: fx }).start();
  actor.send({ type: "ADD_PHOTO", entryId: 7, file: new Blob(["x"]) });
  await waitFor(actor, s => s.matches("idle") && s.context.error !== null);
  expect(actor.getSnapshot().context.revision).toBe(0);
  expect(actor.getSnapshot().context.error).toEqual(new ApiError("full", 409, "care_photo_limit_reached"));
  actor.send({ type: "SAVE_NOTE", entryId: 7, note: null });
  await waitFor(actor, s => s.matches("idle") && s.context.revision === 1);
  expect(actor.getSnapshot().context.error).toBeNull();
  actor.stop();
});

it("adds, checks off, reopens and removes to-dos from the list", async () => {
  const fx = { add: vi.fn(async () => todo), setDone: vi.fn(async () => todo), remove: vi.fn(async () => todo) };
  const actor = createActor(todoListMachine, { input: fx }).start();
  actor.send({ type: "ADD", tankId: 1, text: "Buy conditioner" });
  await waitFor(actor, s => s.context.revision === 1);
  actor.send({ type: "SET_DONE", id: 3, done: true });
  await waitFor(actor, s => s.context.revision === 2);
  actor.send({ type: "SET_DONE", id: 3, done: false });
  await waitFor(actor, s => s.context.revision === 3);
  actor.send({ type: "REMOVE", id: 3 });
  await waitFor(actor, s => s.context.revision === 4);
  expect(fx.add).toHaveBeenCalledWith(1, "Buy conditioner");
  expect(fx.setDone.mock.calls).toEqual([[3, true], [3, false]]);
  expect(fx.remove).toHaveBeenCalledWith(3);
  fx.setDone.mockRejectedValueOnce(new Error("offline"));
  actor.send({ type: "SET_DONE", id: 3, done: true });
  await waitFor(actor, s => s.matches("idle") && s.context.error !== null);
  expect(actor.getSnapshot().context.revision).toBe(4);
  actor.stop();
});

it("sends notes, photos and to-dos to the care routes and builds photo URLs", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://care.test/", getAccessToken: async () => "tok", fetch: async request => {
    const req = request as Request; requests.push(req.clone());
    if (req.url.includes("/todos") && req.method === "GET") return Response.json({ total: "2", items: [{ ...todo, tankTitle: "Mbuna" }] });
    if (req.url.includes("/todos")) return Response.json(todo, { status: req.method === "POST" ? 201 : 200 });
    return Response.json(entry);
  } });
  await client.care.setNote(1, 7, "  ");
  expect(requests[0].method).toBe("PATCH");
  expect(await requests[0].json()).toEqual({ amount: null, note: "" });
  await client.care.setNote(1, 7, null);
  expect(await requests[1].json()).toEqual({ amount: null, note: "" });
  await client.care.addPhoto(1, 7, new Blob(["x"], { type: "image/jpeg" }));
  expect(requests[2].url).toBe("http://care.test/api/tanks/1/care/7/photos");
  expect(requests[2].headers.get("Content-Type")).toContain("multipart/form-data");
  expect(requests[2].headers.get("Authorization")).toBe("Bearer tok");
  await client.care.removePhoto(1, 7, 4);
  expect(requests[3].method).toBe("DELETE");
  expect(requests[3].url).toBe("http://care.test/api/tanks/1/care/7/photos/4");
  await client.care.addTodo(1, 7, "Buy conditioner");
  expect(requests[4].url).toBe("http://care.test/api/tanks/1/care/7/todos");
  expect(await requests[4].json()).toEqual({ text: "Buy conditioner" });
  await client.todos.add(1, "Clean filter");
  expect(requests[5].url).toBe("http://care.test/api/tanks/1/todos");
  const open = await client.todos.list({ state: "open", limit: 1 });
  expect(requests[6].url).toBe("http://care.test/api/me/todos?state=open&limit=1");
  expect(open).toEqual({ total: 2, items: [{ ...todo, tankTitle: "Mbuna" }] });
  await client.todos.setDone(3, true);
  expect(requests[7].method).toBe("PATCH");
  expect(requests[7].url).toBe("http://care.test/api/me/todos/3");
  expect(await requests[7].json()).toEqual({ done: true });
  await client.todos.remove(3);
  expect(requests[8].method).toBe("DELETE");
  expect(client.care.photoUrl("/api/care-photos/abc")).toBe("http://care.test/api/care-photos/abc");
});
