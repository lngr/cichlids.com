import { SHARE_CARD_COLORS, SHARE_CARD_UNITS, SHARE_CARD_UNITS_HEIGHT, type ShareCardFigure, type ShareCardTexts } from "./shareCardModel";

/** The part of the card that shows a text or the photo; positions and sizes are in layout units. */
export type ShareCardItem =
  | { kind: "text"; text: string; x: number; y: number; width: number; height: number; size: number; weight: "400" | "700" | "800"; color: string; maxLines: number }
  | { kind: "photo"; x: number; y: number; width: number; height: number };

const PADDING = 28;
const CONTENT_WIDTH = SHARE_CARD_UNITS - 2 * PADDING;
const GAP = 22;
const HEADER_BOTTOM = 140;
const BOTTOM_MARGIN = 48;

/**
 * Places the texts and the photo of the card from top to bottom. Positions count from the start
 * edge, which is the left edge in a left-to-right language and the right edge in a right-to-left
 * one. Every text is centred in its box and the content below the title is centred vertically in
 * the space that is left. The photo is placed only when the card has one.
 */
export function layoutShareCard(texts: ShareCardTexts, { hasPhoto }: { hasPhoto: boolean }): ShareCardItem[] {
  const colors = SHARE_CARD_COLORS;
  const items: ShareCardItem[] = [];
  const text = (value: string, x: number, y: number, width: number, height: number, size: number, weight: "400" | "700" | "800", color: string, maxLines = 1) =>
    items.push({ kind: "text", text: value, x, y, width, height, size, weight, color, maxLines });
  text(texts.appName, PADDING, 36, CONTENT_WIDTH, 22, 16, "800", colors.accent);
  text(texts.title, PADDING, 70, CONTENT_WIDTH, 60, 24, "800", colors.text, 2);
  let y = 150;
  const figure = (entry: ShareCardFigure, x: number, width: number) => {
    text(entry.label, x, y, width, 16, 12, "700", colors.muted);
    text(entry.value, x, y + 18, width, 36, 28, "800", colors.accent);
  };
  if (texts.memberSince) {
    figure(texts.memberSince, PADDING, CONTENT_WIDTH);
    y += 54 + GAP;
  }
  const [first, second] = texts.counts;
  if (first && !second) {
    figure(first, PADDING, CONTENT_WIDTH);
    y += 54 + GAP;
  } else if (first && second) {
    const half = CONTENT_WIDTH / 2;
    figure(first, PADDING, half);
    figure(second, PADDING + half, half);
    y += 54 + GAP;
  }
  if (texts.mostViewed) {
    text(texts.mostViewed.label, PADDING, y, CONTENT_WIDTH, 16, 12, "700", colors.muted);
    y += 22;
    if (hasPhoto) {
      const height = (CONTENT_WIDTH * 3) / 4;
      items.push({ kind: "photo", x: PADDING, y, width: CONTENT_WIDTH, height });
      y += height + 8;
    }
    text(`${texts.mostViewed.views.value} ${texts.mostViewed.views.label}`, PADDING, y, CONTENT_WIDTH, 24, 17, "700", colors.text);
    y += 24 + GAP;
  }
  if (texts.handle) {
    text(texts.handle, PADDING, y, CONTENT_WIDTH, 22, 15, "700", colors.muted);
    y += 22 + GAP;
  }
  const header = 2;
  const bodyTop = 150;
  const bodyHeight = y - GAP - bodyTop;
  const shift = Math.max(0, Math.floor((SHARE_CARD_UNITS_HEIGHT - BOTTOM_MARGIN - HEADER_BOTTOM - bodyHeight) / 2));
  return items.map((item, index) => index < header ? item : { ...item, y: item.y + shift });
}

/** The items with their positions counted from the left edge: mirrored for a right-to-left language. */
export function mirrorForRtl(items: ShareCardItem[], rtl: boolean): ShareCardItem[] {
  return rtl ? items.map(item => ({ ...item, x: SHARE_CARD_UNITS - item.x - item.width })) : items;
}
