import { useCallback, useEffect, useRef, useState } from "react";
import type { Stocking } from "@cichlids/client-core";
import { apiClient } from "../api/client";

/**
 * The stocking of a tank, read when the tank changes and whenever reload is called. Null until it is
 * read and when it cannot be read, so the tank page stays usable without it.
 */
export function useStocking(tankId: number): { stocking: Stocking | null; reload: () => void } {
  const [stocking, setStocking] = useState<Stocking | null>(null);
  const [revision, setRevision] = useState(0);
  const current = useRef(0);
  useEffect(() => {
    const ticket = ++current.current;
    void apiClient.tanks.stocking(tankId)
      .then(result => { if (ticket === current.current) setStocking(result); })
      .catch(() => { if (ticket === current.current) setStocking(null); });
    return () => { current.current++; };
  }, [tankId, revision]);
  const reload = useCallback(() => setRevision(value => value + 1), []);
  return { stocking, reload };
}
