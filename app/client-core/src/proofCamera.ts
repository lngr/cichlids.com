import { assign, fromPromise, setup } from "xstate";
import type { UploadFile } from "./client.js";
import type { TankOperation } from "./decommission.js";

type ProofContext = {
  upload: (file: UploadFile, capturedInApp: boolean) => Promise<TankOperation>;
  shot: UploadFile | null;
  shotInApp: boolean;
  result: TankOperation | null;
  error: unknown;
};

/**
 * The camera that takes the photo proving the state of a tank when it is decommissioned or put back
 * into operation: the member takes or picks a shot, reviews it and sends it. A shot taken with the
 * camera of the app is sent as captured in the app; a picked shot is not. A failed send returns to the
 * review with the error.
 */
export const proofCameraMachine = setup({
  types: {
    input: {} as { upload: ProofContext["upload"] },
    context: {} as ProofContext,
    events: {} as
      | { type: "SHOT"; file: UploadFile; capturedInApp?: boolean }
      | { type: "RETAKE" }
      | { type: "USE" },
    output: {} as TankOperation,
  },
  actors: {
    send: fromPromise(async ({ input }: { input: { upload: ProofContext["upload"]; file: UploadFile; capturedInApp: boolean } }) =>
      input.upload(input.file, input.capturedInApp)),
  },
  actions: {
    keepShot: assign({
      shot: ({ event }) => (event.type === "SHOT" ? event.file : null),
      shotInApp: ({ event }) => event.type === "SHOT" && event.capturedInApp === true,
      error: null,
    }),
    dropShot: assign({ shot: null, shotInApp: false }),
    clearError: assign({ error: null }),
    keepResult: assign({ result: ({ event }) => (event as unknown as { output: TankOperation }).output }),
    keepError: assign({ error: ({ event }) => (event as { error?: unknown }).error }),
  },
}).createMachine({
  id: "proofCamera",
  context: ({ input }) => ({ upload: input.upload, shot: null, shotInApp: false, result: null, error: null }),
  initial: "framing",
  states: {
    framing: { on: { SHOT: { target: "reviewing", actions: "keepShot" } } },
    reviewing: {
      on: {
        SHOT: { actions: "keepShot" },
        RETAKE: { target: "framing", actions: "dropShot" },
        USE: { target: "uploading", actions: "clearError" },
      },
    },
    uploading: {
      invoke: {
        src: "send",
        input: ({ context }) => ({ upload: context.upload, file: context.shot!, capturedInApp: context.shotInApp }),
        onDone: { target: "done", actions: "keepResult" },
        onError: { target: "reviewing", actions: "keepError" },
      },
    },
    done: { type: "final" },
  },
  output: ({ context }) => context.result!,
});
