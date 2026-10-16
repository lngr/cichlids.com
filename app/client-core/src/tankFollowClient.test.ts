import { expect, it } from "vitest";
import { ApiError, createCichlidsClient } from "./index.js";

function setup(answer: (request: Request) => Response) {
  const requests: Request[] = [];
  const client = createCichlidsClient({
    baseUrl: "http://follow.test",
    getAccessToken: async () => "follow-token",
    fetch: async request => { const req = request as Request; requests.push(req); return answer(req); },
  });
  return { client, requests };
}
const member = { member: { id: 5, username: "anna", displayName: null, avatarUrl: null }, since: "2026-10-16T08:00:00Z" };

it("reads, requests and withdraws the follow relation of a tank", async () => {
  const { client, requests } = setup(request => request.method === "DELETE"
    ? new Response(null, { status: 204 })
    : Response.json({ state: request.method === "PUT" ? "requested" : "none", canFollow: request.method === "GET" }));
  expect(await client.tanks.follow(42)).toEqual({ state: "none", canFollow: true });
  expect(requests[0].method).toBe("GET");
  expect(requests[0].url).toBe("http://follow.test/api/tanks/42/follow");
  expect((await client.tanks.requestFollow(42)).state).toBe("requested");
  expect(requests[1].method).toBe("PUT");
  await client.tanks.unfollow(42);
  expect(requests[2].method).toBe("DELETE");
  expect(requests[2].url).toBe("http://follow.test/api/tanks/42/follow");
});

it("answers follow requests and removes followers on the exact routes", async () => {
  const { client, requests } = setup(request => request.method === "GET"
    ? Response.json({ total: 1, items: [member] })
    : new Response(null, { status: 204 }));
  expect((await client.tanks.followRequests(42, { offset: 0, limit: 20 })).items[0].member.username).toBe("anna");
  expect(requests[0].url).toBe("http://follow.test/api/tanks/42/follow-requests?offset=0&limit=20");
  expect((await client.tanks.followers(42)).total).toBe(1);
  expect(requests[1].url).toBe("http://follow.test/api/tanks/42/followers");
  await client.tanks.approveFollowRequest(42, 5);
  await client.tanks.declineFollowRequest(42, 5);
  await client.tanks.removeFollower(42, 5);
  expect(requests.slice(2).map(request => `${request.method} ${request.url}`)).toEqual([
    "POST http://follow.test/api/tanks/42/follow-requests/5/approve",
    "POST http://follow.test/api/tanks/42/follow-requests/5/decline",
    "DELETE http://follow.test/api/tanks/42/followers/5",
  ]);
});

it("manages the selected members by handle and by profile id", async () => {
  const { client, requests } = setup(request => request.method === "DELETE"
    ? new Response(null, { status: 204 })
    : request.method === "POST" ? Response.json(member) : Response.json([member]));
  expect(await client.tanks.selectedMembers(42)).toHaveLength(1);
  expect(requests[0].url).toBe("http://follow.test/api/tanks/42/selected-members");
  expect((await client.tanks.addSelectedMember(42, "anna")).member.username).toBe("anna");
  expect(requests[1].method).toBe("POST");
  expect(await requests[1].json()).toEqual({ handle: "anna" });
  await client.tanks.removeSelectedMember(42, 5);
  expect(requests[2].method).toBe("DELETE");
  expect(requests[2].url).toBe("http://follow.test/api/tanks/42/selected-members/5");
});

it("carries the problem code of a refused follow", async () => {
  const { client } = setup(() => Response.json({ code: "tank_follow_not_allowed", detail: "No" }, { status: 409 }));
  const error = await client.tanks.requestFollow(42).catch(caught => caught);
  expect(error).toBeInstanceOf(ApiError);
  expect(error.code).toBe("tank_follow_not_allowed");
  expect(error.status).toBe(409);
});
