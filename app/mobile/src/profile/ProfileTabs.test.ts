import { describe, expect, it, vi } from "vitest";
import type { ReactElement } from "react";
import en from "../i18n/en.json";
import de from "../i18n/de.json";
const { language, scheme } = vi.hoisted(() => ({ language: { current: "en" }, scheme: { current: "light" } }));
vi.mock("react-native", () => ({ Pressable: "Pressable", Text: "Text", View: "View" }));
vi.mock("../theme", async (importOriginal) => {
  const theme = await importOriginal<typeof import("../theme")>();
  return { ...theme, useTheme: () => scheme.current === "dark" ? theme.darkTheme : theme.lightTheme };
});
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => {
  const resource = language.current === "en" ? en : de;
  return key.split(".").reduce<any>((value, part) => value[part], resource);
} }) }));
import { ProfileTabs, type ProfileSection } from "./ProfileTabs";
import { darkTheme, lightTheme } from "../theme";

describe("profile section tabs", () => {
  it("offers localized member sections with 48dp targets and announces the selected tab in both themes", () => {
    for (const locale of ["en", "de"]) {
      language.current = locale;
      for (const mode of ["light", "dark"]) {
        scheme.current = mode;
        const theme = mode === "light" ? lightTheme : darkTheme;
        const select = vi.fn();
        const element = ProfileTabs({ section: "pictures", onSelect: select, own: true });
        const tabs = (element.props as { children: ReactElement<any>[] }).children;
        expect(tabs.map((tab) => tab.props.children.props.children)).toEqual(locale === "en" ? ["Pictures", "Tanks", "Drafts"] : ["Bilder", "Becken", "Entwürfe"]);
        tabs.forEach((tab, index) => {
          expect(tab.props.style.minHeight).toBeGreaterThanOrEqual(48);
          expect(tab.props.accessibilityRole).toBe("tab");
          expect(tab.props.accessibilityState.selected).toBe(index === 0);
          expect(tab.props["aria-selected"]).toBe(index === 0);
          expect(tab.props.children.props.style[1].color).toBe(index === 0 ? theme.colors.onAccent : theme.colors.fg);
          tab.props.onPress();
          expect(select).toHaveBeenLastCalledWith((["pictures", "tanks", "drafts"] as ProfileSection[])[index]);
        });
      }
    }
  });
  it("offers only public sections on a public profile", () => {
    const element = ProfileTabs({ section: "tanks", onSelect: vi.fn() });
    expect((element.props as { children: ReactElement[] }).children).toHaveLength(2);
  });
});
