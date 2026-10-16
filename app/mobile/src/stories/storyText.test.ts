import { expect, it } from "vitest";
import type { EditorSubject } from "@cichlids/client-core";
import { filterByTitle, subjectLabel } from "./storyText";

const t = (key: string, options?: Record<string, unknown>) => `${key}${options ? JSON.stringify(options) : ""}`;
const base = { id: 1, type: "tank", position: null, tank: null, inhabitant: null, species: null } as const;

it("filters rows by a case-insensitive part of the label and keeps all rows for a blank query", () => {
  const rows = [{ id: 1, label: "Reef Tank" }, { id: 2, label: "Mbuna" }];
  expect(filterByTitle(rows, " reef ").map(row => row.id)).toEqual([1]);
  expect(filterByTitle(rows, "  ")).toEqual(rows);
});

it("names each kind of subject and nothing for one that is not visible", () => {
  expect(subjectLabel({ ...base, tank: { id: 1, title: "Reef", image: null } } as EditorSubject, t)).toBe('stories.subject.tank{"title":"Reef"}');
  expect(subjectLabel({ ...base, type: "species", species: { id: 2, displayName: "Pundamilia" } } as EditorSubject, t)).toBe('stories.subject.species{"name":"Pundamilia"}');
  expect(subjectLabel({ ...base, type: "inhabitant", inhabitant: { id: 3, tankId: 1, tankTitle: "Reef", speciesName: null, count: 2 } } as EditorSubject, t))
    .toBe('stories.subject.inhabitant{"name":"common.unknownValue","tank":"Reef"}');
  expect(subjectLabel(base as EditorSubject, t)).toBeNull();
});
