import { describe, expect, it, vi } from "vitest";
import type { ReactElement } from "react";
import type { StoryListItem, TankListItem } from "@cichlids/client-core";
import type { UsePagedListState } from "../hooks/usePagedList";

vi.mock("react-native", () => ({ FlatList: "FlatList", View: "View" }));
vi.mock("../theme", () => ({ useTheme: () => ({ colors: { bg: "background" }, space: { 6: 24 } }) }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
const push = vi.hoisted(() => vi.fn());
vi.mock("../navigation/NavigationProvider", () => ({ useMachineRouter: () => ({ push }) }));
vi.mock("../components/StoryCard", () => ({ StoryCard: "StoryCard" }));
vi.mock("../components/TankCard", () => ({ TankCard: "TankCard" }));
vi.mock("../components/StatusView", () => ({ LoadingView: "LoadingView", ErrorView: "ErrorView", EmptyView: "EmptyView" }));
import { ProfileContentList } from "./ProfileContentList";

function state<T>(overrides: Partial<UsePagedListState<T>> = {}): UsePagedListState<T> {
  return { items: [], total: 0, loading: false, loadingMore: false, error: null, hasMore: false, loadMore: vi.fn(), reload: vi.fn(), prepend: vi.fn(), ...overrides };
}
function propsOf(element: ReactElement) {
  return element.props as Record<string, any>;
}
describe("profile content lists", () => {
  it("shows loading, errors with retry, and the section's empty message", () => {
    for (const section of ["pictures", "tanks"] as const) {
      const retry = vi.fn();
      for (const [input, type] of [
        [{ loading: true }, "LoadingView"],
        [{ error: "Connection failed", reload: retry }, "ErrorView"],
        [{}, "EmptyView"],
      ] as const) {
        const list = ProfileContentList({ section, pictures: state(input), tanks: state(input), header: null });
        const empty = propsOf(list).ListEmptyComponent;
        expect(empty.type).toBe(type);
        if (type === "ErrorView") {
          expect(empty.props.message).toBe("Connection failed");
          empty.props.onRetry();
          expect(retry).toHaveBeenCalled();
        }
        if (type === "EmptyView") expect(empty.props.message).toBe(section === "pictures" ? "profile.emptyPictures" : "profile.emptyTanks");
      }
    }
  });
  it("pages the active section and opens the selected picture or tank", () => {
    const pictures = state<StoryListItem>();
    const tanks = state<TankListItem>();
    const header = null;
    const pictureList = propsOf(ProfileContentList({ section: "pictures", pictures, tanks, header }));
    expect(pictureList.ListHeaderComponent).toBe(header);
    expect(pictureList.numColumns).toBe(2);
    pictureList.onEndReached();
    expect(pictures.loadMore).toHaveBeenCalledOnce();
    expect(tanks.loadMore).not.toHaveBeenCalled();
    pictureList.renderItem({ item: { slug: "my-fish" } }).props.onPress();
    expect(push).toHaveBeenLastCalledWith("/gallery/my-fish");
    const tankList = propsOf(ProfileContentList({ section: "tanks", pictures, tanks, header }));
    tankList.renderItem({ item: { id: 28 } }).props.onPress();
    expect(push).toHaveBeenLastCalledWith("/tanks/28");
  });
  it("keeps loaded cards visible with a retry after a later page fails", () => {
    const reload = vi.fn();
    const pictures = state<StoryListItem>({ items: [{ slug: "fish" } as StoryListItem], error: "Offline", reload });
    const list = propsOf(ProfileContentList({ section: "pictures", pictures, tanks: state(), header: null }));
    expect(list.data).toBe(pictures.items);
    expect(list.ListFooterComponent.type).toBe("ErrorView");
    list.ListFooterComponent.props.onRetry();
    expect(reload).toHaveBeenCalledOnce();
  });
});
