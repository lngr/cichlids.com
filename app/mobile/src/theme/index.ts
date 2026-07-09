import { useColorScheme } from "react-native";
import { darkColors, lightColors } from "./colors.generated";

// Mirrors design-system/tokens.json radius/space/type scales (not generated:
// these are plain numbers already, no OKLCH conversion needed).
export const radius = { sm: 9, md: 12, lg: 16, sheet: 22, pill: 999 } as const;
export const space = { 1: 4, 2: 8, 3: 12, 4: 16, 6: 24, 8: 32 } as const;

export const type = {
  display: { fontSize: 28, fontWeight: "800" as const, letterSpacing: -0.6 },
  h1: { fontSize: 21, fontWeight: "800" as const, letterSpacing: -0.3 },
  h2: { fontSize: 17, fontWeight: "700" as const, letterSpacing: -0.15 },
  body: { fontSize: 15, fontWeight: "400" as const },
  bodyStrong: { fontSize: 15, fontWeight: "600" as const },
  meta: { fontSize: 12, fontWeight: "400" as const },
  eyebrow: { fontSize: 10.5, fontWeight: "700" as const, letterSpacing: 1.2, textTransform: "uppercase" as const },
};

// Touch targets follow the design system's a11y rule (>= 48dp).
export const minTouchTarget = 48;

export interface Theme {
  scheme: "light" | "dark";
  colors: Record<import("./colors.generated").ColorKey, string>;
  radius: typeof radius;
  space: typeof space;
  type: typeof type;
}

function buildTheme(scheme: "light" | "dark"): Theme {
  return {
    scheme,
    colors: scheme === "dark" ? darkColors : lightColors,
    radius,
    space,
    type,
  };
}

export const lightTheme = buildTheme("light");
export const darkTheme = buildTheme("dark");

/** Resolves the active theme from the OS color scheme (falls back to light). */
export function useTheme(): Theme {
  const scheme = useColorScheme();
  return scheme === "dark" ? darkTheme : lightTheme;
}
