import { useCallback, useEffect, useRef, useState } from "react";
import type { PagedResponse } from "@cichlids/client-core";

const PAGE_SIZE = 20;

export interface UsePagedListState<T> {
  items: T[];
  total: number;
  loading: boolean;
  loadingMore: boolean;
  error: string | null;
  hasMore: boolean;
  loadMore: () => void;
  reload: () => void;
}

/**
 * Drives a FlatList against a paged Cichlids.Api list endpoint: fetches the
 * first page on mount or when `deps` change, and appends further pages via
 * loadMore() (wired to FlatList's onEndReached).
 */
export function usePagedList<T>(
  fetchPage: (offset: number, limit: number) => Promise<PagedResponse<T>>,
  deps: unknown[],
): UsePagedListState<T> {
  const [items, setItems] = useState<T[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const requestId = useRef(0);

  const load = useCallback(
    async (offset: number, append: boolean) => {
      const id = ++requestId.current;
      append ? setLoadingMore(true) : setLoading(true);
      setError(null);
      try {
        const page = await fetchPage(offset, PAGE_SIZE);
        if (id !== requestId.current) return;
        setTotal(page.total);
        setItems((prev) => (append ? [...prev, ...page.items] : page.items));
      } catch (err) {
        if (id !== requestId.current) return;
        setError(err instanceof Error ? err.message : "Unbekannter Fehler");
      } finally {
        if (id !== requestId.current) return;
        append ? setLoadingMore(false) : setLoading(false);
      }
    },
    [fetchPage],
  );

  useEffect(() => {
    load(0, false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  const loadMore = useCallback(() => {
    if (loading || loadingMore || items.length >= total) return;
    load(items.length, true);
  }, [loading, loadingMore, items.length, total, load]);

  const reload = useCallback(() => load(0, false), [load]);

  return {
    items,
    total,
    loading,
    loadingMore,
    error,
    hasMore: items.length < total,
    loadMore,
    reload,
  };
}
