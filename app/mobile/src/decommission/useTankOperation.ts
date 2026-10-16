import { useCallback, useRef, useState } from "react";
import { useFocusEffect } from "expo-router";
import type { TankOperation } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { localTime } from "../today/localDate";

/**
 * The operating time and the decommissionings of a tank, read whenever the screen gains focus. Null
 * while unread and when they cannot be read, for instance because the viewer does not see the care
 * stream of the tank.
 */
export function useTankOperation(tankId: number): TankOperation | null {
  const [operation, setOperation] = useState<TankOperation | null>(null);
  const current = useRef(0);
  useFocusEffect(useCallback(() => {
    const ticket = ++current.current;
    void apiClient.tanks.operation(tankId, localTime())
      .then(result => { if (ticket === current.current) setOperation(result); })
      .catch(() => { if (ticket === current.current) setOperation(null); });
    return () => { current.current++; };
  }, [tankId]));
  return operation;
}
