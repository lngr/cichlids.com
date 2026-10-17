import { useEffect, useRef } from "react";
import { useAuth } from "../auth/AuthProvider";
import { apiClient } from "../api/client";
import { useAppNavigation } from "../navigation/NavigationProvider";
import { isDeviceNoteSeen, shouldOfferFirstTank } from "../registration/deviceNote";
import { localStore } from "../lib/localStore";
import { shouldOpenArchiveRetrospective } from "../retrospectives/retrospectiveView";

export function OnboardingGate() {
  const { status, accountId, provisional } = useAuth();
  const { send } = useAppNavigation();
  const checked = useRef<string | null>(null);
  useEffect(() => {
    if (status !== "authenticated") { checked.current = null; return; }
    if (checked.current === accountId) return;
    let cancelled = false;
    let retry: ReturnType<typeof setTimeout>;
    const check = () => {
      void apiClient.me.get().then(async me => {
        const noteSeen = provisional ? await isDeviceNoteSeen(localStore) : true;
        if (cancelled) return;
        checked.current = accountId;
        const firstTankOffered = shouldOfferFirstTank({ provisional, onboardingCompleted: Boolean(me.profile.onboardingCompletedAt), noteSeen });
        if (firstTankOffered) send({ type: "REPLACE", path: "/onboarding" });
        else if (shouldOpenArchiveRetrospective({ status: me.archiveRetrospective, provisional, firstTankOffered })) send({ type: "OPEN", path: "/retrospective/archive" });
      }).catch(() => { if (!cancelled) retry = setTimeout(check, 5000); });
    };
    check();
    return () => { cancelled = true; clearTimeout(retry); };
  }, [status, accountId, provisional, send]);
  return null;
}
