import { useCallback, useEffect, useRef, useState } from "react";
import type { CareKind, CarePlan, CarePlanChange } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { reminderRefresh } from "../reminders/refreshSignal";
import { localTime } from "../today/localDate";

/**
 * The care plan of a tank with its due states and its reminder switch. The plan is read again
 * whenever the revision changes, since a new entry moves the due dates. A change of a rule or of
 * the switch asks the reminders of the device to be rescheduled.
 */
export function useCarePlan(tankId: number, enabled: boolean, revision: number) {
  const [plan, setPlan] = useState<CarePlan | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [saving, setSaving] = useState<CareKind | null>(null);
  const [savingReminders, setSavingReminders] = useState(false);
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
      reminderRefresh.request();
      return true;
    } catch (err) {
      if (ticket === current.current) setError(err);
      return false;
    } finally {
      setSaving(null);
    }
  }, [tankId]);
  const setReminders = useCallback(async (enabled: boolean): Promise<boolean> => {
    const ticket = ++current.current;
    setSavingReminders(true);
    setError(null);
    try {
      const next = await apiClient.care.setReminders(tankId, enabled, localTime());
      if (ticket === current.current) setPlan(next);
      reminderRefresh.request();
      return true;
    } catch (err) {
      if (ticket === current.current) setError(err);
      return false;
    } finally {
      setSavingReminders(false);
    }
  }, [tankId]);
  return { plan, error, saving, savingReminders, change, setReminders };
}
