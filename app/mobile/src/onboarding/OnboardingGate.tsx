import { useEffect, useRef } from "react";
import { useAuth } from "../auth/AuthProvider";
import { apiClient } from "../api/client";
import { useAppNavigation } from "../navigation/NavigationProvider";
import { isDeviceNoteSeen, shouldOfferFirstTank } from "../registration/deviceNote";
import { localStore } from "../lib/localStore";
import { opensOnStart, returnCards } from "@cichlids/client-core";
import { shouldCheckReturnRetrospective, shouldOpenArchiveRetrospective } from "../retrospectives/retrospectiveView";
import { localTime } from "../today/localDate";

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
        const archiveOpens = shouldOpenArchiveRetrospective({ status: me.archiveRetrospective, provisional, firstTankOffered });
        if (firstTankOffered) send({ type: "REPLACE", path: "/onboarding" });
        else if (archiveOpens) send({ type: "OPEN", path: "/retrospective/archive" });
        else if (shouldCheckReturnRetrospective({ provisional, firstTankOffered, archiveOpens })) {
          void apiClient.me.returnRetrospective(localTime()).then(result => {
            if (!cancelled && result && opensOnStart(result) && returnCards(result).length > 0) send({ type: "OPEN", path: "/retrospective/return" });
          }).catch(() => {});
        }
      }).catch(() => { if (!cancelled) retry = setTimeout(check, 5000); });
    };
    check();
    return () => { cancelled = true; clearTimeout(retry); };
  }, [status, accountId, provisional, send]);
  return null;
}
