import { assign, fromPromise, setup } from "xstate";
import type { CareEntry, CareParameter, CareSeries, MeasurementOrigin, MeasurementUnit, MeasurementValue, MeasurementValueRequest } from "./client.js";

export type UnitSystem = "metric" | "us";

/** Parameters in the order the form and the graphs list them. */
export const MEASUREMENT_PARAMETERS: CareParameter[] = ["ph", "no3", "temperature", "kh", "gh", "no2", "nh4", "po4", "conductivity", "tds"];
/** Parameters the form shows before the member asks for more. */
export const COMMON_PARAMETERS: CareParameter[] = ["ph", "no3", "temperature"];
export const MEASUREMENT_ORIGINS: MeasurementOrigin[] = ["test_kit", "meter", "probe"];

// Aquarium hobby convention: 1 German degree is 17.848 ppm as CaCO3, for general and carbonate hardness.
const PPM_PER_GERMAN_DEGREE = 17.848;
const CANONICAL_UNIT: Record<CareParameter, MeasurementUnit> = {
  temperature: "celsius", ph: "none", kh: "dkh", gh: "dgh", no2: "mg_per_liter", no3: "mg_per_liter",
  nh4: "mg_per_liter", po4: "mg_per_liter", conductivity: "microsiemens_per_cm", tds: "ppm",
};
// Plausible range of the canonical value, matching the API.
const RANGE: Record<CareParameter, [number, number]> = {
  temperature: [0, 45], ph: [0, 14], kh: [0, 50], gh: [0, 100], no2: [0, 100], no3: [0, 1000],
  nh4: [0, 100], po4: [0, 100], conductivity: [0, 100000], tds: [0, 100000],
};

/** The unit a member of the given system enters a parameter in. */
export function inputUnit(parameter: CareParameter, system: UnitSystem): MeasurementUnit {
  if (system === "us") {
    if (parameter === "temperature") return "fahrenheit";
    if (parameter === "kh" || parameter === "gh") return "ppm";
  }
  return CANONICAL_UNIT[parameter];
}

const round = (value: number) => Math.round(value * 1e6) / 1e6;

/** Converts a value in the canonical unit of the parameter to the given unit. */
export function fromCanonical(parameter: CareParameter, value: number, unit: MeasurementUnit): number {
  if (unit === "fahrenheit") return round(value * 9 / 5 + 32);
  if (unit === "ppm" && (parameter === "kh" || parameter === "gh")) return round(value * PPM_PER_GERMAN_DEGREE);
  return value;
}

function toCanonical(parameter: CareParameter, value: number, unit: MeasurementUnit): number {
  if (unit === "fahrenheit") return (value - 32) * 5 / 9;
  if (unit === "ppm" && (parameter === "kh" || parameter === "gh")) return value / PPM_PER_GERMAN_DEGREE;
  return value;
}

/** A stored value as the member's system shows it: the entered number when its unit already matches, else the converted normalized value. */
export function valueInSystem(value: MeasurementValue, system: UnitSystem): { value: number; unit: MeasurementUnit } {
  const unit = inputUnit(value.parameter, system);
  return { value: value.unit === unit ? Number(value.value) : fromCanonical(value.parameter, Number(value.normalizedValue), unit), unit };
}

export type MeasurementDraft = {
  system: UnitSystem;
  expanded: boolean;
  origin: MeasurementOrigin | null;
  fields: Partial<Record<CareParameter, string>>;
};
export type DraftErrors = Partial<Record<CareParameter, "invalid" | "range">>;

export const createDraft = (system: UnitSystem): MeasurementDraft => ({ system, expanded: false, origin: null, fields: {} });
export const showMore = (draft: MeasurementDraft): MeasurementDraft => ({ ...draft, expanded: true });
export const setOrigin = (draft: MeasurementDraft, origin: MeasurementOrigin | null): MeasurementDraft => ({ ...draft, origin });
export const setField = (draft: MeasurementDraft, parameter: CareParameter, text: string): MeasurementDraft =>
  ({ ...draft, fields: { ...draft.fields, [parameter]: text } });
export const visibleParameters = (draft: MeasurementDraft): CareParameter[] => draft.expanded ? MEASUREMENT_PARAMETERS : COMMON_PARAMETERS;

/** The form of an existing measurement, with each value in the member's unit and every filled parameter visible. */
export function editDraft(system: UnitSystem, entry: CareEntry): MeasurementDraft {
  const values = entry.values ?? [];
  const fields: MeasurementDraft["fields"] = {};
  for (const value of values) {
    fields[value.parameter] = String(valueInSystem(value, system).value);
  }
  return {
    system, fields, origin: values.find(value => value.origin)?.origin ?? null,
    expanded: values.some(value => !COMMON_PARAMETERS.includes(value.parameter)),
  };
}

/**
 * The values of the filled fields as an API request, or null when nothing is filled or a field
 * is unusable; the errors name each unusable field.
 */
export function validateDraft(draft: MeasurementDraft): { values: MeasurementValueRequest[] | null; errors: DraftErrors } {
  const values: MeasurementValueRequest[] = [];
  const errors: DraftErrors = {};
  for (const parameter of MEASUREMENT_PARAMETERS) {
    const text = draft.fields[parameter]?.trim();
    if (!text) continue;
    const value = /^\d+([.,]\d+)?$/.test(text) ? Number(text.replace(",", ".")) : NaN;
    const unit = inputUnit(parameter, draft.system);
    if (!Number.isFinite(value)) errors[parameter] = "invalid";
    else {
      const canonical = toCanonical(parameter, value, unit);
      const [minimum, maximum] = RANGE[parameter];
      if (canonical < minimum || canonical > maximum) errors[parameter] = "range";
      else values.push({ parameter, value, unit, origin: draft.origin });
    }
  }
  return { values: values.length > 0 && Object.keys(errors).length === 0 ? values : null, errors };
}

export type DisplayPoint = { entryId: number | string; occurredAt: string; value: number };
export type DisplaySeries = { parameter: CareParameter; unit: MeasurementUnit; points: DisplayPoint[] };

/** A series with its values converted to the units of the member's system. */
export function seriesForDisplay(series: CareSeries, system: UnitSystem): DisplaySeries {
  const unit = inputUnit(series.parameter, system);
  return {
    parameter: series.parameter, unit,
    points: series.points.map(point => ({ entryId: point.entryId, occurredAt: point.occurredAt, value: fromCanonical(series.parameter, Number(point.value), unit) })),
  };
}

export type ScaledPoint = { entryId: number | string; x: number; y: number };

/** Positions the points inside a box: oldest at the left, the highest value at the top. */
export function scaleSeries(points: DisplayPoint[], width: number, height: number): ScaledPoint[] {
  if (points.length === 0) return [];
  const times = points.map(point => Date.parse(point.occurredAt));
  const values = points.map(point => point.value);
  const [firstTime, lastTime] = [Math.min(...times), Math.max(...times)];
  const [low, high] = [Math.min(...values), Math.max(...values)];
  return points.map((point, index) => ({
    entryId: point.entryId,
    x: lastTime === firstTime ? width / 2 : (times[index] - firstTime) / (lastTime - firstTime) * width,
    y: high === low ? height / 2 : height - (point.value - low) / (high - low) * height,
  }));
}

type Effects = {
  log: (values: MeasurementValueRequest[]) => Promise<CareEntry>;
  update: (id: number, values: MeasurementValueRequest[]) => Promise<CareEntry>;
  remove: (id: number) => Promise<CareEntry>;
};
type Context = { effects: Effects; draft: MeasurementDraft; entry: CareEntry | null; error: unknown; revision: number };

export const measurementMachine = setup({
  types: {
    input: {} as Effects,
    context: {} as Context,
    events: {} as { type: "OPEN"; system: UnitSystem; entry?: CareEntry }
      | { type: "CHANGE"; parameter: CareParameter; text: string }
      | { type: "ORIGIN"; origin: MeasurementOrigin | null }
      | { type: "MORE" | "SAVE" | "DELETE" | "CANCEL" },
  },
  actors: {
    save: fromPromise(({ input }: { input: { effects: Effects; entry: CareEntry | null; values: MeasurementValueRequest[] } }) =>
      input.entry ? input.effects.update(Number(input.entry.id), input.values) : input.effects.log(input.values)),
    remove: fromPromise(({ input }: { input: { effects: Effects; id: number } }) => input.effects.remove(input.id)),
  },
  guards: {
    valid: ({ context }) => validateDraft(context.draft).values !== null,
    existing: ({ context }) => context.entry !== null,
  },
  actions: {
    open: assign(({ event }) => event.type === "OPEN"
      ? { draft: event.entry ? editDraft(event.system, event.entry) : createDraft(event.system), entry: event.entry ?? null, error: null }
      : {}),
    change: assign({ draft: ({ context, event }) => event.type === "CHANGE" ? setField(context.draft, event.parameter, event.text) : context.draft }),
    origin: assign({ draft: ({ context, event }) => event.type === "ORIGIN" ? setOrigin(context.draft, event.origin) : context.draft }),
    more: assign({ draft: ({ context }) => showMore(context.draft) }),
    finished: assign({ entry: null, error: null, revision: ({ context }) => context.revision + 1 }),
    failed: assign({ error: ({ event }) => (event as { error?: unknown }).error }),
  },
}).createMachine({
  id: "measurement",
  context: ({ input }) => ({ effects: input, draft: createDraft("metric"), entry: null, error: null, revision: 0 }),
  initial: "idle",
  states: {
    idle: { on: { OPEN: { target: "editing", actions: "open" } } },
    editing: {
      on: {
        CHANGE: { actions: "change" }, ORIGIN: { actions: "origin" }, MORE: { actions: "more" },
        SAVE: { guard: "valid", target: "saving" },
        DELETE: { guard: "existing", target: "deleting" },
        CANCEL: "idle",
      },
    },
    saving: {
      invoke: {
        src: "save",
        input: ({ context }) => ({ effects: context.effects, entry: context.entry, values: validateDraft(context.draft).values! }),
        onDone: { target: "idle", actions: "finished" },
        onError: { target: "editing", actions: "failed" },
      },
    },
    deleting: {
      invoke: {
        src: "remove",
        input: ({ context }) => ({ effects: context.effects, id: Number(context.entry!.id) }),
        onDone: { target: "idle", actions: "finished" },
        onError: { target: "editing", actions: "failed" },
      },
    },
  },
});
