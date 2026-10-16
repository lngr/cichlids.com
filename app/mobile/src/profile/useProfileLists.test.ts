import { describe, expect, it, vi } from "vitest";
const { listStories, listTanks, usePagedList } = vi.hoisted(() => ({ listStories: vi.fn(), listTanks: vi.fn(), usePagedList: vi.fn() }));
vi.mock("react", () => ({ useCallback: (fn: unknown) => fn }));
vi.mock("../api/client", () => ({ apiClient: { profiles: { listStories, listTanks } } }));
vi.mock("../hooks/usePagedList", () => ({ usePagedList }));
import { useProfileLists } from "./useProfileLists";

describe("profile lists", () => {
  it("binds both paged endpoints to the member profile id and resets on profile changes", async () => {
    usePagedList.mockReturnValueOnce("pictures").mockReturnValueOnce("tanks");
    expect(useProfileLists(28)).toEqual({ pictures: "pictures", tanks: "tanks" });
    const [fetchPictures, pictureDeps] = usePagedList.mock.calls[0];
    const [fetchTanks, tankDeps] = usePagedList.mock.calls[1];
    await fetchPictures(20, 20);
    await fetchTanks(40, 20);
    expect(listStories).toHaveBeenCalledWith(28, { offset: 20, limit: 20 });
    expect(listTanks).toHaveBeenCalledWith(28, { offset: 40, limit: 20 });
    expect(pictureDeps).toEqual([28]);
    expect(tankDeps).toEqual([28]);
    useProfileLists(41);
    expect(usePagedList.mock.calls[2][1]).toEqual([41]);
    expect(usePagedList.mock.calls[3][1]).toEqual([41]);
  });
});
