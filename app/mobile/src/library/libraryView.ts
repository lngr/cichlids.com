import type { LibraryItem, LibraryKind, LibraryPlace } from "@cichlids/client-core";
import { formatDate } from "../lib/format";

/** Which kind of file the grid shows. */
export type KindFilter = "all" | LibraryKind;

/** Whether the grid shows files with a place, without one, or both. */
export type UseFilter = "all" | "linked" | "unlinked";

type Translate = (key: string, options?: Record<string, unknown>) => string;

/** The list parameters of a pair of filters; "all" leaves the parameter out. */
export function listParams(kind: KindFilter, use: UseFilter): { kind?: LibraryKind; linked?: boolean } {
  return {
    ...(kind === "all" ? {} : { kind }),
    ...(use === "all" ? {} : { linked: use === "linked" }),
  };
}

/** The line that names a place: its kind, the tank section for a tank, and the name when the place is the member's own. */
export function describePlace(place: LibraryPlace, t: Translate, language: string): string {
  const label = place.kind === "tank" && place.section
    ? t("library.place.tankSection", { section: t(`library.section.${place.section}`) })
    : t(`library.place.${place.kind}`);
  const when = place.at ? formatDate(place.at, language) : "";
  const name = [place.name, when].filter((part): part is string => !!part).join(", ");
  return name ? t("library.placeNamed", { place: label, name }) : label;
}

/** The label a screen reader reads for a tile of the grid. */
export function tileLabel(item: LibraryItem, t: Translate, language: string): string {
  return t(item.placeCount === 0 ? "library.tileUnused" : "library.tileUsed", {
    kind: t(`library.kind.${item.kind}`),
    date: formatDate(item.createdAt, language),
    places: item.placeCount,
  });
}

/** The address of the picture a tile shows: the small photo, or the poster of a video; null when there is none yet. */
export function tileImage(item: LibraryItem): string | null {
  if (item.kind === "video") return item.video?.posterUrl ?? null;
  return item.image?.small ?? item.image?.thumb ?? null;
}

/** The picture the detail shows: the large photo or the poster of a video. */
export function previewImage(item: LibraryItem): string | null {
  if (item.kind === "video") return item.video?.posterUrl ?? null;
  return item.image?.large ?? item.image?.medium ?? item.image?.small ?? item.image?.thumb ?? null;
}

/** A length in seconds as minutes and seconds, such as 1:05. */
export function formatClock(seconds: number): string {
  const whole = Math.max(0, Math.round(seconds));
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, "0")}`;
}
