import { describe, expect, it } from "vitest";
import { wrapText } from "./shareCardText";

const measure = (text: string) => text.length * 10;

describe("wrapText", () => {
  it("keeps a text that fits on one line", () => {
    expect(wrapText("My time", 100, 2, measure)).toEqual(["My time"]);
  });

  it("breaks between words", () => {
    expect(wrapText("My time on cichlids", 100, 2, measure)).toEqual(["My time on", "cichlids"]);
  });

  it("ends the last line with an ellipsis when words are left over", () => {
    expect(wrapText("one two three four five six", 80, 2, measure)).toEqual(["one two", "three f…"]);
  });

  it("shortens a single word that is wider than the line", () => {
    expect(wrapText("Supercalifragilistic", 100, 1, measure)).toEqual(["Supercali…"]);
  });
});
