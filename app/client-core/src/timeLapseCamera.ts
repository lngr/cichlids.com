import { assign, fromPromise, setup } from "xstate";
import type { TankTimeLapse, TankTimeLapsePhoto, UploadFile } from "./client.js";

/** The opacity of the previous photo over the viewfinder when the camera opens. */
export const DEFAULT_OVERLAY_OPACITY = 0.4;

type CameraContext = {
  upload: (file: UploadFile, capturedInApp: boolean) => Promise<TankTimeLapsePhoto>;
  previous: string | null;
  opacity: number;
  overlayVisible: boolean;
  shot: UploadFile | null;
  shotInApp: boolean;
  added: TankTimeLapsePhoto | null;
  error: unknown;
};

/**
 * The opacity at which the previous photo is drawn: 0 without a previous photo or while the
 * overlay is hidden.
 */
export const overlayOpacity = (context: Pick<CameraContext, "previous" | "opacity" | "overlayVisible">): number =>
  context.previous !== null && context.overlayVisible ? context.opacity : 0;

/**
 * The URL of the newest photo of a series, which is the last one of its capture-time order and
 * has a usable image; null for an empty series.
 */
export function latestSeriesPhotoUrl(series: TankTimeLapse): string | null {
  for (let i = series.photos.length - 1; i >= 0; i--) {
    const image = series.photos[i]!.image;
    const url = image.large ?? image.medium ?? image.small ?? image.thumb;
    if (url) return url;
  }
  return null;
}

/**
 * The guided time-lapse camera: the member frames the tank with the previous photo drawn over the
 * viewfinder, takes or picks a shot, reviews it and uploads it to the series. The overlay opacity
 * and visibility belong to the member while the camera is open and survive a retake. A shot taken
 * with the camera of the app is uploaded as captured in the app; a shot picked from the library is
 * not. A failed upload returns to the review with the error.
 */
export const timeLapseCameraMachine = setup({
  types: {
    input: {} as { previous: string | null; upload: CameraContext["upload"] },
    context: {} as CameraContext,
    events: {} as
      | { type: "SET_OPACITY"; value: number }
      | { type: "TOGGLE_OVERLAY" }
      | { type: "SHOT"; file: UploadFile; capturedInApp?: boolean }
      | { type: "RETAKE" }
      | { type: "USE" },
    output: {} as TankTimeLapsePhoto,
  },
  actors: {
    send: fromPromise(async ({ input }: { input: { upload: CameraContext["upload"]; file: UploadFile; capturedInApp: boolean } }) =>
      input.upload(input.file, input.capturedInApp)),
  },
  actions: {
    setOpacity: assign({
      opacity: ({ context, event }) =>
        event.type === "SET_OPACITY" && Number.isFinite(event.value) ? Math.min(1, Math.max(0, event.value)) : context.opacity,
    }),
    toggleOverlay: assign({ overlayVisible: ({ context }) => context.previous !== null && !context.overlayVisible }),
    keepShot: assign({
      shot: ({ event }) => (event.type === "SHOT" ? event.file : null),
      shotInApp: ({ event }) => event.type === "SHOT" && event.capturedInApp === true,
      error: null,
    }),
    dropShot: assign({ shot: null, shotInApp: false }),
    clearError: assign({ error: null }),
    keepAdded: assign({ added: ({ event }) => (event as unknown as { output: TankTimeLapsePhoto }).output }),
    keepError: assign({ error: ({ event }) => (event as { error?: unknown }).error }),
  },
}).createMachine({
  id: "timeLapseCamera",
  context: ({ input }) => ({
    upload: input.upload, previous: input.previous, opacity: DEFAULT_OVERLAY_OPACITY,
    overlayVisible: input.previous !== null, shot: null, shotInApp: false, added: null, error: null,
  }),
  initial: "framing",
  on: {
    SET_OPACITY: { actions: "setOpacity" },
    TOGGLE_OVERLAY: { actions: "toggleOverlay" },
  },
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
        onDone: { target: "done", actions: "keepAdded" },
        onError: { target: "reviewing", actions: "keepError" },
      },
    },
    done: { type: "final" },
  },
  output: ({ context }) => context.added!,
});
