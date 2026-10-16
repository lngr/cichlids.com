import { expect, it } from "vitest";
import { createCichlidsClient } from "./client.js";
import { canPublishStory, isValidCaption, moveEntry, normalizeCaption, storySequence, toStoryEditor } from "./storyEditor.js";

const urls = { thumb: "/api/media/photos/t1", small: null, medium: null, large: null };
const dto = {
  id: "5", state: "draft", slug: null, title: "Reef", description: null, topic: "cichlids", coverItemId: "11", cover: urls,
  items: [
    { id: "11", mediaItemId: "7", position: 2, caption: "Second", kind: "photo", image: urls, video: null },
    { id: "10", mediaItemId: "8", position: 0, caption: null, kind: "photo", image: urls, video: null },
  ],
  subjects: [
    { id: "3", type: "tank", position: 1, tank: { id: "9", title: "Tank", category: null, image: null }, inhabitant: null, species: null },
    { id: "4", type: "species", position: null, tank: null, inhabitant: null, species: { id: "2", slug: null, genus: "G", name: "n", displayName: "G n" } },
  ],
  unavailableSubjectIds: ["6"], quickShareMediaItemId: null, updatedAt: "2026-10-16T10:00:00Z",
};

it("maps ids to numbers and keeps the cards by type", () => {
  const editor = toStoryEditor(dto as never);
  expect(editor).toMatchObject({ id: 5, coverItemId: 11, unavailableSubjectIds: [6], quickShareMediaItemId: null });
  expect(editor.subjects[0]).toMatchObject({ id: 3, position: 1, tank: { id: 9, title: "Tank" } });
  expect(editor.subjects[1]).toMatchObject({ position: null, species: { id: 2, displayName: "G n" } });
});

it("orders items and positioned subjects into one sequence and leaves references out", () => {
  expect(storySequence(toStoryEditor(dto as never))).toEqual([
    { kind: "item", id: 10 }, { kind: "subject", id: 3 }, { kind: "item", id: 11 },
  ]);
});

it("moves an entry one step and stays put at the ends", () => {
  const order = storySequence(toStoryEditor(dto as never));
  expect(moveEntry(order, 1, -1).map(entry => entry.id)).toEqual([3, 10, 11]);
  expect(moveEntry(order, 1, 1).map(entry => entry.id)).toEqual([10, 11, 3]);
  expect(moveEntry(order, 0, -1)).toEqual(order);
  expect(moveEntry(order, 2, 1)).toEqual(order);
});

it("limits captions and trims them", () => {
  expect(normalizeCaption("  hello ")).toBe("hello");
  expect(normalizeCaption("   ")).toBeNull();
  expect(isValidCaption("x".repeat(500))).toBe(true);
  expect(isValidCaption("x".repeat(501))).toBe(false);
});

it("publishes only with a title and an item", () => {
  const editor = toStoryEditor(dto as never);
  expect(canPublishStory(editor, "Reef")).toBe(true);
  expect(canPublishStory(editor, "  ")).toBe(false);
  expect(canPublishStory({ items: [] }, "Reef")).toBe(false);
});

it("calls the editor routes with signed paths made absolute", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({
    baseUrl: "http://stories.test", getAccessToken: async () => "token",
    fetch: async request => { requests.push(request as Request); return Response.json(dto); },
  });
  const editor = await client.storyEditor.addItem(5, 7, "A caption");
  expect(requests[0]!.method).toBe("POST");
  expect(requests[0]!.url).toBe("http://stories.test/api/me/stories/5/items");
  expect(await requests[0]!.clone().json()).toEqual({ mediaItemId: 7, caption: "A caption" });
  expect(editor.items[0]!.image?.thumb).toBe("http://stories.test/api/media/photos/t1");
  await client.storyEditor.reorder(5, [{ kind: "item", id: 10 }, { kind: "subject", id: 3 }]);
  expect(requests[1]!.method).toBe("PUT");
  expect(requests[1]!.url).toBe("http://stories.test/api/me/stories/5/order");
  expect(await requests[1]!.clone().json()).toEqual({ entries: [{ kind: "item", id: 10 }, { kind: "subject", id: 3 }] });
  await client.storyEditor.setCover(5, null);
  expect(await requests[2]!.clone().json()).toEqual({ itemId: null });
});

it("shares a file to the gallery and keeps the problem code of a second share", async () => {
  const requests: Request[] = [];
  let status = 201;
  const client = createCichlidsClient({
    baseUrl: "http://stories.test", getAccessToken: async () => "token",
    fetch: async request => {
      requests.push(request as Request);
      return status === 201
        ? Response.json({ id: 1, slug: "abc", items: [], subjects: [] }, { status })
        : Response.json({ title: "Already shared", code: "media_already_shared" }, { status });
    },
  });
  expect((await client.library.share(7, { title: "My fish", topic: "cichlids" })).slug).toBe("abc");
  expect(requests[0]!.url).toBe("http://stories.test/api/me/media/7/share");
  status = 409;
  await expect(client.library.share(7, { title: "Again" })).rejects.toMatchObject({ status: 409, code: "media_already_shared" });
});
