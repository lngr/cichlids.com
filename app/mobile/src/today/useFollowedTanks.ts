import { useCallback, useRef, useState } from "react";
import { useFocusEffect } from "expo-router";
import type { TodayFollowed } from "@cichlids/client-core";
import { apiClient } from "../api/client";

/**
 * The followed tanks with a fresh update that Today shows, read whenever the screen gains focus.
 * Null until they are read and when they cannot be read, so Today stays usable.
 */
export function useFollowedTanks(): TodayFollowed | null {
  const [followed, setFollowed] = useState<TodayFollowed | null>(null);
  const current = useRef(0);
  useFocusEffect(useCallback(() => {
    const ticket = ++current.current;
    void apiClient.me.followed()
      .then(result => { if (ticket === current.current) setFollowed(result); })
      .catch(() => { if (ticket === current.current) setFollowed(null); });
    return () => { current.current++; };
  }, []));
  return followed;
}
