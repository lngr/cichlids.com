import { useCallback, useRef, useState } from "react";
import { useFocusEffect } from "expo-router";
import type { Milestone } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { localTime } from "./localDate";

/**
 * The milestones of the own tanks that Today shows, read whenever the screen gains focus. Dismissing
 * one removes it at once and stores the dismissal; a dismissal that fails brings the milestone back.
 * Milestones that cannot be read are an empty list, so Today stays usable.
 */
export function useMilestones(): { milestones: Milestone[]; dismiss: (key: string) => void } {
  const [milestones, setMilestones] = useState<Milestone[]>([]);
  const current = useRef(0);
  useFocusEffect(useCallback(() => {
    const ticket = ++current.current;
    void apiClient.me.milestones(localTime())
      .then(result => { if (ticket === current.current) setMilestones(result); })
      .catch(() => { if (ticket === current.current) setMilestones([]); });
    return () => { current.current++; };
  }, []));
  const dismiss = useCallback((key: string) => {
    current.current++;
    setMilestones(items => items.filter(item => item.key !== key));
    void apiClient.me.dismissMilestone(key).catch(() => {
      void apiClient.me.milestones(localTime()).then(setMilestones).catch(() => undefined);
    });
  }, []);
  return { milestones, dismiss };
}
