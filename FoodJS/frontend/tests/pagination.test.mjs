import assert from "node:assert/strict";
import { test } from "node:test";
import { paginateItems } from "../src/lib/pagination.js";

const items = Array.from({ length: 23 }, (_, index) => index + 1);

test("empty lists have one valid page and no NaN counts", () => {
  assert.deepEqual(paginateItems([], 9, 10), { page: 1, pageSize: 10, totalCount: 0, totalPages: 1, pageItems: [] });
});

test("first, middle, and last pages cover every item exactly once", () => {
  const pages = [1, 2, 3].map(page => paginateItems(items, page, 10));
  assert.deepEqual(pages.flatMap(page => page.pageItems), items);
  assert.equal(pages[2].totalCount, 23);
  assert.deepEqual(pages[2].pageItems, [21, 22, 23]);
  assert.equal(items.length, 23); // Paging must not mutate the full collection.
});

test("removing the final item on a page clamps to the previous page", () => {
  assert.equal(paginateItems(items.slice(0, 20), 3, 10).page, 2);
  assert.equal(paginateItems([], 3, 10).page, 1);
});

test("page sizes 10, 20, and 50 produce consistent metadata", () => {
  for (const size of [10, 20, 50]) {
    const result = paginateItems(items, 1, size);
    assert.equal(result.pageSize, size);
    assert.equal(result.pageItems.length, Math.min(size, items.length));
    assert.equal(result.totalPages, Math.ceil(items.length / size));
  }
});

test("invalid page and size values cannot produce NaN or empty phantom pages", () => {
  for (const value of [undefined, NaN, -2, 0, 1.5, Infinity]) {
    const result = paginateItems(items, value, value);
    assert.equal(result.page, 1);
    assert.equal(result.pageSize, 10);
  }
});

test("cart totals still include items beyond the current page", () => {
  const cart = items.map(id => ({ id, price: 25, quantity: 2 }));
  const page = paginateItems(cart, 2, 10);
  assert.equal(page.pageItems.length, 10);
  assert.equal(cart.reduce((sum, item) => sum + item.price * item.quantity, 0), 1150);
});
