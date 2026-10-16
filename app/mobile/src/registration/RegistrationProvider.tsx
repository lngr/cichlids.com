import { createContext, useCallback, useContext, useEffect, useMemo, useReducer, useRef, type ReactNode } from "react";
import { completeRegistration, type RegistrationPreview, type RegistrationReason } from "@cichlids/client-core";
import { apiClient, setRegistrationRequiredListener } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { deviceAccount } from "../deviceAccount/useAccountStatus";
import { localTime } from "../today/localDate";
import { CLOSED, outcomeStep, reduceSheet, type SheetState } from "./registrationSheet";

interface RegistrationContextValue {
  sheet: SheetState;
  /** Opens the registration sheet with the reason; an open sheet stays as it is. */
  requestRegistration(reason: RegistrationReason): void;
  closeSheet(): void;
  /** Answers the question whether to take the content of the device along. */
  decide(takeAlong: boolean): void;
  /** Retries the handover of the content after a failure. */
  retry(): void;
  /** Leaves the content on the device and goes on with the account. */
  postpone(): void;
  /** Offers the content that stayed on the device to the account. */
  takeAlong(): void;
}

const RegistrationContext = createContext<RegistrationContextValue | null>(null);

export function useRegistration(): RegistrationContextValue {
  const value = useContext(RegistrationContext);
  if (!value) throw new Error("useRegistration must be used inside RegistrationProvider.");
  return value;
}

/**
 * The function a gated action calls first: for the provisional account it opens the registration
 * sheet with the reason and returns true, so the caller stops; otherwise it returns false.
 */
export function useRegistrationGate(): (reason: RegistrationReason) => boolean {
  const { provisional } = useAuth();
  const { requestRegistration } = useRegistration();
  return useCallback((reason) => {
    if (!provisional) return false;
    requestRegistration(reason);
    return true;
  }, [provisional, requestRegistration]);
}

/**
 * Holds the registration sheet and hands the content of the device account to the account that
 * signs in: after the sign-in it asks the API what the registration brings over, upgrades the
 * profile of the device or, when the account already has a profile, asks the member whether to
 * take the content along. An answer of the API that an action needs a registered account opens the
 * sheet as well.
 */
export function RegistrationProvider({ children }: { children: ReactNode }) {
  const { provisional, registrationPending } = useAuth();
  const [sheet, dispatch] = useReducer(reduceSheet, CLOSED);
  const answer = useRef<((takeAlong: boolean) => void) | null>(null);
  const running = useRef(false);

  const requestRegistration = useCallback((reason: RegistrationReason) => dispatch({ type: "open", reason }), []);

  useEffect(() => {
    setRegistrationRequiredListener(() => requestRegistration("generic"));
    return () => setRegistrationRequiredListener(null);
  }, [requestRegistration]);

  const run = useCallback(async () => {
    if (running.current || !deviceAccount) return;
    running.current = true;
    dispatch({ type: "working" });
    try {
      const outcome = await completeRegistration(apiClient.registration, localTime(), (preview: RegistrationPreview) =>
        new Promise<boolean>((resolve) => {
          answer.current = resolve;
          dispatch({ type: "decision", preview });
        }));
      if (outcome.kind === "declined") await deviceAccount.setLeftBehind(true);
      else {
        // A member who brings tanks along has no first tank to create, so the onboarding counts as done.
        const content = outcome.kind === "moved" ? outcome.moved : outcome.content;
        if (content.tanks > 0) await apiClient.me.completeOnboarding({ skip: true }).catch(() => undefined);
        await deviceAccount.discard();
      }
      dispatch(outcomeStep(outcome));
    } catch (error) {
      dispatch({ type: "fail", error });
    } finally {
      answer.current = null;
      running.current = false;
    }
  }, []);

  useEffect(() => {
    if (registrationPending) void run();
  }, [registrationPending, run]);

  // A registration that finished without the sheet taking part (the account signed in elsewhere) leaves nothing to show.
  useEffect(() => {
    if (!provisional && !registrationPending && sheet.step === "reason") dispatch({ type: "close" });
  }, [provisional, registrationPending, sheet.step]);

  const value = useMemo<RegistrationContextValue>(() => ({
    sheet,
    requestRegistration,
    closeSheet: () => dispatch({ type: "close" }),
    decide: (takeAlong) => answer.current?.(takeAlong),
    retry: () => void run(),
    postpone: () => {
      dispatch({ type: "close" });
      void deviceAccount?.setLeftBehind(true);
    },
    takeAlong: () => void deviceAccount?.setLeftBehind(false),
  }), [sheet, requestRegistration, run]);

  return <RegistrationContext.Provider value={value}>{children}</RegistrationContext.Provider>;
}
