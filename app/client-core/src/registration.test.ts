import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from "vitest";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { ApiError, createCichlidsClient } from "./client.js";
import { createDeviceAuth, deviceSignatureMessage } from "./deviceAuth.js";
import { createWebCryptoKeyProvider } from "./deviceAuthWebCrypto.js";

const baseUrl = "http://registration-mock.test";
const credentialId = "0c2a3f3e-5b55-4d33-9a59-5a8f0f6d2c11";

type Call = { route: string; query: string; authorization: string | null; body: unknown };

let calls: Call[];
let challengeCounter: number;
let refusal: { status: number; code: string } | null;

const local = { date: "2026-10-15", utcOffsetMinutes: 120 };
const counts = { tanks: 2, careEntries: 5, mediaItems: 3, todos: 1, vacations: 2 };

const server = setupServer(
  http.post(`${baseUrl}/api/auth/device/credentials`, () =>
    HttpResponse.json({ credentialId, profileId: 42, displayName: "user12345678" }, { status: 201 })),
  http.post(`${baseUrl}/api/auth/device/challenges`, () => {
    challengeCounter += 1;
    const challenge = Buffer.alloc(32, challengeCounter).toString("base64url");
    return HttpResponse.json({ challenge, expiresAt: "2026-10-16T12:02:00Z" });
  }),
  ...["preview", "upgrade", "move"].map((route) =>
    http.post(`${baseUrl}/api/me/provisional/${route}`, async ({ request }) => {
      calls.push({ route, query: new URL(request.url).search, authorization: request.headers.get("Authorization"), body: await request.json() });
      if (refusal) return HttpResponse.json({ code: refusal.code, detail: "Refused." }, { status: refusal.status });
      if (route === "preview") {
        return HttpResponse.json({ mode: "move", content: counts, vacationsDropped: 1, vacationsDroppedRepair: 2, targetDisplayName: "Tanja" });
      }
      const me = { profile: { id: 7 }, roles: [], provisional: false };
      return HttpResponse.json(route === "upgrade" ? me : { moved: counts, vacationsDropped: "1", vacationsDroppedRepair: 3, me });
    })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterAll(() => server.close());
beforeEach(() => {
  calls = [];
  challengeCounter = 0;
  refusal = null;
});
afterEach(() => server.resetHandlers());

function setup(keycloakToken: string | undefined) {
  const keys = createWebCryptoKeyProvider();
  const deviceAuth = createDeviceAuth({ baseUrl, keys, now: () => Date.parse("2026-10-16T12:00:00Z") });
  const client = createCichlidsClient({ baseUrl, getAccessToken: () => keycloakToken, deviceAuth });
  return { keys, deviceAuth, client };
}

describe("handing the content of the installation to an account", () => {
  it("signs a fresh challenge that the public key of the installation verifies", async () => {
    const { keys, deviceAuth } = setup("keycloak-token");
    await deviceAuth.ensureRegistered();

    const proof = await deviceAuth.signProof();

    expect(proof.credentialId).toBe(credentialId);
    expect(proof.timestamp).toBe(Date.parse("2026-10-16T12:00:00Z") / 1000);
    const publicKey = await crypto.subtle.importKey("spki", await keys.exportPublicKey(), { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
    const message = deviceSignatureMessage(proof.challenge, proof.credentialId, proof.timestamp);
    expect(await crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, publicKey, Buffer.from(proof.signature, "base64url"), message)).toBe(true);
  });

  it("previews with the Keycloak token as bearer and a signed challenge as body", async () => {
    const { client } = setup("keycloak-token");

    const preview = await client.registration.preview(local);

    expect(preview).toEqual({ mode: "move", content: counts, vacationsDropped: 1, vacationsDroppedRepair: 2, targetDisplayName: "Tanja" });
    expect(calls).toHaveLength(1);
    expect(calls[0]!.authorization).toBe("Bearer keycloak-token");
    expect(calls[0]!.body).toMatchObject({ credentialId, timestamp: expect.any(Number), challenge: expect.any(String), signature: expect.any(String) });
  });

  it("signs a new challenge for every call", async () => {
    const { client } = setup("keycloak-token");

    await client.registration.preview(local);
    await client.registration.preview(local);

    const [first, second] = calls.map((call) => (call.body as { challenge: string }).challenge);
    expect(first).not.toBe(second);
  });

  it("upgrades and returns the profile of the account", async () => {
    const { client } = setup("keycloak-token");

    const me = await client.registration.upgrade();

    expect(me.profile.id).toBe(7);
    expect(calls[0]).toMatchObject({ route: "upgrade", authorization: "Bearer keycloak-token" });
  });

  it("moves and reports the amounts as numbers", async () => {
    const { client } = setup("keycloak-token");

    const moved = await client.registration.move(local);

    expect(moved.moved).toEqual(counts);
    expect(moved.vacationsDropped).toBe(1);
    expect(moved.vacationsDroppedRepair).toBe(3);
    expect(calls[0]!.query).toBe("?date=2026-10-15&utcOffsetMinutes=120");
    expect(moved.me.profile.id).toBe(7);
    expect(calls[0]).toMatchObject({ route: "move", authorization: "Bearer keycloak-token" });
  });

  it("fails without a Keycloak session and sends nothing", async () => {
    const { client } = setup(undefined);

    await expect(client.registration.move(local)).rejects.toMatchObject({ status: 401, code: "keycloak_session_required" });
    expect(calls).toEqual([]);
    expect(challengeCounter).toBe(0);
  });

  it("fails without the device authentication of the installation", async () => {
    const client = createCichlidsClient({ baseUrl, getAccessToken: () => "keycloak-token" });

    await expect(client.registration.upgrade()).rejects.toThrow(/device authentication/);
    expect(calls).toEqual([]);
  });

  it("raises the code of a refusal", async () => {
    refusal = { status: 409, code: "account_has_profile" };
    const { client } = setup("keycloak-token");

    const error = await client.registration.upgrade().catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 409, code: "account_has_profile" });
  });
});
