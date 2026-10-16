import { describe, expect, it } from "vitest";
import { careViewPath, parseReminderData, reminderData } from "./notificationData";

describe("notificationData", () => {
  const reminder = { tankId: 4, tankTitle: "Mbuna", kind: "water_change" as const, dueDate: "2026-10-15" };
  it("round-trips the data of a reminder", () => {
    expect(parseReminderData(reminderData(reminder))).toEqual(reminder);
  });
  it("reads nothing from the data of another notification", () => {
    expect(parseReminderData(undefined)).toBeNull();
    expect(parseReminderData({})).toBeNull();
    expect(parseReminderData({ ...reminderData(reminder), kind: "feeding" })).toBeNull();
    expect(parseReminderData({ ...reminderData(reminder), tankId: "x" })).toBeNull();
  });
  it("opens the care view of the tank", () => {
    expect(careViewPath(4)).toBe("/care?tank=4");
  });
});
