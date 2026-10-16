import { assign, fromCallback, fromPromise, setup, type ActorRefFrom } from "xstate";
import type {
  CareAmountRequest, UploadFile, WaterChangeFinished, WaterChangeSession, WaterChangeStep, WaterChangeStepState,
} from "./client.js";

/** The screens of the guided water change in the order the member meets them. */
export const WATER_CHANGE_STAGES = [
  "heaterFilterOff", "draining", "drainedPhoto", "tips", "refill", "heaterFilterOn", "refilledPhoto", "amount",
] as const;
export type WaterChangeStage = (typeof WATER_CHANGE_STAGES)[number];

/** The number of photos a session holds at most. */
export const WATER_CHANGE_MAX_PHOTOS = 6;

/** The tips of the tips stage: optional, and recorded as done or skipped. */
export type WaterChangeTip = Extract<WaterChangeStep, "conditioner" | "temperature_matched">;

/** The position the app keeps on the device, so a closed app reopens the wizard where it stopped. */
export type StoredWaterChange = { sessionId: number; stage: WaterChangeStage };

export type WaterChangeEffects = {
  /** Starts the session on the server, or returns the open one. */
  start: () => Promise<WaterChangeSession>;
  markStep: (id: number, step: WaterChangeStep, state: WaterChangeStepState) => Promise<WaterChangeSession>;
  addPhoto: (id: number, file: UploadFile, capturedInApp: boolean) => Promise<WaterChangeSession>;
  finish: (id: number, amount: CareAmountRequest | null) => Promise<WaterChangeFinished>;
  cancel: (id: number) => Promise<void>;
  storage: { load: () => Promise<StoredWaterChange | null>; save: (value: StoredWaterChange | null) => Promise<void> };
  /** The current time in milliseconds. */
  now: () => number;
};

type Context = {
  effects: WaterChangeEffects;
  session: WaterChangeSession | null;
  initialStage: WaterChangeStage;
  drainStartedAtMs: number | null;
  now: number;
  amount: CareAmountRequest | null;
  tips: Partial<Record<WaterChangeTip, WaterChangeStepState>>;
  finished: WaterChangeFinished | null;
  error: unknown;
  syncFailures: number;
  queue: { tail: Promise<unknown> };
};

type Events =
  | { type: "DONE" | "SKIP" | "FINISH" | "CANCEL" | "RETRY" }
  | { type: "TIP"; step: WaterChangeTip; state: WaterChangeStepState }
  | { type: "PHOTO"; file: UploadFile; capturedInApp?: boolean }
  | { type: "SET_AMOUNT"; amount: CareAmountRequest | null }
  | { type: "TICK"; now: number }
  | { type: "SYNCED"; session: WaterChangeSession }
  | { type: "SYNC_FAILED" };

const marked = (session: WaterChangeSession, step: WaterChangeStep) => session.steps.some(s => s.step === step);

// Whether the session holds a photo that arrived at or after the mark of the step.
const photoSince = (session: WaterChangeSession, step: WaterChangeStep) => {
  const mark = session.steps.find(s => s.step === step);
  return mark !== undefined && session.photos.some(p => Date.parse(p.receivedAt) >= Date.parse(mark.at));
};

/**
 * The stage a session continues with: the first stage whose step is not marked and whose photo is
 * not stored. A step the member skipped counts as handled. The photo of a photo stage is a photo
 * received after the mark of the step before it.
 */
export function nextWaterChangeStage(session: WaterChangeSession): WaterChangeStage {
  if (!marked(session, "heater_filter_off")) return "heaterFilterOff";
  if (!marked(session, "draining_started")) return "draining";
  if (!photoSince(session, "draining_started") && !marked(session, "drained_photo")) return "drainedPhoto";
  if (!marked(session, "conditioner") && !marked(session, "temperature_matched")) return "tips";
  if (!marked(session, "refill_started")) return "refill";
  if (!marked(session, "heater_filter_on")) return "heaterFilterOn";
  if (!photoSince(session, "heater_filter_on") && !marked(session, "refilled_photo")) return "refilledPhoto";
  return "amount";
}

const laterStage = (a: WaterChangeStage, b: WaterChangeStage) =>
  WATER_CHANGE_STAGES.indexOf(a) >= WATER_CHANGE_STAGES.indexOf(b) ? a : b;

const stepAt = (session: WaterChangeSession | null, step: WaterChangeStep): number | null => {
  const found = session?.steps.find(s => s.step === step && s.state === "done");
  return found ? Date.parse(found.at) : null;
};

// Keeps the later mark of every step and the longer list of photos, so answers that arrive out of
// order never take a mark or a photo away.
function merge(current: WaterChangeSession | null, incoming: WaterChangeSession): WaterChangeSession {
  if (!current) return incoming;
  const steps = new Map(current.steps.map(s => [s.step, s]));
  for (const s of incoming.steps) {
    const known = steps.get(s.step);
    if (!known || Date.parse(s.at) >= Date.parse(known.at)) steps.set(s.step, s);
  }
  return {
    ...incoming, steps: [...steps.values()],
    photos: incoming.photos.length >= current.photos.length ? incoming.photos : current.photos,
  };
}

/** Whole seconds from the start of draining to the current time; 0 before draining started. */
export function drainingSeconds(context: Pick<Context, "drainStartedAtMs" | "now">): number {
  return context.drainStartedAtMs === null ? 0 : Math.max(0, Math.floor((context.now - context.drainStartedAtMs) / 1000));
}

/** Whole seconds from the start of the session to the current time. */
export function totalSeconds(context: Pick<Context, "session" | "now">): number {
  return context.session ? Math.max(0, Math.floor((context.now - Date.parse(context.session.startedAt)) / 1000)) : 0;
}

/** Elapsed seconds as m:ss, or h:mm:ss from one hour on. */
export function formatElapsed(seconds: number): string {
  const total = Math.max(0, Math.floor(seconds));
  const pad = (value: number) => String(value).padStart(2, "0");
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  return hours > 0 ? `${hours}:${pad(minutes)}:${pad(total % 60)}` : `${minutes}:${pad(total % 60)}`;
}

/**
 * The guided water change. The machine starts or resumes the session on the server, walks the member
 * through the stages and finishes the session. A step mark is sent in the background in order; a
 * failing mark, a skipped step or a skipped photo never blocks the next stage, the server records
 * what is missing. The position is kept on the device between app starts; the marks and photos on the
 * server set the earliest stage a session resumes at.
 */
function enqueueMark(context: Context, self: { send: (event: Events) => void }, step: WaterChangeStep, state: WaterChangeStepState) {
  if (!context.session) return;
  const id = Number(context.session.id);
  context.queue.tail = context.queue.tail
    .then(() => context.effects.markStep(id, step, state))
    .then(session => self.send({ type: "SYNCED", session }), () => self.send({ type: "SYNC_FAILED" }));
}

export const waterChangeMachine = setup({
  types: {
    input: {} as WaterChangeEffects,
    context: {} as Context,
    events: {} as Events,
    output: {} as { finished: WaterChangeFinished | null },
  },
  actors: {
    begin: fromPromise(async ({ input }: { input: WaterChangeEffects }) => {
      const session = await input.start();
      const stored = await input.storage.load().catch(() => null);
      return { session, stored };
    }),
    upload: fromPromise(async ({ input }: { input: { effects: WaterChangeEffects; id: number; file: UploadFile; capturedInApp: boolean } }) =>
      input.effects.addPhoto(input.id, input.file, input.capturedInApp)),
    complete: fromPromise(async ({ input }: { input: { effects: WaterChangeEffects; id: number; amount: CareAmountRequest | null } }) =>
      input.effects.finish(input.id, input.amount)),
    discard: fromPromise(async ({ input }: { input: { effects: WaterChangeEffects; id: number } }) => input.effects.cancel(input.id)),
    ticker: fromCallback<Events, WaterChangeEffects>(({ sendBack, input }) => {
      const timer = setInterval(() => sendBack({ type: "TICK", now: input.now() }), 1000);
      return () => clearInterval(timer);
    }),
  },
  actions: {
    remember: ({ context }, params: { stage: WaterChangeStage }) => {
      if (context.session) void context.effects.storage.save({ sessionId: Number(context.session.id), stage: params.stage }).catch(() => {});
    },
    forget: ({ context }) => { void context.effects.storage.save(null).catch(() => {}); },
    mark: ({ context, self }, params: { step: WaterChangeStep; state: WaterChangeStepState }) => enqueueMark(context, self, params.step, params.state),
    markTip: assign(({ context, event, self }) => {
      if (event.type !== "TIP") return {};
      enqueueMark(context, self, event.step, event.state);
      return { tips: { ...context.tips, [event.step]: event.state } };
    }),
    skipUnchosenTips: ({ context, self }) => {
      for (const tip of ["conditioner", "temperature_matched"] as const) {
        if (!context.tips[tip]) enqueueMark(context, self, tip, "skipped");
      }
    },
    startDrainClock: assign({ drainStartedAtMs: ({ context }) => context.drainStartedAtMs ?? context.effects.now() }),
    synced: assign(({ context, event }) => {
      if (event.type !== "SYNCED") return {};
      const session = merge(context.session, event.session);
      return { session, drainStartedAtMs: stepAt(session, "draining_started") ?? context.drainStartedAtMs };
    }),
    syncFailed: assign({ syncFailures: ({ context }) => context.syncFailures + 1 }),
    tick: assign({ now: ({ context, event }) => (event.type === "TICK" ? event.now : context.now) }),
    keepAmount: assign({ amount: ({ event }) => (event.type === "SET_AMOUNT" ? event.amount : null) }),
    keepUploaded: assign(({ context, event }) => ({ session: merge(context.session, (event as unknown as { output: WaterChangeSession }).output), error: null })),
    keepFinished: assign({ finished: ({ event }) => (event as unknown as { output: WaterChangeFinished }).output }),
    keepError: assign({ error: ({ event }) => (event as unknown as { error: unknown }).error }),
    clearError: assign({ error: null }),
  },
  guards: {
    hasPhotoSince: ({ context }, params: { step: WaterChangeStep }) => context.session !== null && photoSince(context.session, params.step),
    canTakeAnotherPhoto: ({ context }) => (context.session?.photos.length ?? 0) < WATER_CHANGE_MAX_PHOTOS,
    atStage: ({ context }, params: { stage: WaterChangeStage }) => context.initialStage === params.stage,
  },
}).createMachine({
  id: "waterChange",
  context: ({ input }) => ({
    effects: input, session: null, initialStage: "heaterFilterOff", drainStartedAtMs: null, now: input.now(),
    amount: null, tips: {}, finished: null, error: null, syncFailures: 0, queue: { tail: Promise.resolve() },
  }),
  initial: "starting",
  invoke: { src: "ticker", input: ({ context }) => context.effects },
  on: {
    TICK: { actions: "tick" },
    SYNCED: { actions: "synced" },
    SYNC_FAILED: { actions: "syncFailed" },
  },
  states: {
    starting: {
      invoke: {
        src: "begin",
        input: ({ context }) => context.effects,
        onDone: {
          target: "routing",
          actions: assign(({ context, event }) => {
            const { session, stored } = event.output;
            const derived = nextWaterChangeStage(session);
            const hinted = stored && Number(stored.sessionId) === Number(session.id) ? stored.stage : derived;
            return {
              session, initialStage: laterStage(derived, hinted), drainStartedAtMs: stepAt(session, "draining_started"), now: context.effects.now(),
            };
          }),
        },
        onError: { target: "failed", actions: "keepError" },
      },
    },
    failed: { on: { RETRY: { target: "starting", actions: "clearError" } } },
    routing: {
      always: WATER_CHANGE_STAGES.map(stage => ({ guard: { type: "atStage" as const, params: { stage } }, target: `running.${stage}` })),
    },
    running: {
      initial: "heaterFilterOff",
      on: { CANCEL: "cancelling" },
      states: {
        heaterFilterOff: {
          entry: { type: "remember", params: { stage: "heaterFilterOff" } },
          on: {
            DONE: { target: "draining", actions: { type: "mark", params: { step: "heater_filter_off", state: "done" } } },
            SKIP: { target: "draining", actions: { type: "mark", params: { step: "heater_filter_off", state: "skipped" } } },
          },
        },
        draining: {
          entry: { type: "remember", params: { stage: "draining" } },
          on: {
            DONE: { target: "drainedPhoto", actions: ["startDrainClock", { type: "mark", params: { step: "draining_started", state: "done" } }] },
            SKIP: { target: "drainedPhoto", actions: { type: "mark", params: { step: "draining_started", state: "skipped" } } },
          },
        },
        drainedPhoto: {
          entry: { type: "remember", params: { stage: "drainedPhoto" } },
          initial: "ready",
          states: {
            ready: {
              always: { guard: { type: "hasPhotoSince", params: { step: "draining_started" as const } }, target: "added" },
              on: {
                PHOTO: "uploading",
                SKIP: { target: "#waterChange.running.tips", actions: ["clearError", { type: "mark", params: { step: "drained_photo", state: "skipped" } }] },
              },
            },
            uploading: {
              invoke: {
                src: "upload",
                input: ({ context, event }) => ({
                  effects: context.effects, id: Number(context.session!.id),
                  file: (event as Extract<Events, { type: "PHOTO" }>).file, capturedInApp: (event as Extract<Events, { type: "PHOTO" }>).capturedInApp ?? true,
                }),
                onDone: { target: "added", actions: "keepUploaded" },
                onError: { target: "ready", actions: "keepError" },
              },
            },
            added: {
              on: {
                PHOTO: { guard: "canTakeAnotherPhoto", target: "uploading" },
                DONE: "#waterChange.running.tips",
              },
            },
          },
        },
        tips: {
          entry: { type: "remember", params: { stage: "tips" } },
          on: {
            TIP: { actions: "markTip" },
            DONE: { target: "refill", actions: "skipUnchosenTips" },
            SKIP: { target: "refill", actions: "skipUnchosenTips" },
          },
        },
        refill: {
          entry: { type: "remember", params: { stage: "refill" } },
          on: {
            DONE: { target: "heaterFilterOn", actions: { type: "mark", params: { step: "refill_started", state: "done" } } },
            SKIP: { target: "heaterFilterOn", actions: { type: "mark", params: { step: "refill_started", state: "skipped" } } },
          },
        },
        heaterFilterOn: {
          entry: { type: "remember", params: { stage: "heaterFilterOn" } },
          on: {
            DONE: { target: "refilledPhoto", actions: { type: "mark", params: { step: "heater_filter_on", state: "done" } } },
            SKIP: { target: "refilledPhoto", actions: { type: "mark", params: { step: "heater_filter_on", state: "skipped" } } },
          },
        },
        refilledPhoto: {
          entry: { type: "remember", params: { stage: "refilledPhoto" } },
          initial: "ready",
          states: {
            ready: {
              always: { guard: { type: "hasPhotoSince", params: { step: "heater_filter_on" as const } }, target: "added" },
              on: {
                PHOTO: "uploading",
                SKIP: { target: "#waterChange.running.amount", actions: ["clearError", { type: "mark", params: { step: "refilled_photo", state: "skipped" } }] },
              },
            },
            uploading: {
              invoke: {
                src: "upload",
                input: ({ context, event }) => ({
                  effects: context.effects, id: Number(context.session!.id),
                  file: (event as Extract<Events, { type: "PHOTO" }>).file, capturedInApp: (event as Extract<Events, { type: "PHOTO" }>).capturedInApp ?? true,
                }),
                onDone: { target: "added", actions: "keepUploaded" },
                onError: { target: "ready", actions: "keepError" },
              },
            },
            added: {
              on: {
                PHOTO: { guard: "canTakeAnotherPhoto", target: "uploading" },
                DONE: "#waterChange.running.amount",
              },
            },
          },
        },
        amount: {
          entry: { type: "remember", params: { stage: "amount" } },
          on: { SET_AMOUNT: { actions: "keepAmount" }, FINISH: { target: "#waterChange.finishing", actions: "clearError" } },
        },
      },
    },
    finishing: {
      invoke: {
        src: "complete",
        input: ({ context }) => ({ effects: context.effects, id: Number(context.session!.id), amount: context.amount }),
        onDone: { target: "finished", actions: "keepFinished" },
        onError: { target: "running.amount", actions: "keepError" },
      },
    },
    cancelling: {
      invoke: {
        src: "discard",
        input: ({ context }) => ({ effects: context.effects, id: Number(context.session!.id) }),
        onDone: "cancelled",
        onError: { target: "running.amount", actions: "keepError" },
      },
    },
    finished: { type: "final", entry: "forget" },
    cancelled: { type: "final", entry: "forget" },
  },
  output: ({ context }) => ({ finished: context.finished }),
});

export type WaterChangeActor = ActorRefFrom<typeof waterChangeMachine>;
