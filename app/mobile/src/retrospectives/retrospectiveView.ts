import type { Me, RetrospectiveImage } from "@cichlids/client-core";
import { resolveLanguage } from "../i18n/locales";

/** What a card offers the member to do next. */
export type RetrospectiveAction =
  | { type: "open_tank"; tankId: number }
  | { type: "open_story"; slug: string }
  | { type: "set_up_tank" };

/** The route an action leads to; creating a tank runs through the onboarding with its first photo. */
export function routeOfAction(action: RetrospectiveAction): string {
  switch (action.type) {
    case "open_tank": return `/tanks/${action.tankId}`;
    case "open_story": return `/gallery/${action.slug}`;
    case "set_up_tank": return "/onboarding";
  }
}

/** The month and year a member joined, in the language of the member. */
export function memberSinceLabel(year: number, month: number, language: string): string {
  return new Intl.DateTimeFormat(resolveLanguage(language), { year: "numeric", month: "long", timeZone: "UTC" })
    .format(new Date(Date.UTC(year, month - 1, 1)));
}

/** The URL of the largest size of a card image, or undefined when the card has no image. */
export function cardImageUrl(image: RetrospectiveImage | null | undefined): string | undefined {
  return image?.large ?? image?.medium ?? image?.small ?? image?.thumb ?? undefined;
}

/**
 * Whether the app opens the archive retrospective by itself: the member is registered, has one that
 * is unseen, and is not sent to set up the first tank at the same time.
 */
export function shouldOpenArchiveRetrospective({ status, provisional, firstTankOffered }: {
  status: Me["archiveRetrospective"];
  provisional: boolean;
  firstTankOffered: boolean;
}): boolean {
  return Boolean(status?.available) && !status?.seenAt && !provisional && !firstTankOffered;
}

/**
 * The step a tap on a card asks for: the leading third of the screen goes back, the rest goes
 * forward. A right-to-left layout leads on the right.
 */
export function tapDirection(x: number, width: number, rtl: boolean): "previous" | "next" {
  const leading = rtl ? x >= (width * 2) / 3 : x < width / 3;
  return leading ? "previous" : "next";
}
