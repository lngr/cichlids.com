import { createClient, createConfig } from "./generated/client/index.js";
import type { Client } from "./generated/client/index.js";
import {
  createPictureComment,
  createTankComment,
  discardPost,
  getCommunityThread,
  getMe,
  getPicture,
  getProfile,
  getSpecies,
  getTank,
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
  CommentCreatedDto,
  CommentDto,
  CommunityAttachmentDto,
  CommunityCategoryDto,
  CommunityPostDto,
  CommunityThreadDetailDto,
  CommunityThreadListItemDto,
  CreateCommentRequest,
  DimensionsDto,
  DraftDto,
  InhabitantDto,
  MeDto,
  PictureDetailDto,
  PictureListItemDto,
  ProfileDetailDto,
  PublishPostRequest,
  SpeciesDetailDto,
  SpeciesLinkDto,
  SpeciesListItemDto,
  TankDetailDto,
  TankListItemDto,
  TankMediaItemDto,
  WaterValuesDto,
} from "./generated/types.gen.js";

export type PictureListItem = PictureListItemDto;
export type PictureDetail = PictureDetailDto;
export type TankListItem = TankListItemDto;
export type TankDetail = TankDetailDto;
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
export type { CreateCommentRequest, PublishPostRequest };
export type Me = MeDto;
export type Draft = DraftDto;
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

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

/**
 * Throws for any non-2xx response with the response body's error text, if
 * present. `response` is optional because the generated client also reaches
 * this path for errors raised while building the request itself, before any
 * response exists (e.g. a network failure).
 */
function unwrap<T>({ data, error, response }: { data?: T; error?: unknown; response?: Response }): T {
  if (error !== undefined) {
    const message = typeof error === "string" ? error : (response?.statusText ?? "Network error");
    throw new ApiError(message, response?.status ?? 0);
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

export interface CichlidsClient {
  pictures: {
    list(params?: PicturesListParams): Promise<PagedResponse<PictureListItem>>;
    get(slug: string): Promise<PictureDetail>;
    listComments(slug: string, params?: PageParams): Promise<PagedResponse<Comment>>;
    createComment(slug: string, request: CreateCommentRequest): Promise<CommentCreated>;
  };
  tanks: {
    list(params?: TanksListParams): Promise<PagedResponse<TankListItem>>;
    get(id: number): Promise<TankDetail>;
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
    drafts(): Promise<Draft[]>;
  };
  uploads: {
    create(file: UploadFile): Promise<Draft>;
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
