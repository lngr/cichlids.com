import { useCallback, useRef, useState } from "react";
import { useFocusEffect } from "expo-router";
import type { CareHeatmap } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { localTime } from "../today/localDate";

/**
 * The care heatmap of one tank, or of the own tanks summed when no tank is given, read whenever the
 * screen gains focus. Null while unread and when it cannot be read, for instance because the viewer
 * does not see the care stream of the tank.
 */
export function useCareHeatmap(tankId?: number): CareHeatmap | null {
  const [heatmap, setHeatmap] = useState<CareHeatmap | null>(null);
  const current = useRef(0);
  useFocusEffect(useCallback(() => {
    const ticket = ++current.current;
    const read = tankId === undefined ? apiClient.care.ownHeatmap(localTime()) : apiClient.care.heatmap(tankId, localTime());
    void read
      .then(result => { if (ticket === current.current) setHeatmap(result); })
      .catch(() => { if (ticket === current.current) setHeatmap(null); });
    return () => { current.current++; };
  }, [tankId]));
  return heatmap;
}
