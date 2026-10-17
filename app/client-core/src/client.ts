import { createClient, createConfig } from "./generated/client/index.js";
import type { Client } from "./generated/client/index.js";
import type { DeviceAuth } from "./deviceAuth.js";
import { REGISTRATION_REQUIRED_CODE, toRegistrationMove, toRegistrationPreview } from "./registration.js";
import type { RegistrationMove, RegistrationPreview } from "./registration.js";
import type { VideoUploadTransport } from "./videoUpload.js";
import { toCareHeatmap } from "./careHeatmap.js";
import type { CareHeatmap } from "./careHeatmap.js";
import { toTankOperation } from "./decommission.js";
import type { DecommissionScope, TankOperation } from "./decommission.js";
import { toLibraryDetail, toLibraryItem, toLibraryPlace, libraryQuery } from "./library.js";
import type { LibraryDetail, LibraryItem, LibraryListParams, LibraryPlace } from "./library.js";
import { toMyStory, toStoryEditor } from "./storyEditor.js";
import type { EditorEntry, MyStory, StoryEditor, StorySubjectRequest, StoryTextRequest } from "./storyEditor.js";
import { toFollowedTank, toMilestone, toTodayFollowed } from "./todaySections.js";
import type { FollowedTank, Milestone, TodayFollowed } from "./todaySections.js";
import { toMeStreaks, toTankStreaks, toVacation } from "./streaks.js";
import type { MeStreaks, TankStreaks, Vacation } from "./streaks.js";
import {
  addMyStoryItem,
  addMyStorySubject,
  createMyStory,
  deleteMyStory,
  getMyStory,
  listMyStories,
  dismissMilestone,
  getTodayFollowed,
  getTodayMilestones,
  listMyFollowedTanks,
  placeMyStorySubject,
  removeMyStoryItem,
  removeMyStorySubject,
  reorderMyStory,
  setMyStoryCover,
  setMyStoryItemCaption,
  shareMyMedia,
  updateMyStory,
  withdrawMyStory,
  logCareEntry,
  updateCareEntry,
  deleteCareEntry,
  listCareEntries,
  listCareSeries,
  createStoryComment,
  addCarePhoto,
  createCareEntryTodo,
  createTankTodo,
  startWaterChange,
  getCurrentWaterChange,
  markWaterChangeStep,
  addWaterChangePhoto,
  finishWaterChange,
  cancelWaterChange,
  deleteTodo,
  listMyTodos,
  removeCarePhoto,
  updateTodo,
  createTankComment,
  createTank,
  setTankVisibility,
  getTankFollow,
  followTank,
  unfollowTank,
  listTankFollowers,
  removeTankFollower,
  listTankFollowRequests,
  approveTankFollowRequest,
  declineTankFollowRequest,
  listTankSelectedMembers,
  addTankSelectedMember,
  removeTankSelectedMember,
  completeOnboarding,
  listMyLegacyTanks,
  setMyInterests,
  getMySettings,
  changeMyHandle,
  updateMySettings, setMyAvatar, setMyHeader, uploadMyAvatar, uploadMyHeader, removeMyAvatar, removeMyHeader,
  refineTankRegion,
  discardStory,
  getCarePlan,
  getTankStreaks,
  getMyStreaks,
  listMyVacations,
  startVacation,
  endVacation,
  getWebPushConfig,
  subscribeWebPush,
  unsubscribeWebPush,
  listDueCare,
  getCareHeatmap,
  getMyCareHeatmap,
  updateCarePlan,
  updateCarePlanRule,
  getCommunityThread,
  getFeatureFlags,
  getDiscover,
  getMe,
  moveProvisionalContent,
  previewProvisionalRegistration,
  upgradeProvisionalProfile,
  getToday,
  getStory,
  getProfile,
  getSpecies,
  getTank,
  getTankTimeLapse,
  addTankSeriesPhoto,
  getTankOperation,
  decommissionTank,
  recommissionTank,
  appendVideoUploadChunk,
  cancelVideoUpload,
  completeVideoUpload,
  createVideoUpload,
  getVideoUpload,
  listTankVideos,
  removeCareVideo,
  removeTankVideo,
  listCommunityCategories,
  listCommunityThreads,
  listMyDrafts,
  listMyMedia,
  getMyMedia,
  deleteMyMedia,
  uploadMyMediaPhoto,
  listStoryComments,
  listStories,
  listProfileStories,
  listProfileTanks,
  listSpecies,
  listTankComments,
  listTanks,
  publishStory,
  uploadPhoto,
} from "./generated/sdk.gen.js";
import type {
  CareDueDto,
  CarePhotoDto,
  CareTodoDto,
  WaterChangeFinishedDto,
  WaterChangeSessionDto,
  WaterChangeStep,
  WaterChangeStepState,
  TodoListItemDto,
  CareDueState,
  CareEntryDto,
  CareReasonDto,
  DueCareItemDto,
  DueStreakDto,
  CareKind,
  CarePlanDto,
  DueCareDto,
  CareAmountRequest,
  CareParameter,
  CareSeriesDto,
  LogCareRequest,
  MeasurementOrigin,
  MeasurementUnit,
  MeasurementValueDto,
  MeasurementValueRequest,
  DiscoverDto,
  DiscoverItemDto,
  CommentCreatedDto,
  CommentDto,
  CommunityAttachmentDto,
  CommunityCategoryDto,
  CommunityPostDto,
  CommunityThreadDetailDto,
  CommunityThreadListItemDto,
  CreateCommentRequest,
  CreateTankRequest,
  TankVisibility,
  TankVisibilityDto,
  TankFollowDto,
  TankFollowState,
  TankAudienceDto,
  TankMemberDto,
  OnboardingRequest,
  OnboardingResultDto,
  LegacyTankDto,
  TankRegion,
  DimensionsDto,
  DraftDto,
  VideoDto,
  VideoState,
  InhabitantDto,
  MeDto,
  HandleChangeDto,
  TodayDto,
  StoryDetailDto,
  StoryEditorDto,
  StoryItemDto,
  StoryListItemDto,
  StorySubjectDto,
  ProfileDetailDto,
  PublishStoryRequest,
  SpeciesDetailDto,
  SpeciesLinkDto,
  SpeciesListItemDto,
  TankDetailDto,
  TankTimeLapseDto,
  TankTimeLapsePhotoDto,
  TankListItemDto,
  TankMediaItemDto,
  WaterValuesDto,
} from "./generated/types.gen.js";

export type CareEntry = CareEntryDto;
export type CarePhoto = CarePhotoDto;
export type CareTodo = CareTodoDto;
export type WaterChangeSession = WaterChangeSessionDto;
export type WaterChangeFinished = WaterChangeFinishedDto;
export type { WaterChangeStep, WaterChangeStepState };
/** Which photo of a guided water change: the partly drained tank or the refilled tank. */
export type TodoListItem = TodoListItemDto;
/** Which to-dos a list shows: those still open, those marked done, or both. */
export type TodoState = "open" | "done" | "all";
export interface TodoListParams extends PageParams {
  state?: TodoState;
}
export type CareSeries = CareSeriesDto;
export type { CareDueState, CareKind };
export type CareReason = { code: string; parameters: Record<string, string> | null };
export type CareDue = { state: CareDueState; days: number; dueDate: string | null };
export type CarePlanRule = {
  kind: CareKind; rhythmDays: number | null; enabled: boolean; isDefault: boolean; reason: CareReason | null; due: CareDue | null;
  /** Whether a decommissioning of the tank pauses this kind of care; a paused rule has no due state. */
  paused: boolean;
};
export type CarePlan = { tankId: number; rules: CarePlanRule[]; remindersEnabled: boolean };
/**
 * The water change streak of a due water change as the reminders need it. While the streak runs,
 * reminderFrom and deadline are the first and the last local date of the daily reminders; a streak
 * paused by a vacation has no reminder days.
 */
export type DueStreak = { state: "running" | "paused"; reminderFrom: string | null; deadline: string | null };
export type DueCareItem = {
  tankId: number; tankTitle: string; kind: CareKind; rhythmDays: number; reason: CareReason; due: CareDue; remindersEnabled: boolean;
  /** Set for a water change whose streak runs or is paused; null otherwise. */
  streak?: DueStreak | null;
};
export type CarePlanChange = { rhythmDays?: number; enabled?: boolean };
/** The member's local calendar date (YYYY-MM-DD) and, optionally, the offset of the clock from UTC in minutes. */
export type LocalTime = { date: string; utcOffsetMinutes?: number };

/** Whether the server sends Web Push messages and the VAPID public key a browser subscribes with. */
export type WebPushConfig = { available: boolean; publicKey: string | null };

/** The subscription a browser holds for Web Push, with the IANA time zone in which its reminders fall due. */
export type WebPushSubscriptionRequest = { endpoint: string; keys: { p256dh: string; auth: string }; timeZone: string };

/** A subscription stored on the server. */
export type WebPushSubscriptionRecord = { id: number; timeZone: string };
export type MeasurementValue = MeasurementValueDto;
export type { CareAmountRequest, CareParameter, LogCareRequest, MeasurementOrigin, MeasurementUnit, MeasurementValueRequest };
export type CareSeriesRange = { from?: string; to?: string };
export type DiscoverArchive = DiscoverDto;
export type DiscoverItem = DiscoverItemDto;
export type StoryListItem = StoryListItemDto;
export type StoryDetail = StoryDetailDto;
export type StoryItem = StoryItemDto;
export type StorySubject = StorySubjectDto;
export type TankListItem = TankListItemDto;
export type TankDetail = TankDetailDto;
export type TankTimeLapse = TankTimeLapseDto;
export type TankTimeLapsePhoto = TankTimeLapsePhotoDto;
export type TankMediaItem = TankMediaItemDto;
export type Dimensions = DimensionsDto;
export type WaterValues = WaterValuesDto;
export type Inhabitant = InhabitantDto;
export type ProfileDetail = ProfileDetailDto;
/** The two images of a profile: the round avatar and the wide header. */
export type ProfileImageRole = "avatar" | "header";
export type SpeciesListItem = SpeciesListItemDto;
export type SpeciesDetail = SpeciesDetailDto;
export type CommunityCategory = CommunityCategoryDto;
export type CommunityThreadListItem = CommunityThreadListItemDto;
export type CommunityThreadDetail = CommunityThreadDetailDto;
export type { CommunityPostDto };
export type CommunityAttachment = CommunityAttachmentDto;
export type SpeciesLink = SpeciesLinkDto;
export type Comment = CommentDto;
export type CommentCreated = CommentCreatedDto;
export type { CreateTankRequest, CreateCommentRequest, PublishStoryRequest, TankVisibility, TankVisibilityDto };
export type TankFollow = TankFollowDto;
export type TankAudience = TankAudienceDto;
export type TankMember = TankMemberDto;
export type { TankFollowState };
export type { OnboardingRequest, OnboardingResultDto, LegacyTankDto, TankRegion };
export type Me = MeDto;
/** The settings of the member; streaksEnabled tells whether the streaks are shown to the member and to other viewers. */
export type MeSettings = { streaksEnabled: boolean };
/** The public handle of the member after a change and the earliest time of the next change. */
export type HandleChange = HandleChangeDto;
export type Today = TodayDto;
export type Draft = DraftDto;
export type Video = VideoDto;
export type { VideoState };
export type PagedResponse<T> = { total: number; items: T[] };

/**
 * A file for uploads.create: a Blob or File on the web, or React Native's
 * { uri, name, type } object, which its FormData implementation accepts
 * directly as a multipart part.
 */
export type UploadFile = Blob | { uri: string; name: string; type: string };

export interface CichlidsClientOptions {
  /** Base URL of the Cichlids.Api instance, e.g. http://localhost:5045 */
  baseUrl: string;
  /** Resolves the current access token; omit for anonymous (read-only) usage. */
  getAccessToken?: () => string | undefined | Promise<string | undefined>;
  /** Override fetch, e.g. for React Native or test environments. */
  fetch?: typeof fetch;
  /**
   * The token source of an installation without an account. A request that has no access token
   * from getAccessToken carries the device token; a source that yields no token (undefined)
   * leaves the request without one. Omit it for clients that never run without a sign-in.
   */
  deviceAuth?: DeviceTokenSource;
  /**
   * Whether a session of the account authenticates the requests without a bearer token, as the
   * cookie session of the web app does. A registration call needs either this or an access token.
   */
  accountSession?: () => boolean | Promise<boolean>;
  /**
   * Called when the API answers a write with registration_required, the answer to an action only
   * a registered account may take.
   */
  onRegistrationRequired?: () => void;
}

/** What the client needs from the device authentication of an installation. */
export interface DeviceTokenSource {
  getAccessToken(): Promise<string | undefined>;
  signProof?: DeviceAuth["signProof"];
  /** Drops the cached token; called when the API answers 401 to a request that carried it. */
  invalidate?: DeviceAuth["invalidate"];
}

export interface StoriesListParams {
  sort?: "newest" | "views" | "rating";
  topic?: string;
  user?: number;
  species?: string;
  offset?: number;
  limit?: number;
}

export interface TanksListParams {
  user?: number;
  category?: string;
  offset?: number;
  limit?: number;
}

export interface SpeciesListParams {
  query?: string;
  offset?: number;
  limit?: number;
}

export interface CommunityThreadsListParams {
  category?: string;
  query?: string;
  offset?: number;
  limit?: number;
}

export interface PageParams {
  offset?: number;
  limit?: number;
}

/** An API error with the HTTP status and, for problem details bodies, the machine-readable code. */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code?: string,
    /** The members of the problem details body beyond the standard ones and the code, such as the date a refusal names; undefined when there are none. */
    public readonly problem?: Readonly<Record<string, unknown>>,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

const STANDARD_PROBLEM_MEMBERS = new Set(["type", "title", "status", "detail", "instance", "code", "traceId"]);

function problemExtensionsOf(error: unknown): Record<string, unknown> | undefined {
  if (typeof error !== "object" || error === null) return undefined;
  const extensions = Object.entries(error).filter(([name]) => !STANDARD_PROBLEM_MEMBERS.has(name));
  return extensions.length > 0 ? Object.fromEntries(extensions) : undefined;
}

function problemDetailsOf(error: unknown): { message?: string; code?: string } {
  if (typeof error !== "object" || error === null) return {};
  const { detail, title, code } = error as { detail?: unknown; title?: unknown; code?: unknown };
  const message = [detail, title].find((text): text is string => typeof text === "string" && text !== "");
  return { message, code: typeof code === "string" ? code : undefined };
}

/**
 * Throws for any non-2xx response with the response body's error text, if
 * present. A problem details body supplies the message (detail, else title)
 * and the code. `response` is optional because the generated client also reaches
 * this path for errors raised while building the request itself, before any
 * response exists (e.g. a network failure).
 */
function unwrap<T>({ data, error, response }: { data?: T; error?: unknown; response?: Response }): T {
  if (error !== undefined) {
    const problem = problemDetailsOf(error);
    const message = typeof error === "string" ? error : (problem.message ?? response?.statusText ?? "Network error");
    throw new ApiError(message, response?.status ?? 0, problem.code, problemExtensionsOf(error));
  }
  if (data === undefined) {
    throw new ApiError("Empty response body", response?.status ?? 0);
  }
  return data;
}

// The API's generated schema types every int32/int64 field as `number | string`:
// the OpenAPI document marks integers wire-compatible with numeric strings for
// lenient request parsing, but response bodies always carry plain JSON numbers.
// `total` is the one such field this package's own PagedResponse type narrows
// to `number`, since callers page through results by arithmetic on it.
function unwrapPaged<T>(result: { data?: { total: number | string; items: T[] }; error?: unknown; response?: Response }): PagedResponse<T> {
  const body = unwrap(result);
  return { total: Number(body.total), items: body.items };
}

/**
 * The paths of private media (photo variants and video files behind signed tokens) are
 * relative to the API; every such string anywhere in a response becomes an absolute URL, so an image or
 * video player can load it. Public URLs are already absolute and stay as they are.
 */
const MEDIA_PATH = /^\/api\/media\/(photos|videos)\//;
function absolutizeMedia<T>(value: T, baseUrl: string): T {
  const root = baseUrl.replace(/\/+$/, "");
  const visit = (item: unknown): unknown => {
    if (typeof item === "string") return MEDIA_PATH.test(item) ? `${root}${item}` : item;
    if (Array.isArray(item)) return item.map(visit);
    if (item !== null && typeof item === "object") return Object.fromEntries(Object.entries(item).map(([key, entry]) => [key, visit(entry)]));
    return item;
  };
  return visit(value) as T;
}

const toDue = (due: CareDueDto): CareDue => ({ state: due.state, days: Number(due.days), dueDate: due.dueDate });
const toReason = (reason: CareReasonDto): CareReason => ({ code: reason.code, parameters: reason.parameters });
const toPlan = (plan: CarePlanDto): CarePlan => ({
  tankId: Number(plan.tankId),
  rules: plan.rules.map(rule => ({
    kind: rule.kind, rhythmDays: rule.rhythmDays === null ? null : Number(rule.rhythmDays), enabled: rule.enabled,
    isDefault: rule.isDefault, reason: rule.reason && toReason(rule.reason), due: rule.due && toDue(rule.due), paused: rule.paused === true,
  })),
  remindersEnabled: plan.remindersEnabled,
});
const toDueStreak = (streak: DueStreakDto): DueStreak => ({
  state: streak.state === "paused" ? "paused" : "running", reminderFrom: streak.reminderFrom ?? null, deadline: streak.deadline ?? null,
});
const toDueItem = (item: DueCareItemDto): DueCareItem => ({
  tankId: Number(item.tankId), tankTitle: item.tankTitle, kind: item.kind, rhythmDays: Number(item.rhythmDays),
  reason: toReason(item.reason), due: toDue(item.due), remindersEnabled: item.remindersEnabled,
  streak: item.streak ? toDueStreak(item.streak) : null,
});

export interface CichlidsClient {
  care: {
    list(tankId: number, params?: PageParams): Promise<PagedResponse<CareEntry>>;
    log(tankId: number, request?: LogCareRequest): Promise<CareEntry>;
    /** Replaces or clears the amount and, when given, moves the occurred time; a null amount with a time moves only the time. */
    update(tankId: number, id: number, amount: CareAmountRequest | null, occurredAt?: string): Promise<CareEntry>;
    updateValues(tankId: number, id: number, values: MeasurementValueRequest[], occurredAt?: string): Promise<CareEntry>;
    /** The series of every parameter that has points in the range, with the points oldest first. */
    allSeries(tankId: number, range?: CareSeriesRange): Promise<CareSeries[]>;
    remove(tankId: number, id: number): Promise<CareEntry>;
    /** Sets the note of an entry; null or blank removes it. */
    setNote(tankId: number, id: number, note: string | null): Promise<CareEntry>;
    /** Marks an entry as seen by the owner alone, or shows it to the audience of the care stream again. */
    setOnlyMe(tankId: number, id: number, onlyMe: boolean): Promise<CareEntry>;
    addPhoto(tankId: number, id: number, file: UploadFile): Promise<CareEntry>;
    removePhoto(tankId: number, id: number, photoId: number): Promise<CareEntry>;
    addTodo(tankId: number, id: number, text: string): Promise<CareTodo>;
    /** The absolute URL for a photo path of a care entry. */
    photoUrl(path: string): string;
    plan(tankId: number, local: LocalTime): Promise<CarePlan>;
    changeRule(tankId: number, kind: CareKind, change: CarePlanChange, local: LocalTime): Promise<CarePlan>;
    /** Switches the care reminders of a tank on or off for every device of the member. */
    setReminders(tankId: number, enabled: boolean, local: LocalTime): Promise<CarePlan>;
    due(local: LocalTime): Promise<DueCareItem[]>;
    /** The care days of the last 16 weeks of a tank whose care stream the viewer sees; local is the date of the viewer. */
    heatmap(tankId: number, local: LocalTime): Promise<CareHeatmap>;
    /** The care days of the last 16 weeks of the own tanks, summed. */
    ownHeatmap(local: LocalTime): Promise<CareHeatmap>;
  };
  waterChange: {
    /** Starts a guided water change on an own tank, or returns the open one. */
    start(tankId: number): Promise<WaterChangeSession>;
    /** The open session of a tank; null when none is open. */
    current(tankId: number): Promise<WaterChangeSession | null>;
    markStep(tankId: number, id: number, step: WaterChangeStep, state: WaterChangeStepState): Promise<WaterChangeSession>;
    /** Stores the photo of a slot; capturedInApp tells that the camera of the app took it. */
    addPhoto(tankId: number, id: number, file: UploadFile, capturedInApp: boolean): Promise<WaterChangeSession>;
    finish(tankId: number, id: number, amount: CareAmountRequest | null): Promise<WaterChangeFinished>;
    cancel(tankId: number, id: number): Promise<void>;
  };
  webPush: {
    config(): Promise<WebPushConfig>;
    /** Stores the subscription of this browser, or updates the one stored for its endpoint. */
    subscribe(subscription: WebPushSubscriptionRequest): Promise<WebPushSubscriptionRecord>;
    /** Deletes the subscription of the endpoint; an unknown endpoint changes nothing. */
    unsubscribe(endpoint: string): Promise<void>;
  };
  todos: {
    list(params?: TodoListParams): Promise<PagedResponse<TodoListItem>>;
    /** Adds a to-do that belongs to a tank only. */
    add(tankId: number, text: string): Promise<CareTodo>;
    setDone(id: number, done: boolean): Promise<CareTodo>;
    remove(id: number): Promise<CareTodo>;
  };
  discover: {
    get(regions?: TankRegion[]): Promise<DiscoverArchive>;
  };
  featureFlags: {
    get(): Promise<Record<string, boolean>>;
  };
  stories: {
    list(params?: StoriesListParams): Promise<PagedResponse<StoryListItem>>;
    get(slug: string): Promise<StoryDetail>;
    listComments(slug: string, params?: PageParams): Promise<PagedResponse<Comment>>;
    createComment(slug: string, request: CreateCommentRequest): Promise<CommentCreated>;
    publish(id: number, request: PublishStoryRequest): Promise<StoryDetail>;
    discard(id: number): Promise<void>;
  };
  tanks: {
    create(request: CreateTankRequest): Promise<TankDetail>;
    refineRegion(id: number, region: TankRegion): Promise<void>;
    /** Sets the profile and stream level of an own tank; the stream is never wider than the profile. */
    setVisibility(id: number, levels: TankVisibilityDto): Promise<TankVisibilityDto>;
    /** The follow relation of the caller to a tank, with the teaser of a tank for followers that the caller does not see yet. */
    follow(id: number): Promise<TankFollow>;
    /** Follows a public tank at once, or sends a follow request to a tank for followers. */
    requestFollow(id: number): Promise<TankFollow>;
    /** Stops following a tank or withdraws the open follow request. */
    unfollow(id: number): Promise<void>;
    /** The approved followers of an own tank. */
    followers(id: number, params?: PageParams): Promise<PagedResponse<TankMember>>;
    /** Removes an approved follower from an own tank. */
    removeFollower(id: number, profileId: number): Promise<void>;
    /** The open follow requests of an own tank. */
    followRequests(id: number, params?: PageParams): Promise<PagedResponse<TankMember>>;
    approveFollowRequest(id: number, profileId: number): Promise<void>;
    declineFollowRequest(id: number, profileId: number): Promise<void>;
    /** The selected members of an own tank. */
    selectedMembers(id: number): Promise<TankMember[]>;
    /** Selects the member with the handle (the username) for an own tank. */
    addSelectedMember(id: number, handle: string): Promise<TankMember>;
    removeSelectedMember(id: number, profileId: number): Promise<void>;
    list(params?: TanksListParams): Promise<PagedResponse<TankListItem>>;
    get(id: number): Promise<TankDetail>;
    timeLapse(id: number): Promise<TankTimeLapse>;
    /** Adds a photo to the time-lapse series of an own tank. */
    /** Adds a photo to the series; capturedInApp tells that the camera of the app took it, which the time-lapse streak requires. */
    addTimeLapsePhoto(id: number, file: UploadFile, capturedInApp?: boolean): Promise<TankTimeLapsePhoto>;
    /** The streaks of a tank a viewer may see; local is the date of the viewer. */
    streaks(id: number, local: LocalTime): Promise<TankStreaks>;
    /** The operating time and the decommissionings of a tank whose care stream the viewer sees. */
    operation(id: number, local: LocalTime): Promise<TankOperation>;
    /**
     * Decommissions an own tank with the photo of the empty tank; capturedInApp tells that the camera of
     * the app took it, which the decommissioning requires.
     */
    decommission(id: number, file: UploadFile, options: { scope: DecommissionScope; capturedInApp: boolean }, local: LocalTime): Promise<TankOperation>;
    /** Puts an own decommissioned tank back into operation with the photo of the filled tank. */
    recommission(id: number, file: UploadFile, capturedInApp: boolean, local: LocalTime): Promise<TankOperation>;
    listComments(id: number, params?: PageParams): Promise<PagedResponse<Comment>>;
    createComment(id: number, request: CreateCommentRequest): Promise<CommentCreated>;
  };
  profiles: {
    get(id: number): Promise<ProfileDetail>;
    listStories(id: number, params?: PageParams): Promise<PagedResponse<StoryListItem>>;
    listTanks(id: number, params?: PageParams): Promise<PagedResponse<TankListItem>>;
  };
  species: {
    list(params?: SpeciesListParams): Promise<PagedResponse<SpeciesListItem>>;
    get(idOrSlug: string): Promise<SpeciesDetail>;
  };
  community: {
    listCategories(): Promise<CommunityCategory[]>;
    listThreads(params?: CommunityThreadsListParams): Promise<PagedResponse<CommunityThreadListItem>>;
    getThread(id: number): Promise<CommunityThreadDetail>;
  };
  /**
   * Hands the content of the installation to the account that is signed in. Every call needs the
   * Keycloak session (getAccessToken) and the device key (deviceAuth) together and fails without
   * either.
   */
  registration: {
    /** What handing the content over does, asked before the member decides; changes nothing. */
    preview(local: LocalTime): Promise<RegistrationPreview>;
    /** The account has no profile: the profile of the installation becomes it and keeps all content. */
    upgrade(): Promise<Me>;
    /** The account has a profile: moves the content into it and removes the profile of the installation. */
    /** local is the date of the member, against which moved vacations are judged. */
    move(local: LocalTime): Promise<RegistrationMove>;
  };
  me: {
    get(): Promise<Me>;
    today(date: string): Promise<Today>;
    /** The followed tanks with a visible update in the last 30 days, newest first, at most five. */
    followed(): Promise<TodayFollowed>;
    /** All followed tanks the member sees, those with the newest update first. */
    followedTanks(params?: PageParams): Promise<PagedResponse<FollowedTank>>;
    /** The milestones of the own tanks reached within the last 7 days that are not dismissed. */
    milestones(local: LocalTime): Promise<Milestone[]>;
    /** Dismisses a milestone for good. */
    dismissMilestone(key: string): Promise<void>;
    completeOnboarding(request: OnboardingRequest): Promise<OnboardingResultDto>;
    legacyTanks(): Promise<LegacyTankDto[]>;
    setInterests(interests: string[]): Promise<string[]>;
    drafts(): Promise<Draft[]>;
    /** The settings of the member. */
    settings(): Promise<MeSettings>;
    /** Turns the streaks of the member on or off. */
    setStreaksEnabled(streaksEnabled: boolean): Promise<MeSettings>;
    /**
     * Changes the public handle. The API lowercases it and refuses an unusable one with a problem
     * code that handleProblemOf names; me().handleChangeAvailableAt tells when the next change is possible.
     */
    setHandle(handle: string): Promise<HandleChange>;
    /** Chooses a photo of the media library as avatar or header of the profile; resolves with the profile. */
    setProfileImage(role: ProfileImageRole, mediaItemId: number): Promise<ProfileDetail>;
    /** Stores a fresh photo in the media library and chooses it as avatar or header of the profile. */
    uploadProfileImage(role: ProfileImageRole, file: UploadFile): Promise<ProfileDetail>;
    /** Removes the avatar or the header from the profile; the file stays in the media library. */
    removeProfileImage(role: ProfileImageRole): Promise<ProfileDetail>;
    /** The streaks of every tank of the member with the vacations of the member. */
    streaks(local: LocalTime): Promise<MeStreaks>;
    /** The vacations of the member, the newest first. */
    vacations(): Promise<Vacation[]>;
    /** Starts an active vacation on the local date of today, or on startDate when it lies earlier. */
    startVacation(local: LocalTime, startDate?: string): Promise<Vacation>;
    /** Records an ended vacation afterwards; both dates lie on or before the local date of today. */
    recordVacation(local: LocalTime, startDate: string, endDate: string): Promise<Vacation>;
    /** Ends the active vacation on the local date of today. */
    endVacation(date: string): Promise<Vacation>;
  };
  uploads: {
    create(file: UploadFile): Promise<Draft>;
  };
  /** The media library of the member: every file the member owns, with or without a place that shows it. */
  library: {
    /** The files newest first, narrowed by kind and by whether a place shows them. */
    list(params?: LibraryListParams): Promise<PagedResponse<LibraryItem>>;
    /** A file with its provenance, the places that show it and the streak weeks it keeps on the local date. */
    get(id: number, local?: LocalTime): Promise<LibraryDetail>;
    /** Deletes the file with its renditions and all its links; resolves with the places it was shown in. */
    remove(id: number): Promise<LibraryPlace[]>;
    /** Stores a photo in the library without a place. */
    uploadPhoto(file: UploadFile): Promise<LibraryItem>;
    /** Shares an own photo or video to the gallery: creates and publishes a story with the file as its only item. A file has one such story. */
    share(id: number, request: PublishStoryRequest): Promise<StoryDetail>;
  };
  /** The stories of the member and the editor that builds them. */
  storyEditor: {
    /** The stories of the member, the most recently changed first. */
    list(params?: PageParams): Promise<PagedResponse<MyStory>>;
    create(request?: StoryTextRequest): Promise<StoryEditor>;
    get(id: number): Promise<StoryEditor>;
    updateText(id: number, request: StoryTextRequest): Promise<StoryEditor>;
    /** Deletes a draft with its links or removes a published story from the gallery; the files stay in the library. */
    remove(id: number): Promise<void>;
    /** Makes a published story a draft again. */
    withdraw(id: number): Promise<StoryEditor>;
    /** Adds an own file as the last entry; the caption belongs to this story only. */
    addItem(id: number, mediaItemId: number, caption?: string | null): Promise<StoryEditor>;
    setCaption(id: number, itemId: number, caption: string | null): Promise<StoryEditor>;
    /** Removes the item from the story; the file stays in the library. */
    removeItem(id: number, itemId: number): Promise<StoryEditor>;
    /** Sets the sequence: every item and every positioned subject exactly once. */
    reorder(id: number, entries: EditorEntry[]): Promise<StoryEditor>;
    /** Chooses the cover among the items; null makes the first item the cover. */
    setCover(id: number, itemId: number | null): Promise<StoryEditor>;
    addSubject(id: number, request: StorySubjectRequest): Promise<StoryEditor>;
    placeSubject(id: number, subjectId: number, positioned: boolean): Promise<StoryEditor>;
    removeSubject(id: number, subjectId: number): Promise<StoryEditor>;
  };
  videos: {
    /** The videos of a tank: the ready ones for everybody, every state for the owner. */
    listForTank(tankId: number): Promise<Video[]>;
    removeFromTank(tankId: number, mediaItemId: number): Promise<void>;
    removeFromCareEntry(tankId: number, entryId: number, mediaItemId: number): Promise<CareEntry>;
    /** The calls of the chunked upload, for uploadVideo. */
    uploadTransport: VideoUploadTransport;
    cancelUpload(id: number): Promise<void>;
    /** The absolute URL for a playlist or poster path of a care video. */
    careVideoUrl(path: string): string;
  };
}

/**
 * Builds a typed client for the Cichlids.Api HTTP surface. Types come from
 * ./generated/types.gen.ts (@hey-api/openapi-ts output); requests run through
 * the generated fetch-based SDK in ./generated/sdk.gen.ts.
 */
export function createCichlidsClient(options: CichlidsClientOptions): CichlidsClient {
  const client: Client = createClient(
    createConfig({
      baseUrl: options.baseUrl,
      fetch: options.fetch,
    }),
  );

  // The requests that carry a device token, so a 401 answer drops exactly that token.
  const deviceTokenRequests = new WeakSet<Request>();
  if (options.getAccessToken || options.deviceAuth) {
    client.interceptors.request.use(async (request) => {
      let token = await options.getAccessToken?.();
      if (!token) {
        token = await options.deviceAuth?.getAccessToken();
        if (token) deviceTokenRequests.add(request);
      }
      if (token) {
        request.headers.set("Authorization", `Bearer ${token}`);
      }
      return request;
    });
  }
  if (options.deviceAuth?.invalidate || options.onRegistrationRequired) {
    client.interceptors.response.use(async (response, request) => {
      if (response.status === 401 && deviceTokenRequests.has(request)) options.deviceAuth?.invalidate?.();
      if (response.status === 403 && options.onRegistrationRequired && request.method !== "GET" && request.method !== "HEAD") {
        const body = (await response.clone().json().catch(() => null)) as { code?: unknown } | null;
        if (body?.code === REGISTRATION_REQUIRED_CODE) options.onRegistrationRequired();
      }
      return response;
    });
  }

  const editorOf = (result: { data?: StoryEditorDto; error?: unknown; response?: Response }): StoryEditor =>
    absolutizeMedia(toStoryEditor(unwrap(result)), options.baseUrl);

  /** The signed challenge of the installation and the Keycloak token of the account, which a registration call sends together. */
  const registrationProof = async () => {
    const signProof = options.deviceAuth?.signProof;
    if (!signProof) throw new Error("Handing content to an account needs the device authentication of the installation.");
    if (!(await options.getAccessToken?.()) && !(await options.accountSession?.())) throw new ApiError("Sign in with the account first.", 401, "keycloak_session_required");
    return signProof();
  };

  return {
    registration: {
      async preview(local) {
        return toRegistrationPreview(unwrap(await previewProvisionalRegistration({ client, query: local, body: await registrationProof() })));
      },
      async upgrade() {
        return unwrap(await upgradeProvisionalProfile({ client, body: await registrationProof() }));
      },
      async move(local) {
        return toRegistrationMove(unwrap(await moveProvisionalContent({ client, query: local, body: await registrationProof() })));
      },
    },
    care: {
      async list(tankId, params) { return unwrapPaged(await listCareEntries({ client, path: { tankId }, query: params ?? {} })); },
      async log(tankId, request) { return unwrap(await logCareEntry({ client, path: { tankId }, body: request ?? {} })); },
      async update(tankId, id, amount, occurredAt) {
        return unwrap(await updateCareEntry({ client, path: { tankId, id }, body: occurredAt ? { amount, occurredAt } : { amount } }));
      },
      async updateValues(tankId, id, values, occurredAt) {
        return unwrap(await updateCareEntry({ client, path: { tankId, id }, body: occurredAt ? { amount: null, values, occurredAt } : { amount: null, values } }));
      },
      async allSeries(tankId, range) { return unwrap(await listCareSeries({ client, path: { tankId }, query: range })).series; },
      async remove(tankId, id) { return unwrap(await deleteCareEntry({ client, path: { tankId, id } })); },
      async setOnlyMe(tankId, id, onlyMe) { return unwrap(await updateCareEntry({ client, path: { tankId, id }, body: { amount: null, onlyMe } })); },
      async setNote(tankId, id, note) { return unwrap(await updateCareEntry({ client, path: { tankId, id }, body: { amount: null, note: note?.trim() ?? "" } })); },
      async addPhoto(tankId, id, file) {
        return unwrap(await addCarePhoto({
          client, path: { tankId, id }, body: { file: file as never },
          bodySerializer: () => {
            const formData = new FormData();
            formData.append("file", file as never);
            return formData;
          },
        }));
      },
      async removePhoto(tankId, id, photoId) { return unwrap(await removeCarePhoto({ client, path: { tankId, id, photoId } })); },
      async addTodo(tankId, id, text) { return unwrap(await createCareEntryTodo({ client, path: { tankId, id }, body: { text } })); },
      photoUrl(path) { return `${options.baseUrl.replace(/\/+$/, "")}${path}`; },
      async plan(tankId, local) { return toPlan(unwrap(await getCarePlan({ client, path: { tankId }, query: local }))); },
      async changeRule(tankId, kind, change, local) {
        return toPlan(unwrap(await updateCarePlanRule({ client, path: { tankId, kind }, query: local, body: change })));
      },
      async setReminders(tankId, enabled, local) {
        return toPlan(unwrap(await updateCarePlan({ client, path: { tankId }, query: local, body: { remindersEnabled: enabled } })));
      },
      async due(local) { return unwrap<DueCareDto>(await listDueCare({ client, query: local })).items.map(toDueItem); },
      async heatmap(tankId, local) { return toCareHeatmap(unwrap(await getCareHeatmap({ client, path: { tankId }, query: local }))); },
      async ownHeatmap(local) { return toCareHeatmap(unwrap(await getMyCareHeatmap({ client, query: local }))); },
    },
    waterChange: {
      async start(tankId) { return unwrap(await startWaterChange({ client, path: { tankId } })); },
      async current(tankId) {
        try {
          return unwrap(await getCurrentWaterChange({ client, path: { tankId } }));
        } catch (error) {
          if (error instanceof ApiError && error.status === 404 && error.code === "water_change_not_found") return null;
          throw error;
        }
      },
      async markStep(tankId, id, step, state) { return unwrap(await markWaterChangeStep({ client, path: { tankId, id, step }, body: { state } })); },
      async addPhoto(tankId, id, file, capturedInApp) {
        return unwrap(await addWaterChangePhoto({
          client, path: { tankId, id }, body: { file: file as never },
          bodySerializer: () => {
            const formData = new FormData();
            formData.append("file", file as never);
            formData.append("capturedInApp", String(capturedInApp));
            return formData;
          },
        }));
      },
      async finish(tankId, id, amount) { return unwrap(await finishWaterChange({ client, path: { tankId, id }, body: amount ? { amount } : {} })); },
      async cancel(tankId, id) { unwrap(await cancelWaterChange({ client, path: { tankId, id } })); },
    },
    webPush: {
      async config() {
        const config = unwrap(await getWebPushConfig({ client }));
        return { available: config.available, publicKey: config.publicKey ?? null };
      },
      async subscribe(subscription) {
        const stored = unwrap(await subscribeWebPush({ client, body: subscription }));
        return { id: Number(stored.id), timeZone: stored.timeZone };
      },
      async unsubscribe(endpoint) { unwrap(await unsubscribeWebPush({ client, body: { endpoint } })); },
    },
    videos: {
      async listForTank(tankId) { return absolutizeMedia(unwrap(await listTankVideos({ client, path: { tankId } })), options.baseUrl); },
      async removeFromTank(tankId, mediaItemId) { unwrap(await removeTankVideo({ client, path: { tankId, mediaItemId } })); },
      async removeFromCareEntry(tankId, entryId, mediaItemId) {
        return unwrap(await removeCareVideo({ client, path: { tankId, id: entryId, mediaItemId } }));
      },
      uploadTransport: {
        async create(request) { return unwrap(await createVideoUpload({ client, body: request })); },
        async status(id) { return unwrap(await getVideoUpload({ client, path: { id } })); },
        async append(id, offset, chunk) {
          const result = await appendVideoUploadChunk({
            client, path: { id }, body: chunk as never, bodySerializer: body => body as never,
            headers: { "Upload-Offset": String(offset) },
          });
          if (result.error !== undefined) unwrap(result as { error: unknown; response?: Response });
          // A browser hides the header from a cross-origin page unless the server exposes it; the
          // server accepts a chunk only whole, so the confirmed offset is then the end of the chunk.
          const confirmed = Number(result.response?.headers.get("Upload-Offset"));
          return Number.isFinite(confirmed) && confirmed > offset ? confirmed : offset + chunk.size;
        },
        async complete(id) { return unwrap(await completeVideoUpload({ client, path: { id } })); },
      },
      async cancelUpload(id) { unwrap(await cancelVideoUpload({ client, path: { id } })); },
      careVideoUrl(path) { return `${options.baseUrl.replace(/\/+$/, "")}${path}`; },
    },
    todos: {
      async list(params) { return unwrapPaged(await listMyTodos({ client, query: params ?? {} })); },
      async add(tankId, text) { return unwrap(await createTankTodo({ client, path: { tankId }, body: { text } })); },
      async setDone(id, done) { return unwrap(await updateTodo({ client, path: { id }, body: { done } })); },
      async remove(id) { return unwrap(await deleteTodo({ client, path: { id } })); },
    },
    discover: {
      async get(regions) {
        return unwrap(await getDiscover({ client, query: regions ? { regions: regions.join(",") } : {} }));
      },
    },
    featureFlags: {
      async get() {
        return unwrap(await getFeatureFlags({ client, cache: "no-cache" }));
      },
    },
    stories: {
      async list(params) {
        const result = await listStories({ client, query: params ?? {} });
        return unwrapPaged(result);
      },
      async get(slug) {
        const result = await getStory({ client, path: { slug } });
        return absolutizeMedia(unwrap(result), options.baseUrl);
      },
      async listComments(slug, params) {
        const result = await listStoryComments({ client, path: { slug }, query: params ?? {} });
        return unwrapPaged(result);
      },
      async createComment(slug, request) {
        const result = await createStoryComment({ client, path: { slug }, body: request });
        return unwrap(result);
      },
      async publish(id, request) {
        const result = await publishStory({
          client,
          path: { id },
          body: request,
        });
        return absolutizeMedia(unwrap(result), options.baseUrl);
      },
      async discard(id) {
        const result = await discardStory({ client, path: { id } });
        unwrap(result);
      },
    },
    tanks: {
      async timeLapse(id) {
        return absolutizeMedia(unwrap(await getTankTimeLapse({ client, path: { id } })), options.baseUrl);
      },
      async addTimeLapsePhoto(id, file, capturedInApp = false) {
        return absolutizeMedia(unwrap(await addTankSeriesPhoto({
          client, path: { tankId: id }, body: { file: file as never, capturedInApp },
          bodySerializer: () => {
            const formData = new FormData();
            formData.append("file", file as never);
            formData.append("capturedInApp", capturedInApp ? "true" : "false");
            return formData;
          },
        })), options.baseUrl);
      },
      async streaks(id, local) {
        return toTankStreaks(unwrap(await getTankStreaks({ client, path: { tankId: id }, query: local })));
      },
      async operation(id, local) {
        return toTankOperation(absolutizeMedia(unwrap(await getTankOperation({ client, path: { tankId: id }, query: local })), options.baseUrl));
      },
      async decommission(id, file, { scope, capturedInApp }, local) {
        return toTankOperation(absolutizeMedia(unwrap(await decommissionTank({
          client, path: { tankId: id }, query: local, body: { file: file as never, capturedInApp, scope },
          bodySerializer: () => {
            const formData = new FormData();
            formData.append("file", file as never);
            formData.append("capturedInApp", capturedInApp ? "true" : "false");
            formData.append("scope", scope);
            return formData;
          },
        })), options.baseUrl));
      },
      async recommission(id, file, capturedInApp, local) {
        return toTankOperation(absolutizeMedia(unwrap(await recommissionTank({
          client, path: { tankId: id }, query: local, body: { file: file as never, capturedInApp },
          bodySerializer: () => {
            const formData = new FormData();
            formData.append("file", file as never);
            formData.append("capturedInApp", capturedInApp ? "true" : "false");
            return formData;
          },
        })), options.baseUrl));
      },
      async setVisibility(id, levels) {
        return unwrap(await setTankVisibility({ client, path: { id }, body: levels }));
      },
      async follow(id) {
        return absolutizeMedia(unwrap(await getTankFollow({ client, path: { id } })), options.baseUrl);
      },
      async requestFollow(id) {
        return unwrap(await followTank({ client, path: { id } }));
      },
      async unfollow(id) {
        unwrap(await unfollowTank({ client, path: { id } }));
      },
      async followers(id, params) {
        return absolutizeMedia(unwrapPaged(await listTankFollowers({ client, path: { id }, query: params ?? {} })), options.baseUrl);
      },
      async removeFollower(id, profileId) {
        unwrap(await removeTankFollower({ client, path: { id, profileId } }));
      },
      async followRequests(id, params) {
        return absolutizeMedia(unwrapPaged(await listTankFollowRequests({ client, path: { id }, query: params ?? {} })), options.baseUrl);
      },
      async approveFollowRequest(id, profileId) {
        unwrap(await approveTankFollowRequest({ client, path: { id, profileId } }));
      },
      async declineFollowRequest(id, profileId) {
        unwrap(await declineTankFollowRequest({ client, path: { id, profileId } }));
      },
      async selectedMembers(id) {
        return absolutizeMedia(unwrap(await listTankSelectedMembers({ client, path: { id } })), options.baseUrl);
      },
      async addSelectedMember(id, handle) {
        return absolutizeMedia(unwrap(await addTankSelectedMember({ client, path: { id }, body: { handle } })), options.baseUrl);
      },
      async removeSelectedMember(id, profileId) {
        unwrap(await removeTankSelectedMember({ client, path: { id, profileId } }));
      },
      async refineRegion(id, region) {
        unwrap(await refineTankRegion({ client, path: { id }, body: { region } }));
      },
      async create(request) {
        const result = await createTank({ client, body: request });
        return unwrap(result);
      },
      async list(params) {
        const result = await listTanks({ client, query: params ?? {} });
        return absolutizeMedia(unwrapPaged(result), options.baseUrl);
      },
      async get(id) {
        const result = await getTank({ client, path: { id } });
        return absolutizeMedia(unwrap(result), options.baseUrl);
      },
      async listComments(id, params) {
        const result = await listTankComments({ client, path: { id }, query: params ?? {} });
        return unwrapPaged(result);
      },
      async createComment(id, request) {
        const result = await createTankComment({ client, path: { id }, body: request });
        return unwrap(result);
      },
    },
    profiles: {
      async get(id) {
        const result = await getProfile({ client, path: { id } });
        return unwrap(result);
      },
      async listStories(id, params) {
        const result = await listProfileStories({ client, path: { id }, query: params ?? {} });
        return unwrapPaged(result);
      },
      async listTanks(id, params) {
        const result = await listProfileTanks({ client, path: { id }, query: params ?? {} });
        return unwrapPaged(result);
      },
    },
    species: {
      async list(params) {
        const result = await listSpecies({ client, query: params ?? {} });
        return unwrapPaged(result);
      },
      async get(idOrSlug) {
        const result = await getSpecies({ client, path: { idOrSlug } });
        return unwrap(result);
      },
    },
    community: {
      async listCategories() {
        const result = await listCommunityCategories({ client });
        return unwrap(result);
      },
      async listThreads(params) {
        const result = await listCommunityThreads({ client, query: params ?? {} });
        return unwrapPaged(result);
      },
      async getThread(id) {
        const result = await getCommunityThread({ client, path: { id } });
        return unwrap(result);
      },
    },
    me: {
      async today(date) {
        return absolutizeMedia(unwrap(await getToday({ client, query: { date } })), options.baseUrl);
      },
      async followed() {
        return toTodayFollowed(absolutizeMedia(unwrap(await getTodayFollowed({ client })), options.baseUrl));
      },
      async followedTanks(params) {
        const page = unwrapPaged(await listMyFollowedTanks({ client, query: params ?? {} }));
        return absolutizeMedia({ total: page.total, items: page.items.map(toFollowedTank) }, options.baseUrl);
      },
      async milestones(local) {
        return unwrap(await getTodayMilestones({ client, query: local })).items.map(toMilestone);
      },
      async dismissMilestone(key) {
        unwrap(await dismissMilestone({ client, path: { key } }));
      },
      async settings() {
        return unwrap(await getMySettings({ client }));
      },
      async setHandle(handle) {
        return unwrap(await changeMyHandle({ client, body: { handle } }));
      },
      async setStreaksEnabled(streaksEnabled) {
        return unwrap(await updateMySettings({ client, body: { streaksEnabled } }));
      },
      async setProfileImage(role, mediaItemId) {
        const call = role === "avatar" ? setMyAvatar : setMyHeader;
        return unwrap(await call({ client, body: { mediaItemId } }));
      },
      async uploadProfileImage(role, file) {
        const call = role === "avatar" ? uploadMyAvatar : uploadMyHeader;
        const result = await call({
          client,
          body: { file: file as never },
          bodySerializer: () => {
            const formData = new FormData();
            formData.append("file", file as never);
            return formData;
          },
        });
        return unwrap(result);
      },
      async removeProfileImage(role) {
        return unwrap(await (role === "avatar" ? removeMyAvatar : removeMyHeader)({ client }));
      },
      async streaks(local) {
        return toMeStreaks(unwrap(await getMyStreaks({ client, query: local })));
      },
      async vacations() {
        return unwrap(await listMyVacations({ client })).items.map(toVacation);
      },
      async startVacation(local, startDate) {
        return toVacation(unwrap(await startVacation({ client, query: local, body: startDate ? { startDate } : {} })));
      },
      async recordVacation(local, startDate, endDate) {
        return toVacation(unwrap(await startVacation({ client, query: local, body: { startDate, endDate } })));
      },
      async endVacation(date) {
        return toVacation(unwrap(await endVacation({ client, query: { date } })));
      },
      async completeOnboarding(request) {
        return unwrap(await completeOnboarding({ client, body: request }));
      },
      async legacyTanks() {
        return unwrap(await listMyLegacyTanks({ client }));
      },
      async setInterests(interests) {
        return unwrap(await setMyInterests({ client, body: { interests } }));
      },
      async get() {
        const result = await getMe({ client });
        return unwrap(result);
      },
      async drafts() {
        const result = await listMyDrafts({ client });
        return absolutizeMedia(unwrap(result), options.baseUrl);
      },
    },
    library: {
      async list(params = {}) {
        const body = unwrap(await listMyMedia({ client, query: libraryQuery(params) }));
        return absolutizeMedia({ total: Number(body.total), items: body.items.map(toLibraryItem) }, options.baseUrl);
      },
      async get(id, local) {
        return absolutizeMedia(toLibraryDetail(unwrap(await getMyMedia({ client, path: { id }, query: local }))), options.baseUrl);
      },
      async remove(id) {
        return unwrap(await deleteMyMedia({ client, path: { id } })).places.map(toLibraryPlace);
      },
      async uploadPhoto(file) {
        const result = await uploadMyMediaPhoto({
          client,
          body: { file: file as never },
          bodySerializer: () => {
            const formData = new FormData();
            formData.append("file", file as never);
            return formData;
          },
        });
        return absolutizeMedia(toLibraryItem(unwrap(result)), options.baseUrl);
      },
      async share(id, request) {
        return absolutizeMedia(unwrap(await shareMyMedia({ client, path: { id }, body: request })), options.baseUrl);
      },
    },
    storyEditor: {
      async list(params) {
        const body = unwrap(await listMyStories({ client, query: params ?? {} }));
        return absolutizeMedia({ total: Number(body.total), items: body.items.map(toMyStory) }, options.baseUrl);
      },
      async create(request = { title: null, description: null, topic: null }) {
        return editorOf(await createMyStory({ client, body: request }));
      },
      async get(id) {
        return editorOf(await getMyStory({ client, path: { id } }));
      },
      async updateText(id, request) {
        return editorOf(await updateMyStory({ client, path: { id }, body: request }));
      },
      async remove(id) {
        unwrap(await deleteMyStory({ client, path: { id } }));
      },
      async withdraw(id) {
        return editorOf(await withdrawMyStory({ client, path: { id } }));
      },
      async addItem(id, mediaItemId, caption = null) {
        return editorOf(await addMyStoryItem({ client, path: { id }, body: { mediaItemId, caption } }));
      },
      async setCaption(id, itemId, caption) {
        return editorOf(await setMyStoryItemCaption({ client, path: { id, itemId }, body: { caption } }));
      },
      async removeItem(id, itemId) {
        return editorOf(await removeMyStoryItem({ client, path: { id, itemId } }));
      },
      async reorder(id, entries) {
        return editorOf(await reorderMyStory({ client, path: { id }, body: { entries } }));
      },
      async setCover(id, itemId) {
        return editorOf(await setMyStoryCover({ client, path: { id }, body: { itemId } }));
      },
      async addSubject(id, request) {
        return editorOf(await addMyStorySubject({ client, path: { id }, body: request }));
      },
      async placeSubject(id, subjectId, positioned) {
        return editorOf(await placeMyStorySubject({ client, path: { id, subjectId }, body: { positioned } }));
      },
      async removeSubject(id, subjectId) {
        return editorOf(await removeMyStorySubject({ client, path: { id, subjectId } }));
      },
    },
    uploads: {
      async create(file) {
        const result = await uploadPhoto({
          client,
          body: { file: file as never },
          bodySerializer: () => {
            const formData = new FormData();
            formData.append("file", file as never);
            return formData;
          },
        });
        return absolutizeMedia(unwrap(result), options.baseUrl);
      },
    },
  };
}
