// The items of a paged list plus the items the app created itself and shows at the top before the
// server lists them (pending). The server lists such an item on some page, where it is skipped;
// the paging offset counts every item the server returned, skipped ones included.

export interface PagedItems<T> {
  /** The shown items: pending items first, then the server items in server order. */
  items: T[];
  /** Locally created items shown at the top that no loaded page has listed yet. */
  pending: T[];
  /** The number of items the server returned for the loaded pages. */
  received: number;
  /** The server's total of the last loaded page. */
  serverTotal: number;
}

type Page<T> = { total: number; items: T[] };
type KeyOf<T> = ((item: T) => string | number) | undefined;

export function emptyPagedItems<T>(): PagedItems<T> {
  return { items: [], pending: [], received: 0, serverTotal: 0 };
}

/** Replaces the server items with a first page; pending items that page does not list stay on top. */
export function applyFirstPage<T>(state: PagedItems<T>, page: Page<T>, keyOf: KeyOf<T>): PagedItems<T> {
  const listed = keyOf ? new Set(page.items.map(keyOf)) : new Set();
  const pending = keyOf ? state.pending.filter((item) => !listed.has(keyOf(item))) : [];
  return { items: [...pending, ...page.items], pending, received: page.items.length, serverTotal: page.total };
}

/** Appends a further page, skipping the pending items it lists. */
export function applyNextPage<T>(state: PagedItems<T>, page: Page<T>, keyOf: KeyOf<T>): PagedItems<T> {
  const pendingKeys = keyOf ? new Set(state.pending.map(keyOf)) : new Set();
  const fresh = keyOf ? page.items.filter((item) => !pendingKeys.has(keyOf(item))) : page.items;
  const listed = keyOf ? new Set(page.items.map(keyOf)) : new Set();
  const pending = keyOf ? state.pending.filter((item) => !listed.has(keyOf(item))) : state.pending;
  return {
    items: [...state.items, ...fresh],
    pending,
    received: state.received + page.items.length,
    serverTotal: page.total,
  };
}

/** Shows a locally created item at the top. */
export function applyPrepend<T>(state: PagedItems<T>, item: T): PagedItems<T> {
  return { ...state, items: [item, ...state.items], pending: [item, ...state.pending] };
}

/** The server offset of the next page. */
export function nextPageOffset<T>(state: PagedItems<T>): number {
  return state.received;
}

export function hasMorePages<T>(state: PagedItems<T>): boolean {
  return state.received < state.serverTotal;
}

/** Whether a first page or a further page is currently being fetched. */
export interface LoadStatus {
  loading: boolean;
  loadingMore: boolean;
}

export const idleLoadStatus: LoadStatus = { loading: false, loadingMore: false };

/**
 * The status when a fetch starts. A first-page fetch (append false) supersedes a load-more that is
 * still pending, so it clears the load-more flag: the superseded fetch never reports back.
 */
export function beginLoad(status: LoadStatus, append: boolean): LoadStatus {
  return append ? { ...status, loadingMore: true } : { loading: true, loadingMore: false };
}

/** The status when the latest fetch ends. */
export function finishLoad(status: LoadStatus, append: boolean): LoadStatus {
  return append ? { ...status, loadingMore: false } : { ...status, loading: false };
}

/** What a list shows in place of its items: loading, the load error with a retry, or the empty message. */
export function listEmptyState(status: { loading: boolean; error: string | null }): "loading" | "error" | "empty" {
  if (status.loading) return "loading";
  return status.error ? "error" : "empty";
}
