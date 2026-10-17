import { useEffect, useState } from "react";
import { apiClient } from "../api/client";

/**
 * The tanks of the signed-in member, read once while enabled. An empty list until they are read and when
 * they cannot be read.
 */
export function useOwnTanks(enabled: boolean): { id: number; title: string }[] {
  const [tanks, setTanks] = useState<{ id: number; title: string }[]>([]);
  useEffect(() => {
    if (!enabled) return;
    let cancelled = false;
    void apiClient.me.get().then(me => apiClient.tanks.list({ user: Number(me.profile.id), limit: 50 })).then(page => {
      if (!cancelled) setTanks(page.items.map(tank => ({ id: Number(tank.id), title: tank.title })));
    }).catch(() => undefined);
    return () => { cancelled = true; };
  }, [enabled]);
  return tanks;
}
