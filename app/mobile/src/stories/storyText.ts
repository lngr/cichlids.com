import type { EditorSubject } from "@cichlids/client-core";

type Translate = (key: string, options?: Record<string, unknown>) => string;

/** The rows whose label contains the query, ignoring case and surrounding blanks; a blank query keeps all rows. */
export function filterByTitle<T extends { label: string }>(rows: T[], query: string): T[] {
  const needle = query.trim().toLocaleLowerCase();
  return needle ? rows.filter(row => row.label.toLocaleLowerCase().includes(needle)) : rows;
}

/** The line that names a subject of a story; null for a subject the member may not see. */
export function subjectLabel(subject: EditorSubject, t: Translate): string | null {
  if (subject.tank) return t("stories.subject.tank", { title: subject.tank.title });
  if (subject.species) return t("stories.subject.species", { name: subject.species.displayName });
  if (subject.inhabitant) {
    return t("stories.subject.inhabitant", { name: subject.inhabitant.speciesName ?? t("common.unknownValue"), tank: subject.inhabitant.tankTitle });
  }
  return null;
}
