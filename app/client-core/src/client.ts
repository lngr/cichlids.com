import { createClient, createConfig } from "./generated/client/index.js";
import type { Client } from "./generated/client/index.js";
import type { VideoUploadTransport } from "./videoUpload.js";
import {
  logCareEntry,
  updateCareEntry,
  deleteCareEntry,
  listCareEntries,
  createPictureComment,
  addCarePhoto,
  createCareEntryTodo,
  createTankTodo,
  startWaterChange,
  getCurrentWaterChange,
  markWaterChangeStep,
  setWaterChangePhoto,
  finishWaterChange,
  cancelWaterChange,
  deleteTodo,
  listMyTodos,
  removeCarePhoto,
  updateTodo,
  createTankComment,
  createTank,
  completeOnboarding,
  listMyLegacyTanks,
  setMyInterests,
  refineTankRegion,
  discardPost,
  getCarePlan,
  getWebPushConfig,
  subscribeWebPush,
  unsubscribeWebPush,
  getCareSeries,
  listDueCare,
  updateCarePlan,
  updateCarePlanRule,
  getCommunityThread,
  getFeatureFlags,
  getDiscover,
  getMe,
  getToday,
  getPicture,
  getProfile,
  getSpecies,
  getTank,
  getTankTimeLapse,
  addTankSeriesPhoto,
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
  listPictureComments,
  listPictures,
  listProfilePictures,
  listProfileTanks,
  listSpecies,
  listTankComments,
  listTanks,
  publishPost,
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
  TodayDto,
  PictureDetailDto,
  PictureListItemDto,
  ProfileDetailDto,
  PublishPostRequest,
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
export type WaterChangePhotoSlot = "drained" | "refilled";
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
};
export type CarePlan = { tankId: number; rules: CarePlanRule[]; remindersEnabled: boolean };
export type DueCareItem = { tankId: number; tankTitle: string; kind: CareKind; rhythmDays: number; reason: CareReason; due: CareDue; remindersEnabled: boolean };
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
export type PictureListItem = PictureListItemDto;
export type PictureDetail = PictureDetailDto;
export type TankListItem = TankListItemDto;
export type TankDetail = TankDetailDto;
export type TankTimeLapse = TankTimeLapseDto;
export type TankTimeLapsePhoto = TankTimeLapsePhotoDto;
export type TankMediaItem = TankMediaItemDto;
export type Dimensions = DimensionsDto;
export type WaterValues = WaterValuesDto;
export type Inhabitant = InhabitantDto;
export type ProfileDetail = ProfileDetailDto;
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
export type { CreateTankRequest, CreateCommentRequest, PublishPostRequest };
export type { OnboardingRequest, OnboardingResultDto, LegacyTankDto, TankRegion };
export type Me = MeDto;
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
}

export interface PicturesListParams {
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
  ) {
    super(message);
    this.name = "ApiError";
  }
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
    throw new ApiError(message, response?.status ?? 0, problem.code);
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

const toDue = (due: CareDueDto): CareDue => ({ state: due.state, days: Number(due.days), dueDate: due.dueDate });
const toReason = (reason: CareReasonDto): CareReason => ({ code: reason.code, parameters: reason.parameters });
const toPlan = (plan: CarePlanDto): CarePlan => ({
  tankId: Number(plan.tankId),
  rules: plan.rules.map(rule => ({
    kind: rule.kind, rhythmDays: rule.rhythmDays === null ? null : Number(rule.rhythmDays), enabled: rule.enabled,
    isDefault: rule.isDefault, reason: rule.reason && toReason(rule.reason), due: rule.due && toDue(rule.due),
  })),
  remindersEnabled: plan.remindersEnabled,
});
const toDueItem = (item: DueCareItemDto): DueCareItem => ({
  tankId: Number(item.tankId), tankTitle: item.tankTitle, kind: item.kind, rhythmDays: Number(item.rhythmDays),
  reason: toReason(item.reason), due: toDue(item.due), remindersEnabled: item.remindersEnabled,
});

export interface CichlidsClient {
  care: {
    list(tankId: number, params?: PageParams): Promise<PagedResponse<CareEntry>>;
    log(tankId: number, request?: LogCareRequest): Promise<CareEntry>;
    update(tankId: number, id: number, amount: CareAmountRequest | null): Promise<CareEntry>;
    updateValues(tankId: number, id: number, values: MeasurementValueRequest[]): Promise<CareEntry>;
    series(tankId: number, parameter: CareParameter, range?: CareSeriesRange): Promise<CareSeries>;
    remove(tankId: number, id: number): Promise<CareEntry>;
    /** Sets the note of an entry; null or blank removes it. */
    setNote(tankId: number, id: number, note: string | null): Promise<CareEntry>;
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
  };
  waterChange: {
    /** Starts a guided water change on an own tank, or returns the open one. */
    start(tankId: number): Promise<WaterChangeSession>;
    /** The open session of a tank; null when none is open. */
    current(tankId: number): Promise<WaterChangeSession | null>;
    markStep(tankId: number, id: number, step: WaterChangeStep, state: WaterChangeStepState): Promise<WaterChangeSession>;
    /** Stores the photo of a slot; capturedInApp tells that the camera of the app took it. */
    setPhoto(tankId: number, id: number, slot: WaterChangePhotoSlot, file: UploadFile, capturedInApp: boolean): Promise<WaterChangeSession>;
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
  pictures: {
    list(params?: PicturesListParams): Promise<PagedResponse<PictureListItem>>;
    get(slug: string): Promise<PictureDetail>;
    listComments(slug: string, params?: PageParams): Promise<PagedResponse<Comment>>;
    createComment(slug: string, request: CreateCommentRequest): Promise<CommentCreated>;
  };
  tanks: {
    create(request: CreateTankRequest): Promise<TankDetail>;
    refineRegion(id: number, region: TankRegion): Promise<void>;
    list(params?: TanksListParams): Promise<PagedResponse<TankListItem>>;
    get(id: number): Promise<TankDetail>;
    timeLapse(id: number): Promise<TankTimeLapse>;
    /** Adds a photo to the time-lapse series of an own tank. */
    addTimeLapsePhoto(id: number, file: UploadFile): Promise<TankTimeLapsePhoto>;
    listComments(id: number, params?: PageParams): Promise<PagedResponse<Comment>>;
    createComment(id: number, request: CreateCommentRequest): Promise<CommentCreated>;
  };
  profiles: {
    get(id: number): Promise<ProfileDetail>;
    listPictures(id: number, params?: PageParams): Promise<PagedResponse<PictureListItem>>;
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
  me: {
    get(): Promise<Me>;
    today(date: string): Promise<Today>;
    completeOnboarding(request: OnboardingRequest): Promise<OnboardingResultDto>;
    legacyTanks(): Promise<LegacyTankDto[]>;
    setInterests(interests: string[]): Promise<string[]>;
    drafts(): Promise<Draft[]>;
  };
  uploads: {
    create(file: UploadFile): Promise<Draft>;
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
  posts: {
    publish(id: number, request: PublishPostRequest): Promise<PictureDetail>;
    discard(id: number): Promise<void>;
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

  if (options.getAccessToken) {
    client.interceptors.request.use(async (request) => {
      const token = await options.getAccessToken!();
      if (token) {
        request.headers.set("Authorization", `Bearer ${token}`);
      }
      return request;
    });
  }

  return {
    care: {
      async list(tankId, params) { return unwrapPaged(await listCareEntries({ client, path: { tankId }, query: params ?? {} })); },
      async log(tankId, request) { return unwrap(await logCareEntry({ client, path: { tankId }, body: request ?? {} })); },
      async update(tankId, id, amount) { return unwrap(await updateCareEntry({ client, path: { tankId, id }, body: { amount } })); },
      async updateValues(tankId, id, values) { return unwrap(await updateCareEntry({ client, path: { tankId, id }, body: { amount: null, values } })); },
      async series(tankId, parameter, range) { return unwrap(await getCareSeries({ client, path: { tankId }, query: { parameter, ...range } })); },
      async remove(tankId, id) { return unwrap(await deleteCareEntry({ client, path: { tankId, id } })); },
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
      async setPhoto(tankId, id, slot, file, capturedInApp) {
        return unwrap(await setWaterChangePhoto({
          client, path: { tankId, id, slot }, body: { file: file as never },
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
      async listForTank(tankId) { return unwrap(await listTankVideos({ client, path: { tankId } })); },
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
    pictures: {
      async list(params) {
        const result = await listPictures({ client, query: params ?? {} });
        return unwrapPaged(result);
      },
      async get(slug) {
        const result = await getPicture({ client, path: { slug } });
        return unwrap(result);
      },
      async listComments(slug, params) {
        const result = await listPictureComments({ client, path: { slug }, query: params ?? {} });
        return unwrapPaged(result);
      },
      async createComment(slug, request) {
        const result = await createPictureComment({ client, path: { slug }, body: request });
        return unwrap(result);
      },
    },
    tanks: {
      async timeLapse(id) {
        return unwrap(await getTankTimeLapse({ client, path: { id } }));
      },
      async addTimeLapsePhoto(id, file) {
        return unwrap(await addTankSeriesPhoto({
          client, path: { tankId: id }, body: { file: file as never },
          bodySerializer: () => {
            const formData = new FormData();
            formData.append("file", file as never);
            return formData;
          },
        }));
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
        return unwrapPaged(result);
      },
      async get(id) {
        const result = await getTank({ client, path: { id } });
        return unwrap(result);
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
      async listPictures(id, params) {
        const result = await listProfilePictures({ client, path: { id }, query: params ?? {} });
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
        return unwrap(await getToday({ client, query: { date } }));
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
        return unwrap(result);
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
        return unwrap(result);
      },
    },
    posts: {
      async publish(id, request) {
        const result = await publishPost({
          client,
          path: { id },
          body: request,
        });
        return unwrap(result);
      },
      async discard(id) {
        const result = await discardPost({ client, path: { id } });
        unwrap(result);
      },
    },
  };
}
