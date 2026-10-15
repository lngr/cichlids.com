import { describe, expect, it, vi } from "vitest";
import type { ReactElement } from "react";
const state = vi.hoisted(() => ({ enabled: false, scheme: "light" }));
vi.mock("react-native", () => ({ Text: "Text", View: "View", useColorScheme: () => state.scheme }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("./FeatureFlagsProvider", () => ({ useFeatureFlag: (name: string) => {
  expect(name).toBe("discover-activity");
  return state.enabled;
} }));
import { DiscoverActivity } from "./DiscoverActivity";
import { darkTheme, lightTheme } from "../theme";

describe("discover activity placeholder", () => {
  it("omits the entire section when the flag is off", () => {
    state.enabled = false;
    expect(DiscoverActivity()).toBeNull();
  });
  it("uses theme tokens and a localized heading when the flag is on", () => {
    state.enabled = true;
    for (const scheme of ["light", "dark"]) {
      state.scheme = scheme;
      const theme = scheme === "light" ? lightTheme : darkTheme;
      const element = DiscoverActivity()!;
      expect(element.props.style.backgroundColor).toBe(theme.colors.surface);
      expect(element.props.style.padding).toBe(theme.space[4]);
      const [heading, body] = element.props.children as ReactElement<any>[];
      expect(heading.props.accessibilityRole).toBe("header");
      expect(heading.props.children).toBe("featureFlags.activityTitle");
      expect(body.props.children).toBe("featureFlags.activityPlaceholder");
      expect(heading.props.style[1].color).toBe(theme.colors.fg);
      expect(body.props.style[1].color).toBe(theme.colors.fg);
    }
  });
});
