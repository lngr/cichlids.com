/**
 * Generated from design-system/tokens.json by scripts/tokens-to-theme.mjs.
 * Do not edit by hand; re-run `pnpm run tokens` after the tokens change.
 */

export const lightColors = {
  "bg": "#f1f6f8",
  "surface": "#ffffff",
  "surface2": "#f7fbfc",
  "border": "#dee2e7",
  "fg": "#192029",
  "muted": "#6e757e",
  "accent": "#007f8a",
  "accentWeak": "#d8f5f8",
  "accentMid": "#57b5bf",
  "onAccent": "#ffffff",
  "good": "#4a9a5e",
  "warn": "#d8953d",
  "danger": "#cc2823",
  "dangerWeak": "#ffe5e0",
  "placeholder": "#e6ecef"
} as const;

export const darkColors = {
  "bg": "#161e25",
  "surface": "#1e2830",
  "surface2": "#262f37",
  "border": "#3b444c",
  "fg": "#eff2f6",
  "muted": "#aeb5bd",
  "accent": "#49a1ab",
  "accentWeak": "#123941",
  "accentMid": "#3c878f",
  "onAccent": "#0a1d26",
  "good": "#60bb83",
  "warn": "#f7ac4d",
  "danger": "#f2716a",
  "dangerWeak": "#572825",
  "placeholder": "#2d343a"
} as const;

export type ColorKey = keyof typeof lightColors;
