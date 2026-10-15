import { describe, expect, it } from "vitest";
import { createCichlidsClient } from "./client";

describe("Discover archive", () => {
  it("requests the region set through the generated SDK", async () => {
    let requested = "";
    const archive = { regions: [], globalPictures: [] };
    const client = createCichlidsClient({ baseUrl: "https://api.test", fetch: async input => {
      requested = (input as Request).url;
      return Response.json(archive);
    } });
    expect(await client.discover.get(["lake_malawi", "mixed"])).toEqual(archive);
    expect(requested).toBe("https://api.test/api/discover?regions=lake_malawi%2Cmixed");
  });
});
