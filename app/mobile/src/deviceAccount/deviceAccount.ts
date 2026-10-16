import type { DeviceAuth, DeviceCredential } from "@cichlids/client-core";

/**
 * "idle": no registration runs. "starting": the first attempt to register is under way. "ready":
 * the installation has a credential and can ask for device tokens. "offline": registering did not
 * succeed yet and is retried in the background.
 */
export type DevicePhase = "idle" | "starting" | "ready" | "offline";

export interface DeviceAccountState {
  phase: DevicePhase;
  /** The provisional profile of the installation; null until the API assigned it. */
  profileId: number | null;
  /** Whether the installation holds a credential; null until the store was read. */
  hasCredential: boolean | null;
  /**
   * True when the member signed in to an account and left the content of the device account on
   * the device. The device account stays until the member signs out or takes the content along.
   */
  leftBehind: boolean;
}

export interface ClearableCredentialStore {
  load(): Promise<DeviceCredential | undefined>;
  save(credential: DeviceCredential): Promise<void>;
  clear(): Promise<void>;
}

export interface FlagStore {
  get(key: string): Promise<string | null>;
  set(key: string, value: string): Promise<void>;
  remove(key: string): Promise<void>;
}

export interface DeviceAccountDependencies {
  /** Creates the device authentication of the current credential, and a fresh one after discard(). */
  createAuth(): DeviceAuth;
  credentials: ClearableCredentialStore;
  flags: FlagStore;
  /** Deletes the private key of the installation. */
  destroyKey(): Promise<void>;
  /** After this many milliseconds without an answer the first attempt counts as offline and the app stops waiting for it. */
  firstAttemptPatienceMs: number;
  /** The waits between attempts after a failure; the last one repeats. */
  retryDelaysMs: readonly number[];
}

export interface DeviceAccount {
  getState(): DeviceAccountState;
  subscribe(listener: () => void): () => void;
  /** Registers the installation when it has no credential and keeps trying in the background until it works. */
  start(): void;
  /** Ends the background attempts. */
  stop(): void;
  /** Retries at once when registering is offline. */
  retryNow(): void;
  /** Reads the credential and the left behind flag from the store without contacting the API. */
  inspect(): Promise<void>;
  setLeftBehind(value: boolean): Promise<void>;
  /** The device authentication of the current credential; the object changes after discard(). */
  auth(): DeviceAuth;
  /** Removes credential, key and flags after a registration handed the content over. */
  discard(): Promise<void>;
}

export const LEFT_BEHIND_FLAG = "cichlids.device.leftBehind";

const INITIAL_STATE: DeviceAccountState = { phase: "idle", profileId: null, hasCredential: null, leftBehind: false };

/** Registers the installation with the API and keeps its device account alive; a failure never blocks the caller. */
export function createDeviceAccount(dependencies: DeviceAccountDependencies): DeviceAccount {
  let state = INITIAL_STATE;
  let auth = dependencies.createAuth();
  let running = false;
  let generation = 0;
  let attempting = false;
  let failures = 0;
  let retryTimer: ReturnType<typeof setTimeout> | undefined;
  let patienceTimer: ReturnType<typeof setTimeout> | undefined;
  const listeners = new Set<() => void>();

  function update(change: Partial<DeviceAccountState>) {
    state = { ...state, ...change };
    for (const listener of [...listeners]) listener();
  }

  function clearTimers() {
    clearTimeout(retryTimer);
    clearTimeout(patienceTimer);
    retryTimer = undefined;
    patienceTimer = undefined;
  }

  async function attempt() {
    if (attempting || !running) return;
    attempting = true;
    const mine = generation;
    try {
      const credential = await auth.ensureRegistered();
      if (mine !== generation) return;
      clearTimers();
      failures = 0;
      update({ phase: "ready", profileId: credential.profileId, hasCredential: true });
    } catch {
      if (mine !== generation) return;
      clearTimeout(patienceTimer);
      update({ phase: "offline" });
      const delays = dependencies.retryDelaysMs;
      const delay = delays[Math.min(failures, delays.length - 1)] ?? 0;
      failures += 1;
      retryTimer = setTimeout(() => {
        retryTimer = undefined;
        void attempt();
      }, delay);
    } finally {
      if (mine === generation) attempting = false;
    }
  }

  function stop() {
    running = false;
    attempting = false;
    generation += 1;
    failures = 0;
    clearTimers();
    if (state.phase !== "idle") update({ phase: "idle" });
  }

  return {
    getState: () => state,
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    start() {
      if (running) return;
      running = true;
      update({ phase: "starting" });
      patienceTimer = setTimeout(() => {
        patienceTimer = undefined;
        if (state.phase === "starting") update({ phase: "offline" });
      }, dependencies.firstAttemptPatienceMs);
      void attempt();
    },
    stop,
    retryNow() {
      if (!running || attempting || state.phase !== "offline") return;
      clearTimeout(retryTimer);
      retryTimer = undefined;
      void attempt();
    },
    async inspect() {
      const [credential, flag] = await Promise.all([dependencies.credentials.load(), dependencies.flags.get(LEFT_BEHIND_FLAG)]);
      update({ hasCredential: credential !== undefined, profileId: credential?.profileId ?? state.profileId, leftBehind: flag === "1" });
    },
    async setLeftBehind(value) {
      if (value) await dependencies.flags.set(LEFT_BEHIND_FLAG, "1");
      else await dependencies.flags.remove(LEFT_BEHIND_FLAG);
      update({ leftBehind: value });
    },
    auth: () => auth,
    async discard() {
      stop();
      await dependencies.credentials.clear();
      await dependencies.flags.remove(LEFT_BEHIND_FLAG);
      await dependencies.destroyKey();
      auth = dependencies.createAuth();
      update({ ...INITIAL_STATE, hasCredential: false });
    },
  };
}
