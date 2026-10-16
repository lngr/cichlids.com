import { useCallback, useRef, useState } from "react";
import { useFocusEffect } from "expo-router";
import type { MeStreaks, TankStreaks } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { localTime } from "../today/localDate";

/**
 * The streaks of every own tank, read whenever the screen gains focus and whenever the revision
 * changes. Null until they are read; streaks that cannot be read stay null so the screen remains
 * usable without them.
 */
export function useOwnStreaks(revision = 0): MeStreaks | null {
  const [streaks, setStreaks] = useState<MeStreaks | null>(null);
  const current = useRef(0);
  useFocusEffect(useCallback(() => {
    const ticket = ++current.current;
    void apiClient.me.streaks(localTime())
      .then(result => { if (ticket === current.current) setStreaks(result); })
      .catch(() => { if (ticket === current.current) setStreaks(null); });
    return () => { current.current++; };
  }, [revision]));
  return streaks;
}

/** The streaks of one tank a viewer may see, read whenever the screen gains focus; null while unread or when they cannot be read. */
export function useTankStreaks(tankId: number): TankStreaks | null {
  const [streaks, setStreaks] = useState<TankStreaks | null>(null);
  const current = useRef(0);
  useFocusEffect(useCallback(() => {
    const ticket = ++current.current;
    void apiClient.tanks.streaks(tankId, localTime())
      .then(result => { if (ticket === current.current) setStreaks(result); })
      .catch(() => { if (ticket === current.current) setStreaks(null); });
    return () => { current.current++; };
  }, [tankId]));
  return streaks;
}
