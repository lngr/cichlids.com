import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { createCichlidsClient } from "./client.js";

const baseUrl = "http://device-client-mock.test";

let authorizations: (string | null)[];
let answer: () => Response;

const server = setupServer(
  http.get(`${baseUrl}/api/me`, ({ request }) => {
    authorizations.push(request.headers.get("Authorization"));
    return answer();
  }),
  http.put(`${baseUrl}/api/me/interests`, ({ request }) => {
    authorizations.push(request.headers.get("Authorization"));
    return answer();
  }),
  http.post(`${baseUrl}/api/auth/device/challenges`, () => HttpResponse.json({ challenge: "c" })),
  ...["preview", "upgrade"].map((route) =>
    http.post(`${baseUrl}/api/me/provisional/${route}`, () => HttpResponse.json({ profile: { id: 1 }, roles: [], provisional: false }))),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterAll(() => server.close());
beforeEach(() => {
  authorizations = [];
  answer = () => HttpResponse.json({ profile: { id: 1 }, roles: [], provisional: true });
});
afterEach(() => server.resetHandlers());

const proof = { challenge: "c", credentialId: "id", timestamp: 1, signature: "s" };

describe("the device token source", () => {
  it("sends no token when the source has none", async () => {
    const client = createCichlidsClient({ baseUrl, deviceAuth: { getAccessToken: async () => undefined } });
    await client.me.get();
    expect(authorizations).toEqual([null]);
  });

  it("sends the device token when there is no access token", async () => {
    const client = createCichlidsClient({ baseUrl, deviceAuth: { getAccessToken: async () => "device" } });
    await client.me.get();
    expect(authorizations).toEqual(["Bearer device"]);
  });

  it("drops the cached device token when the API answers 401 to it", async () => {
    const invalidate = vi.fn();
    answer = () => HttpResponse.json({ detail: "expired" }, { status: 401 });
    const client = createCichlidsClient({ baseUrl, deviceAuth: { getAccessToken: async () => "device", invalidate } });
    await expect(client.me.get()).rejects.toMatchObject({ status: 401 });
    expect(invalidate).toHaveBeenCalledTimes(1);
  });

  it("keeps the device token when the API answers 401 to an access token", async () => {
    const invalidate = vi.fn();
    answer = () => HttpResponse.json({ detail: "expired" }, { status: 401 });
    const client = createCichlidsClient({ baseUrl, getAccessToken: () => "keycloak", deviceAuth: { getAccessToken: async () => "device", invalidate } });
    await expect(client.me.get()).rejects.toMatchObject({ status: 401 });
    expect(invalidate).not.toHaveBeenCalled();
  });
});

describe("registration with a session that needs no bearer token", () => {
  it("accepts the session of the account as the Keycloak side of the proof", async () => {
    const signProof = vi.fn(async () => proof);
    const client = createCichlidsClient({ baseUrl, accountSession: () => true, deviceAuth: { getAccessToken: async () => undefined, signProof } });
    await expect(client.registration.upgrade()).resolves.toBeDefined();
    expect(signProof).toHaveBeenCalledTimes(1);
  });

  it("refuses without any session of the account", async () => {
    const client = createCichlidsClient({ baseUrl, accountSession: () => false, deviceAuth: { getAccessToken: async () => undefined, signProof: async () => proof } });
    await expect(client.registration.upgrade()).rejects.toMatchObject({ status: 401, code: "keycloak_session_required" });
  });
});

describe("registration_required answers", () => {
  it("reports an answer to a write", async () => {
    answer = () => HttpResponse.json({ code: "registration_required", detail: "Register." }, { status: 403 });
    const onRegistrationRequired = vi.fn();
    const client = createCichlidsClient({ baseUrl, onRegistrationRequired });
    await expect(client.me.setInterests(["lake_malawi"])).rejects.toMatchObject({ code: "registration_required" });
    expect(onRegistrationRequired).toHaveBeenCalledTimes(1);
  });

  it("does not report an answer to a read", async () => {
    answer = () => HttpResponse.json({ code: "registration_required", detail: "Register." }, { status: 403 });
    const onRegistrationRequired = vi.fn();
    const client = createCichlidsClient({ baseUrl, onRegistrationRequired });
    await expect(client.me.get()).rejects.toMatchObject({ code: "registration_required" });
    expect(onRegistrationRequired).not.toHaveBeenCalled();
  });

  it("does not report other refusals", async () => {
    answer = () => HttpResponse.json({ code: "account_suspended", detail: "No." }, { status: 403 });
    const onRegistrationRequired = vi.fn();
    const client = createCichlidsClient({ baseUrl, onRegistrationRequired });
    await expect(client.me.setInterests([])).rejects.toMatchObject({ code: "account_suspended" });
    expect(onRegistrationRequired).not.toHaveBeenCalled();
  });
});
