import assert from "node:assert/strict";
import { before, after, test } from "node:test";
import { createElement, useState } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { createServer } from "vite";

let server;
before(async () => {
  server = await createServer({ server: { middlewareMode: true, hmr: false, watch: null }, appType: "custom" });
});
after(async () => { await server?.close(); });

const records = Array.from({ length: 23 }, (_, index) => ({
  id: index + 1, name: `Record ${index + 1}`, code: `CODE${index + 1}`, description: "Description",
  firstName: "Test", lastName: "Customer", email: `test${index}@qa.invalid`, createdAt: "2026-11-27T00:00:00Z",
  minOrderAmount: 0, maxUses: -1, status: "Pending", total: 100,
}));

for (const [file, props, label] of [
  ["CouponManagement", { coupons: records }, "coupons"],
  ["CustomersPanel", { customers: records, orders: [], selectedCustomer: null }, "customers"],
  ["CategoryManagement", { categories: records, menuItems: [] }, "categories"],
  ["OrdersPanel", { orders: records, filters: {}, statusOptions: [] }, "orders"],
]) {
  test(`${file} renders only the first page and valid pagination props`, async () => {
    const { default: Component } = await server.ssrLoadModule(`/src/components/admin/${file}.jsx`);
    const html = renderToStaticMarkup(createElement(Component, props));
    assert.ok(html.includes(`of 23 ${label}`));
    assert.ok(!html.includes("NaN"));
    assert.ok(html.includes(label === "coupons" ? "CODE10" : file === "OrdersPanel" ? ">10<" : "Record 10"));
    assert.ok(!html.includes(label === "coupons" ? "CODE11" : file === "OrdersPanel" ? ">11<" : "Record 11"));
    assert.match(html, /value="20"/);
    assert.match(html, /value="50"/);
  });
}

test("empty pagination shows 0–0, with both navigation directions disabled", async () => {
  const { default: Pagination } = await server.ssrLoadModule("/src/components/Pagination.jsx");
  const html = renderToStaticMarkup(createElement(Pagination, {
    page: 1, pageSize: 10, totalCount: 0, totalPages: 1, label: "Categories",
    onPageChange() {}, onPageSizeChange() {},
  }));
  assert.ok(html.includes("0–0 of 0 categories"));
  assert.match(html, /disabled=""[^>]*aria-label="Previous page"/);
  assert.match(html, /disabled=""[^>]*aria-label="Next page"/);
});

test("last-page controls and category/address singular labels render correctly", async () => {
  const { default: Pagination } = await server.ssrLoadModule("/src/components/Pagination.jsx");
  for (const [label, singular] of [["Categories", "category"], ["Addresses", "address"]]) {
    const html = renderToStaticMarkup(createElement(Pagination, {
      page: 1, pageSize: 10, totalCount: 1, totalPages: 1, label,
      onPageChange() {}, onPageSizeChange() {},
    }));
    assert.ok(html.includes(`1–1 of 1 ${singular}`));
  }
  const html = renderToStaticMarkup(createElement(Pagination, {
    page: 3, pageSize: 10, totalCount: 23, totalPages: 3, onPageChange() {}, onPageSizeChange() {},
  }));
  assert.ok(html.includes("21–23 of 23 items"));
  assert.match(html, /disabled=""[^>]*aria-label="Next page"/);
  assert.match(html, /aria-label="Previous page"/);
});

test("checkout/order details paginate long item lists without changing their totals", async () => {
  const { default: Details } = await server.ssrLoadModule("/src/components/OrderDetailsContent.jsx");
  const items = records.map(record => ({ ...record, price: 25, quantity: 2 }));
  const html = renderToStaticMarkup(createElement(Details, {
    order: { id: "O-1", items, subtotal: 1150, total: 1150, discount: 0, createdAt: records[0].createdAt,
      statusHistory: records.map(record => ({ changedAt: record.createdAt, toStatus: `Status ${record.id}` })) },
  }));
  assert.ok(html.includes("46 items"));
  assert.ok(html.includes("1,150.00"));
  assert.ok(html.includes("of 23 item types"));
  assert.ok(html.includes("of 23 status changes"));
  assert.ok(!html.includes("Record 11"));
  assert.ok(!html.includes("Status 11"));
});

test("paging callbacks change pages and reset to page one when page size changes", async () => {
  const { default: usePagination } = await server.ssrLoadModule("/src/hooks/usePagination.js");
  function Harness() {
    const [step, setStep] = useState(0);
    const pager = usePagination(records);
    if (step === 0) { pager.onPageChange(3); setStep(1); }
    else if (step === 1) {
      assert.equal(pager.page, 3);
      assert.equal(pager.pageItems.length, 3);
      pager.onPageSizeChange(20); setStep(2);
    } else {
      assert.equal(pager.page, 1);
      assert.equal(pager.pageSize, 20);
      assert.equal(pager.pageItems.length, 20);
    }
    return createElement("p", null, pager.pageSize);
  }
  assert.ok(renderToStaticMarkup(createElement(Harness)).includes("20"));
});

test("changing the search/category/customer reset key returns to page one", async () => {
  const { default: usePagination } = await server.ssrLoadModule("/src/hooks/usePagination.js");
  function Harness() {
    const [step, setStep] = useState(0);
    const [key, setKey] = useState("all");
    const pager = usePagination(records, 10, key);
    if (step === 0) { pager.onPageChange(3); setStep(1); }
    else if (step === 1) { assert.equal(pager.page, 3); setKey("filtered"); setStep(2); }
    else assert.equal(pager.page, 1);
    return createElement("p", null, pager.page);
  }
  assert.ok(renderToStaticMarkup(createElement(Harness)).includes("1"));
});

test("removal clamps the stored page so later additions do not jump forward", async () => {
  const { default: usePagination } = await server.ssrLoadModule("/src/hooks/usePagination.js");
  function Harness() {
    const [step, setStep] = useState(0);
    const [items, setItems] = useState(records);
    const pager = usePagination(items);
    if (step === 0) { pager.onPageChange(3); setStep(1); }
    else if (step === 1) { assert.equal(pager.page, 3); setItems(records.slice(0, 20)); setStep(2); }
    else if (step === 2) { assert.equal(pager.page, 2); setItems(records); setStep(3); }
    else assert.equal(pager.page, 2);
    return createElement("p", null, pager.page);
  }
  assert.ok(renderToStaticMarkup(createElement(Harness)).includes("2"));
});
