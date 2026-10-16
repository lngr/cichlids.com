import type {
  CareKind, ImageUrlsDto, MediaKind, MediaLibraryDetailDto, MediaLibraryItemDto, MediaPlaceDto, MediaPlaceKind, StoryState, TankMediaSection, VideoDto,
} from "./generated/types.gen.js";

export type { MediaKind as LibraryKind, MediaPlaceKind as LibraryPlaceKind };

/**
 * A place that shows a file of the media library. Name and the ids are set for places of the member who owns
 * the file; storySlug addresses a published story.
 */
export type LibraryPlace = {
  kind: MediaPlaceKind;
  name: string | null;
  tankId: number | null;
  careEntryId: number | null;
  storyId: number | null;
  storySlug: string | null;
  section: TankMediaSection | null;
  careKind: CareKind | null;
  at: string | null;
};

/** The story that the quick share of a file created; slug is null while that story is a draft. */
export type LibraryQuickShare = { storyId: number; slug: string | null; state: StoryState };

/** A file of the media library with the number and kinds of places that show it. quickShare is set once the file was shared to the gallery. */
export type LibraryItem = {
  id: number;
  kind: MediaKind;
  createdAt: string;
  width: number | null;
  height: number | null;
  image: ImageUrlsDto | null;
  video: VideoDto | null;
  placeCount: number;
  placeKinds: MediaPlaceKind[];
  quickShare: LibraryQuickShare | null;
};

/** A time-lapse week of a tank that a file keeps: the file is the only in-app photo of that ISO week (weekStart is its Monday). */
export type LibraryStreakWeek = { tankId: number; tankTitle: string; weekStart: string };

/**
 * A file of the media library with its provenance and its places. canDelete is false for the proof photo of a
 * decommissioning. keepsStreak lists the weeks the file keeps; deleting it shortens or breaks the time-lapse streak of those tanks.
 */
export type LibraryDetail = {
  item: LibraryItem;
  originalFilename: string | null;
  byteSize: number | null;
  capturedInApp: boolean;
  receivedAt: string;
  takenAt: string | null;
  canDelete: boolean;
  places: LibraryPlace[];
  keepsStreak: LibraryStreakWeek[];
};

/** Which files a list shows: by kind and by whether a place shows them. Missing fields keep all. */
export type LibraryListParams = {
  kind?: MediaKind;
  linked?: boolean;
  offset?: number;
  limit?: number;
};

const orNull = (value: number | string | null | undefined): number | null => (value === null || value === undefined ? null : Number(value));

export const toLibraryPlace = (place: MediaPlaceDto): LibraryPlace => ({
  kind: place.kind,
  name: place.name,
  tankId: orNull(place.tankId),
  careEntryId: orNull(place.careEntryId),
  storyId: orNull(place.storyId),
  storySlug: place.storySlug,
  section: place.section,
  careKind: place.careKind,
  at: place.at,
});

export const toLibraryItem = (item: MediaLibraryItemDto): LibraryItem => ({
  id: Number(item.id),
  kind: item.kind,
  createdAt: item.createdAt,
  width: orNull(item.width),
  height: orNull(item.height),
  image: item.image,
  video: item.video,
  placeCount: Number(item.placeCount),
  placeKinds: item.placeKinds,
  quickShare: item.quickShare && { storyId: Number(item.quickShare.storyId), slug: item.quickShare.slug, state: item.quickShare.state },
});

export const toLibraryDetail = (detail: MediaLibraryDetailDto): LibraryDetail => ({
  item: toLibraryItem(detail.item),
  originalFilename: detail.originalFilename,
  byteSize: orNull(detail.byteSize),
  capturedInApp: detail.capturedInApp,
  receivedAt: detail.receivedAt,
  takenAt: detail.takenAt,
  canDelete: detail.canDelete,
  places: detail.places.map(toLibraryPlace),
  keepsStreak: detail.keepsStreak.map(week => ({ tankId: Number(week.tankId), tankTitle: week.tankTitle, weekStart: week.weekStart })),
});

/** The query values of a list request: the filter names the API understands. */
export function libraryQuery(params: LibraryListParams): { kind?: string; filter?: string; offset?: number; limit?: number } {
  return {
    ...(params.kind ? { kind: params.kind } : {}),
    ...(params.linked === undefined ? {} : { filter: params.linked ? "linked" : "unlinked" }),
    ...(params.offset === undefined ? {} : { offset: params.offset }),
    ...(params.limit === undefined ? {} : { limit: params.limit }),
  };
}
