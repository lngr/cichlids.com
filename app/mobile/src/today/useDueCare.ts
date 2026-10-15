import { useCallback, useRef, useState } from "react";
import { useFocusEffect } from "expo-router";
import { dueCareForToday, type DueCareItem } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { localTime } from "./localDate";

/**
 * The care of all own tanks that Today shows. The list is read whenever the screen gains focus
 * and whenever the revision changes, since a new entry moves the due dates. A list that cannot be
 * read is empty, so Today stays usable.
 */
export function useDueCare(revision: number): DueCareItem[] {
  const [items, setItems] = useState<DueCareItem[]>([]);
  const current = useRef(0);
  useFocusEffect(useCallback(() => {
    const ticket = ++current.current;
    void apiClient.care.due(localTime())
      .then(due => { if (ticket === current.current) setItems(dueCareForToday(due)); })
      .catch(() => { if (ticket === current.current) setItems([]); });
    return () => { current.current++; };
  }, [revision]));
  return items;
}
