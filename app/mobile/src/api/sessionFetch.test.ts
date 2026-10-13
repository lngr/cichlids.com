// Story: TASK-3.41
import { describe, expect, it, vi } from "vitest";
import { createCookieSessionFetch } from "./sessionFetch";

function setup(csrfToken: string | null, status = 200) {
  const sent: Request[] = [];
  const onUnauthorized = vi.fn();
  const sessionFetch = createCookieSessionFetch({
    fetch: (async (input: RequestInfo | URL, init?: RequestInit) => {
      sent.push(new Request(input, init));
      return new Response(null, { status });
    }) as typeof fetch,
    getCsrfToken: () => csrfToken,
    onUnauthorized,
  });
  return { sessionFetch, sent, onUnauthorized };
}

describe("createCookieSessionFetch", () => {
  it("sends the session cookie and no CSRF header with a read", async () => {
    const { sessionFetch, sent } = setup("csrf");

    await sessionFetch(new Request("https://cichlids.test/api/me"));

    expect(sent[0].credentials).toBe("include");
    expect(sent[0].headers.get("X-CSRF-Token")).toBeNull();
  });

  it("adds the CSRF token to a write", async () => {
    const { sessionFetch, sent } = setup("csrf");

    await sessionFetch(new Request("https://cichlids.test/api/uploads", { method: "POST", body: "x" }));

    expect(sent[0].credentials).toBe("include");
    expect(sent[0].headers.get("X-CSRF-Token")).toBe("csrf");
    expect(await sent[0].text()).toBe("x");
  });

  it("sends a write without a CSRF header while there is no session", async () => {
    const { sessionFetch, sent } = setup(null);

    await sessionFetch(new Request("https://cichlids.test/api/pictures/x/comments", { method: "POST", body: "{}" }));

    expect(sent[0].headers.get("X-CSRF-Token")).toBeNull();
  });

  it("reports a 401 answer as the end of the session", async () => {
    const { sessionFetch, onUnauthorized } = setup("csrf", 401);

    const response = await sessionFetch(new Request("https://cichlids.test/api/me"));

    expect(response.status).toBe(401);
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });

  it("leaves the session alone on other failures", async () => {
    const { sessionFetch, onUnauthorized } = setup("csrf", 503);

    await sessionFetch(new Request("https://cichlids.test/api/me"));

    expect(onUnauthorized).not.toHaveBeenCalled();
  });
});
