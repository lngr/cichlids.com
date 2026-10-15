import { useCallback, useEffect, useRef, useState } from "react";
import type { CareKind, CarePlan, CarePlanChange } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { localTime } from "../today/localDate";

/**
 * The care plan of a tank with its due states. The plan is read again whenever the revision
 * changes, since a new entry moves the due dates.
 */
export function useCarePlan(tankId: number, enabled: boolean, revision: number) {
  const [plan, setPlan] = useState<CarePlan | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [saving, setSaving] = useState<CareKind | null>(null);
  const current = useRef(0);
  useEffect(() => {
    if (!enabled) { setPlan(null); return; }
    const ticket = ++current.current;
    void apiClient.care.plan(tankId, localTime()).then(value => {
      if (ticket === current.current) { setPlan(value); setError(null); }
    }).catch(err => { if (ticket === current.current) setError(err); });
    return () => { current.current++; };
  }, [tankId, enabled, revision]);
  const change = useCallback(async (kind: CareKind, request: CarePlanChange): Promise<boolean> => {
    const ticket = ++current.current;
    setSaving(kind);
    setError(null);
    try {
      const next = await apiClient.care.changeRule(tankId, kind, request, localTime());
      if (ticket === current.current) setPlan(next);
      return true;
    } catch (err) {
      if (ticket === current.current) setError(err);
      return false;
    } finally {
      setSaving(null);
    }
  }, [tankId]);
  return { plan, error, saving, change };
}
