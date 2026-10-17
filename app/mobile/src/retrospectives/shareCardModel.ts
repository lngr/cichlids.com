import type { RetrospectiveCard } from "@cichlids/client-core";
import { cardImageUrl } from "./retrospectiveView";
import { lightColors } from "../theme/colors.generated";

/** The pixel size of the shared image: portrait with 9:16 proportions. */
export const SHARE_CARD_WIDTH = 1080;
export const SHARE_CARD_HEIGHT = 1920;

/** The design width of the card in layout units; every size of the card is a multiple of it. */
export const SHARE_CARD_UNITS = 360;

/** The height of the card in layout units. */
export const SHARE_CARD_UNITS_HEIGHT = (SHARE_CARD_UNITS * SHARE_CARD_HEIGHT) / SHARE_CARD_WIDTH;

/** The colours of the image. They are the light palette in every theme, so the image looks the same everywhere. */
export const SHARE_CARD_COLORS = {
  background: lightColors.bg,
  surface: lightColors.surface,
  text: lightColors.fg,
  muted: lightColors.muted,
  accent: lightColors.accent,
  onAccent: lightColors.onAccent,
  placeholder: lightColors.placeholder,
} as const;

/** What the card shows; a field is null when the retrospective has no value for it. */
export type ShareCardModel = {
  memberSince: { year: number; month: number } | null;
  photos: number | null;
  comments: number | null;
  mostViewed: { imageUrl: string | null; viewCount: number } | null;
  handle: string | null;
};

/**
 * The fields of the card from the cards of the member's own retrospective. The handle appears only
 * when the member chooses to show it and has one.
 */
export function buildShareCard(cards: RetrospectiveCard[], { handle, showHandle }: { handle: string | null; showHandle: boolean }): ShareCardModel {
  const memberSince = cards.find(card => card.kind === "member_since");
  const stats = cards.find(card => card.kind === "stats");
  const mostViewed = cards.find(card => card.kind === "most_viewed");
  const trimmed = handle?.trim() ?? "";
  return {
    memberSince: memberSince ? { year: memberSince.year, month: memberSince.month } : null,
    photos: stats && stats.photos > 0 ? stats.photos : null,
    comments: stats && stats.comments > 0 ? stats.comments : null,
    mostViewed: mostViewed ? { imageUrl: mostViewed.shareImageUrl ?? cardImageUrl(mostViewed.image) ?? null, viewCount: mostViewed.viewCount } : null,
    handle: showHandle && trimmed ? trimmed : null,
  };
}

/** The file name of the shared image. */
export function shareCardFileName(): string {
  return "cichlids-retrospective.png";
}

/** One labelled figure of the card, already formatted for the language of the member. */
export type ShareCardFigure = { label: string; value: string };

/** The texts of the card in the language of the member; figures without a value are absent. */
export type ShareCardTexts = {
  appName: string;
  title: string;
  memberSince: ShareCardFigure | null;
  counts: ShareCardFigure[];
  mostViewed: { label: string; views: ShareCardFigure } | null;
  handle: string | null;
};
