import { expect, it } from "vitest";
import { evidenceKey, missingKey, stageOfValue } from "./waterChangeText";
import { parseStoredWaterChange, waterChangeStorageKey } from "./waterChangeStorage";

it("reads the stage from compound and plain machine values", () => {
  expect(stageOfValue({ running: "tips" })).toBe("tips");
  expect(stageOfValue({ running: { drainedPhoto: "ready" } })).toBe("drainedPhoto");
  expect(stageOfValue("starting")).toBeNull();
  expect(stageOfValue(null)).toBeNull();
});

it("maps evidence states and findings to translation keys", () => {
  expect(evidenceKey("verified")).toBe("waterChange.evidence.verified");
  expect(missingKey("draining_missing")).toBe("waterChange.missing.draining_missing");
  expect(missingKey("duration_too_short")).toBe("waterChange.missing.duration_too_short");
  expect(missingKey("photo_missing")).toBe("waterChange.missing.photo_missing");
  expect(missingKey("something_new")).toBeNull();
});

it("keeps a position per tank and ignores broken stored text", () => {
  expect(waterChangeStorageKey(42)).toBe("waterChange.42");
  expect(parseStoredWaterChange('{"sessionId":5,"stage":"tips"}')).toEqual({ sessionId: 5, stage: "tips" });
  expect(parseStoredWaterChange("{broken")).toBeNull();
  expect(parseStoredWaterChange('{"sessionId":"5"}')).toBeNull();
  expect(parseStoredWaterChange(null)).toBeNull();
});
