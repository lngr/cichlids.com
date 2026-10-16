import { describe, expect, it } from "vitest";
import { chosenTank, todoCountLabelKey } from "./todoGroups";

const tanks = [{ id: 4, title: "Mbuna" }, { id: 9, title: "Frontosa" }];
describe("chosenTank", () => {
  it("keeps a valid choice and otherwise takes the first tank", () => {
    expect(chosenTank(tanks, 9)).toBe(9);
    expect(chosenTank(tanks, 77)).toBe(4);
    expect(chosenTank(tanks, null)).toBe(4);
    expect(chosenTank([], 4)).toBeNull();
  });
});
describe("todoCountLabelKey", () => {
  it("names the Today link only when something is open", () => {
    expect(todoCountLabelKey(0)).toBeNull();
    expect(todoCountLabelKey(3)).toBe("today.todos");
  });
});
