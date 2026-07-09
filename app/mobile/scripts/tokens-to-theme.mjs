#!/usr/bin/env node
// Converts design-system/tokens.json (OKLCH authoring values) into a hex
// React Native theme module, since React Native's StyleSheet color parser
// does not understand the oklch() CSS function. Uses the standard OKLab/sRGB
// conversion (Bjorn Ottosson, https://bottosson.github.io/posts/oklab/).
import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const mobileRoot = fileURLToPath(new URL("..", import.meta.url));
const tokensPath = path.join(mobileRoot, "../../design-system/tokens.json");
const outPath = path.join(mobileRoot, "src/theme/colors.generated.ts");

function oklchToSrgbHex(input) {
  const match = input.match(/^oklch\(\s*([\d.]+)%\s+([\d.]+)\s+([\d.]+)\s*\)$/);
  if (!match) {
    // Already a hex/named color (e.g. "#ffffff") - pass through.
    return input;
  }
  const L = Number(match[1]) / 100;
  const C = Number(match[2]);
  const hDeg = Number(match[3]);
  const hRad = (hDeg * Math.PI) / 180;

  const a = C * Math.cos(hRad);
  const b = C * Math.sin(hRad);

  const l_ = L + 0.3963377774 * a + 0.2158037573 * b;
  const m_ = L - 0.1055613458 * a - 0.0638541728 * b;
  const s_ = L - 0.0894841775 * a - 1.291485548 * b;

  const l = l_ ** 3;
  const m = m_ ** 3;
  const s = s_ ** 3;

  let r = +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
  let g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
  let bl = -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s;

  const toSrgb = (c) => {
    const clamped = Math.min(1, Math.max(0, c));
    const v = clamped <= 0.0031308 ? clamped * 12.92 : 1.055 * clamped ** (1 / 2.4) - 0.055;
    return Math.round(Math.min(1, Math.max(0, v)) * 255);
  };

  const toHex = (n) => n.toString(16).padStart(2, "0");
  return `#${toHex(toSrgb(r))}${toHex(toSrgb(g))}${toHex(toSrgb(bl))}`;
}

function convertPalette(palette) {
  const out = {};
  for (const [key, value] of Object.entries(palette)) {
    out[key] = oklchToSrgbHex(value);
  }
  return out;
}

const tokens = JSON.parse(await readFile(tokensPath, "utf8"));
const light = convertPalette(tokens.color.light);
const dark = convertPalette(tokens.color.dark);

const body = `/**
 * Generated from design-system/tokens.json by scripts/tokens-to-theme.mjs.
 * Do not edit by hand; re-run \`pnpm run tokens\` after the tokens change.
 */

export const lightColors = ${JSON.stringify(light, null, 2)} as const;

export const darkColors = ${JSON.stringify(dark, null, 2)} as const;

export type ColorKey = keyof typeof lightColors;
`;

await writeFile(outPath, body, "utf8");
console.log(`Wrote ${path.relative(mobileRoot, outPath)}`);
