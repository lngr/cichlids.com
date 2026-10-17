import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError, type FishPage } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { localTime } from "../today/localDate";

export type FishPageState = {
  page: FishPage | null;
  loading: boolean;
  /** not_found for a page the viewer may not see or that does not exist, load for any other failure. */
  error: "not_found" | "load" | null;
  reload: () => void;
  /** Shows a page a write answered with. */
  replace: (page: FishPage) => void;
};

/**
 * The page of a named fish, read when the id changes and whenever reload is called. A page the viewer may
 * not see answers like a missing one.
 */
export function useFishPage(id: number): FishPageState {
  const [page, setPage] = useState<FishPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<FishPageState["error"]>(null);
  const [revision, setRevision] = useState(0);
  const current = useRef(0);
  useEffect(() => {
    const ticket = ++current.current;
    setLoading(true);
    setError(null);
    void apiClient.fish.get(id, localTime())
      .then(result => { if (ticket === current.current) setPage(result); })
      .catch((err: unknown) => {
        if (ticket !== current.current) return;
        setPage(null);
        setError(err instanceof ApiError && err.status === 404 ? "not_found" : "load");
      })
      .finally(() => { if (ticket === current.current) setLoading(false); });
    return () => { current.current++; };
  }, [id, revision]);
  const reload = useCallback(() => setRevision(value => value + 1), []);
  const replace = useCallback((result: FishPage) => { current.current++; setPage(result); setLoading(false); setError(null); }, []);
  return { page, loading, error, reload, replace };
}
