import { describe, expect, it } from "vitest";
import { applyFirstPage, applyNextPage, applyPrepend, emptyPagedItems, hasMorePages, nextPageOffset } from "./pagedListState";

type Item = { id: number };
const keyOf = (item: Item) => item.id;
const items = (...ids: number[]) => ids.map((id) => ({ id }));
const page = (total: number, ...ids: number[]) => ({ total, items: items(...ids) });

describe("paged list without prepended items", () => {
  it("shows the first page and pages on from the received count", () => {
    const state = applyFirstPage(emptyPagedItems<Item>(), page(3, 1, 2), keyOf);
    expect(state.items).toEqual(items(1, 2));
    expect(nextPageOffset(state)).toBe(2);
    expect(hasMorePages(state)).toBe(true);

    const next = applyNextPage(state, page(3, 3), keyOf);
    expect(next.items).toEqual(items(1, 2, 3));
    expect(hasMorePages(next)).toBe(false);
  });
});

describe("paged list with a prepended item", () => {
  it("shows the item at the top and skips it when a later page lists it", () => {
    let state = applyFirstPage(emptyPagedItems<Item>(), page(3, 1, 2), keyOf);
    state = applyPrepend(state, { id: 9 });
    expect(state.items).toEqual(items(9, 1, 2));

    state = applyNextPage(state, page(4, 3, 9), keyOf);
    expect(state.items).toEqual(items(9, 1, 2, 3));
    expect(state.pending).toEqual([]);
  });

  it("counts a skipped duplicate as received, so the next page does not repeat an item", () => {
    let state = applyFirstPage(emptyPagedItems<Item>(), page(6, 1, 2), keyOf);
    state = applyPrepend(state, { id: 9 });
    // Another member's comment 3 and the prepended 9 arrive on the second page.
    state = applyNextPage(state, page(6, 3, 9), keyOf);
    expect(nextPageOffset(state)).toBe(4);

    state = applyNextPage(state, page(6, 4, 5), keyOf);
    expect(state.items).toEqual(items(9, 1, 2, 3, 4, 5));
    expect(hasMorePages(state)).toBe(false);
  });

  it("keeps an item prepended while the first page loads when that page does not list it", () => {
    let state = applyPrepend(emptyPagedItems<Item>(), { id: 9 });
    state = applyFirstPage(state, page(2, 1, 2), keyOf);
    expect(state.items).toEqual(items(9, 1, 2));
    expect(state.pending).toEqual(items(9));
  });

  it("drops the local copy when the loaded first page lists the item", () => {
    let state = applyPrepend(emptyPagedItems<Item>(), { id: 9 });
    state = applyFirstPage(state, page(3, 1, 2, 9), keyOf);
    expect(state.items).toEqual(items(1, 2, 9));
    expect(state.pending).toEqual([]);
  });
});
