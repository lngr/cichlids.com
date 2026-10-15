import { describe, expect, it } from "vitest";
import { createCichlidsClient } from "./client";

describe("Today", () => {
  it("sends the member's local calendar date and authentication through the SDK", async () => {
    let request: Request | undefined;
    const today = { displayName: "Member", memberSinceYear: 2006, tanks: [], draftCount: 0, drafts: [], tankOfTheWeek: null };
    const client = createCichlidsClient({ baseUrl: "https://api.test", getAccessToken: () => "member-token", fetch: async input => {
      request = input as Request;
      return Response.json(today);
    } });
    expect(await client.me.today("2026-02-28")).toEqual(today);
    expect(request?.url).toBe("https://api.test/api/me/today?date=2026-02-28");
    expect(request?.headers.get("Authorization")).toBe("Bearer member-token");
  });
});
