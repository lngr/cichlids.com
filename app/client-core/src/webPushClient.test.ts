import { expect, it } from "vitest";
import { ApiError, createCichlidsClient } from "./client.js";

const subscription = { endpoint: "https://push.test/send/abc", keys: { p256dh: "BKey", auth: "authSecret" }, timeZone: "Europe/Berlin" };

it("reads the Web Push configuration with the public key", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://push.test", getAccessToken: async () => "token", fetch: async request => {
    requests.push(request as Request);
    return Response.json({ available: true, publicKey: "BPublic" });
  } });
  expect(await client.webPush.config()).toEqual({ available: true, publicKey: "BPublic" });
  expect(requests[0].url).toBe("http://push.test/api/me/web-push");
  expect(requests[0].headers.get("Authorization")).toBe("Bearer token");
});

it("posts a subscription with its keys and time zone and deletes it by endpoint", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://push.test", getAccessToken: async () => "token", fetch: async request => {
    const req = request as Request;
    requests.push(req.clone());
    return req.method === "DELETE" ? new Response(null, { status: 204 }) : Response.json({ id: "7", timeZone: "Europe/Berlin", createdAt: "2026-10-16T08:00:00Z" }, { status: 201 });
  } });
  expect(await client.webPush.subscribe(subscription)).toEqual({ id: 7, timeZone: "Europe/Berlin" });
  expect(requests[0].method).toBe("POST");
  expect(requests[0].url).toBe("http://push.test/api/me/web-push/subscriptions");
  expect(await requests[0].json()).toEqual(subscription);
  await client.webPush.unsubscribe(subscription.endpoint);
  expect(requests[1].method).toBe("DELETE");
  expect(await requests[1].json()).toEqual({ endpoint: subscription.endpoint });
});

it("reports a refused subscription with the problem code", async () => {
  const client = createCichlidsClient({ baseUrl: "http://push.test", getAccessToken: async () => "token", fetch: async () =>
    Response.json({ code: "web_push_limit_reached", detail: "Private" }, { status: 409 }) });
  await expect(client.webPush.subscribe(subscription)).rejects.toEqual(new ApiError("Private", 409, "web_push_limit_reached"));
});
