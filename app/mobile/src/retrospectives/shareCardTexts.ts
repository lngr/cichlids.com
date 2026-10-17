import { memberSinceLabel } from "./retrospectiveView";
import type { ShareCardFigure, ShareCardModel, ShareCardTexts } from "./shareCardModel";

/** The name of the app on the card. */
export const SHARE_CARD_APP_NAME = "cichlids";

/**
 * The texts of the card in the language of the member: the labels from the catalog and the figures
 * formatted for the language. Figures without a value have no entry.
 */
export function shareCardTexts(model: ShareCardModel, t: (key: string) => string, language: string): ShareCardTexts {
  const number = new Intl.NumberFormat(language);
  const figures: ShareCardFigure[] = [];
  if (model.photos !== null) figures.push({ label: t("retrospective.share.photos"), value: number.format(model.photos) });
  if (model.comments !== null) figures.push({ label: t("retrospective.share.comments"), value: number.format(model.comments) });
  return {
    appName: SHARE_CARD_APP_NAME,
    title: t("retrospective.share.cardTitle"),
    memberSince: model.memberSince ? { label: t("retrospective.share.memberSince"), value: memberSinceLabel(model.memberSince.year, model.memberSince.month, language) } : null,
    counts: figures,
    mostViewed: model.mostViewed ? {
      label: t("retrospective.share.mostViewed"),
      views: { label: t("retrospective.share.views"), value: number.format(model.mostViewed.viewCount) },
    } : null,
    handle: model.handle ? `@${model.handle}` : null,
  };
}
