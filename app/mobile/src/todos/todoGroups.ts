/** The tank a new to-do goes to: the current choice while it is one of the tanks, else the first tank. */
export function chosenTank(tanks: { id: number }[], current: number | null): number | null {
  return tanks.find(tank => tank.id === current)?.id ?? tanks[0]?.id ?? null;
}

/** The translation key of the Today link to the to-do list, or null while no to-do is open. */
export const todoCountLabelKey = (open: number): "today.todos" | null => (open > 0 ? "today.todos" : null);
