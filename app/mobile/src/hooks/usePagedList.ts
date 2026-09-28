import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import type { PagedResponse } from "@cichlids/client-core";
import { applyFirstPage, applyNextPage, applyPrepend, emptyPagedItems, hasMorePages, nextPageOffset, type PagedItems } from "./pagedListState";

const PAGE_SIZE = 20;

export interface UsePagedListState<T> {
  items: T[];
  /** The server's total of the last loaded page. */
  total: number;
  loading: boolean;
  loadingMore: boolean;
  error: string | null;
  hasMore: boolean;
  loadMore: () => void;
  reload: () => void;
  /** Shows an item the app created at the top of the list; requires keyOf. */
  prepend: (item: T) => void;
}

/**
 * Drives a FlatList against a paged Cichlids.Api list endpoint: fetches the
 * first page on mount or when `deps` change, and appends further pages via
 * loadMore() (wired to FlatList's onEndReached). keyOf identifies items that
 * prepend() shows at the top, so loaded pages skip them. A change of `deps`
 * drops the prepended items; reload() keeps those no page has listed yet.
 */
export function usePagedList<T>(
  fetchPage: (offset: number, limit: number) => Promise<PagedResponse<T>>,
  deps: unknown[],
  keyOf?: (item: T) => string | number,
): UsePagedListState<T> {
  const [state, setState] = useState<PagedItems<T>>(emptyPagedItems);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const requestId = useRef(0);
  const { t } = useTranslation();

  const load = useCallback(
    async (offset: number, append: boolean) => {
      const id = ++requestId.current;
      append ? setLoadingMore(true) : setLoading(true);
      setError(null);
      try {
        const page = await fetchPage(offset, PAGE_SIZE);
        if (id !== requestId.current) return;
        setState((current) => (append ? applyNextPage(current, page, keyOf) : applyFirstPage(current, page, keyOf)));
      } catch (err) {
        if (id !== requestId.current) return;
        setError(err instanceof Error ? err.message : t("common.unknownError"));
      } finally {
        if (id !== requestId.current) return;
        append ? setLoadingMore(false) : setLoading(false);
      }
    },
    [fetchPage, keyOf],
  );

  useEffect(() => {
    // Prepended items belong to the previous deps; the shown items stay until the first page replaces them.
    setState((current) => ({ ...current, pending: [] }));
    load(0, false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  const hasMore = hasMorePages(state);
  const offset = nextPageOffset(state);
  const loadMore = useCallback(() => {
    if (loading || loadingMore || !hasMore) return;
    load(offset, true);
  }, [loading, loadingMore, hasMore, offset, load]);

  const reload = useCallback(() => load(0, false), [load]);

  const prepend = useCallback(
    (item: T) => {
      if (!keyOf) throw new Error("usePagedList.prepend requires keyOf.");
      setState((current) => applyPrepend(current, item));
    },
    [keyOf],
  );

  return {
    items: state.items,
    total: state.serverTotal,
    loading,
    loadingMore,
    error,
    hasMore,
    loadMore,
    reload,
    prepend,
  };
}
