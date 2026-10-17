import { describe, expect, it } from "vitest";
import { ApiError, createCichlidsClient } from "./client.js";
import { handleProblemOf } from "./handle.js";

describe("handleProblemOf", () => {
  it.each([
    ["handle_invalid", "invalid"],
    ["handle_generated", "generated"],
    ["handle_reserved", "reserved"],
    ["handle_taken", "taken"],
  ] as const)("maps %s to %s", (code, reason) => {
    expect(handleProblemOf(new ApiError("x", 400, code))).toEqual({ reason });
  });

  it("names the next change of a change that came too soon", () => {
    const error = new ApiError("x", 429, "handle_change_too_soon", { nextChangeAt: "2026-11-16T10:00:00+00:00" });
    expect(handleProblemOf(error)).toEqual({ reason: "too_soon", nextChangeAt: "2026-11-16T10:00:00+00:00" });
  });

  it("leaves a change that came too soon without a date when the body names none", () => {
    expect(handleProblemOf(new ApiError("x", 429, "handle_change_too_soon"))).toEqual({ reason: "too_soon" });
  });

  it.each([new ApiError("x", 500), new ApiError("x", 403, "registration_required"), new Error("x"), null, "handle_taken"])(
    "ignores %j",
    (error) => {
      expect(handleProblemOf(error)).toBeNull();
    },
  );
});

describe("me.setHandle", () => {
  it("sends the handle and returns the handle with the next change", async () => {
    const requests: Request[] = [];
    const client = createCichlidsClient({ baseUrl: "http://handle.test", fetch: async request => {
      requests.push(request as Request);
      return Response.json({ handle: "anna", nextChangeAt: "2026-11-16T10:00:00+00:00" });
    } });

    expect(await client.me.setHandle("Anna")).toEqual({ handle: "anna", nextChangeAt: "2026-11-16T10:00:00+00:00" });

    expect(requests[0]!.method).toBe("PUT");
    expect(requests[0]!.url).toBe("http://handle.test/api/me/handle");
    expect(await requests[0]!.json()).toEqual({ handle: "Anna" });
  });

  it("raises the refusal with its code and the date it names", async () => {
    const client = createCichlidsClient({ baseUrl: "http://handle.test", fetch: async () =>
      Response.json({ title: "Handle changed recently", code: "handle_change_too_soon", nextChangeAt: "2026-11-16T10:00:00+00:00" }, {
        status: 429, headers: { "Content-Type": "application/problem+json" },
      }) });

    const error = await client.me.setHandle("anna").then(() => null, (e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(429);
    expect(handleProblemOf(error)).toEqual({ reason: "too_soon", nextChangeAt: "2026-11-16T10:00:00+00:00" });
  });
});
