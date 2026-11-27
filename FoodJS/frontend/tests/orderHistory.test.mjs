import assert from "node:assert/strict";
import { before, after, test } from "node:test";
import { act, createElement } from "react";
import { MemoryRouter } from "react-router-dom";
import { JSDOM } from "jsdom";
import { createServer } from "vite";

let dom, server, createRoot, History;
const descriptors = new Map();
const records = Array.from({ length: 11 }, (_, index) => ({
  id: `O-${1001 + index}`, createdAt: "2026-11-27T08:00:00Z", status: "Pending", total: 100, itemCount: 1,
}));

before(async () => {
  dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "https://foodjs.example.test" });
  for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document,
    navigator: dom.window.navigator, localStorage: dom.window.localStorage, IS_REACT_ACT_ENVIRONMENT: true })) {
    descriptors.set(key, Object.getOwnPropertyDescriptor(globalThis, key));
    Object.defineProperty(globalThis, key, { configurable: true, writable: true, value });
  }
  ({ createRoot } = await import("react-dom/client"));
  server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false, watch: null }, appType: "custom" });
  ({ default: History } = await server.ssrLoadModule("/src/pages/OrderHistory.jsx"));
});

after(async () => {
  await server?.close();
  dom?.window.close();
  for (const [key, descriptor] of descriptors) {
    if (descriptor) Object.defineProperty(globalThis, key, descriptor);
    else delete globalThis[key];
  }
});

async function mount(t, { orders = [], notices = [], failOrders = false } = {}) {
  const calls = [];
  t.mock.method(globalThis, "fetch", async url => {
    calls.push(url);
    if (url === "/api/notifications") return Response.json(notices);
    assert.ok(url.startsWith("/api/orders/paged?"));
    if (failOrders) return Response.json({ message: "Unable to fetch orders." }, { status: 500 });
    const query = new URL(url, "https://foodjs.example.test").searchParams;
    const search = (query.get("search") || "").toLowerCase();
    const matching = orders.filter(order => (!search || order.id.toLowerCase().includes(search))
      && (!query.get("status") || order.status === query.get("status")));
    const pageSize = Number(query.get("pageSize"));
    const totalPages = Math.max(1, Math.ceil(matching.length / pageSize));
    const page = Math.min(Number(query.get("page")), totalPages);
    return Response.json({ items: matching.slice((page - 1) * pageSize, page * pageSize),
      totalCount: matching.length, page, pageSize, totalPages });
  });
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  await act(async () => root.render(createElement(MemoryRouter, null, createElement(History))));
  t.after(async () => { await act(async () => root.unmount()); container.remove(); });
  return { container, calls };
}

const waitForSearch = () => act(async () => { await new Promise(resolve => setTimeout(resolve, 350)); });

test("empty history has no Browse menu button or pagination", async t => {
  const { container } = await mount(t);
  assert.match(container.querySelector(".order-empty").textContent, /No orders yet/);
  assert.ok(!container.textContent.includes("Browse menu"));
  assert.equal(container.querySelector(".list-pagination"), null);
  // The existing header navigation is preserved.
  assert.equal(container.querySelector('header a[href="/menu"]').textContent, "Back to menu");
});

test("a single page of orders and notifications hides unnecessary controls", async t => {
  const { container } = await mount(t, { orders: records.slice(0, 3), notices: [
    { id: 1, title: "Order updated", message: "Preparing your food", createdAt: records[0].createdAt },
  ] });
  assert.equal(container.querySelectorAll(".history-card").length, 3);
  assert.equal(container.querySelectorAll(".notification-item").length, 1);
  assert.equal(container.querySelector(".list-pagination"), null);
  assert.equal(container.querySelector(".order-empty"), null);
});

test("multi-page history still loads older orders through the server-paged endpoint", async t => {
  const { container, calls } = await mount(t, { orders: records });
  assert.equal(container.querySelectorAll(".history-card").length, 10);
  const pager = container.querySelector('[aria-label="Orders pagination"]');
  assert.ok(pager);
  await act(async () => pager.querySelector('[aria-label="Next page"]').click());
  assert.equal(container.querySelectorAll(".history-card").length, 1);
  assert.match(container.querySelector(".history-card").textContent, /O-1011/);
  assert.ok(calls.some(url => url.includes("page=2")));
  assert.ok(container.querySelector('[aria-label="Orders pagination"]'));
});

test("an unmatched search offers Clear filters, hides pagination, and restores orders when cleared", async t => {
  const { container, calls } = await mount(t, { orders: records });
  const input = container.querySelector('input[type="search"]');
  await act(async () => {
    Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value").set.call(input, "missing-order");
    input.dispatchEvent(new window.Event("input", { bubbles: true }));
  });
  await waitForSearch();
  assert.ok(calls.some(url => new URL(url, "https://foodjs.example.test").searchParams.get("search") === "missing-order"));
  assert.match(container.querySelector(".order-empty").textContent, /No matching orders/);
  assert.ok(!container.textContent.includes("Browse menu"));
  assert.equal(container.querySelector('[aria-label="Orders pagination"]'), null);
  const clear = container.querySelector(".order-empty button");
  assert.equal(clear.textContent, "Clear filters");
  await act(async () => clear.click());
  await waitForSearch();
  assert.equal(input.value, "");
  assert.equal(container.querySelector(".order-empty"), null);
  assert.equal(container.querySelectorAll(".history-card").length, 10);
  assert.ok(container.querySelector('[aria-label="Orders pagination"]'));
});

test("a failed history request is not misrepresented as no orders", async t => {
  const { container } = await mount(t, { failOrders: true });
  assert.match(container.querySelector('[role="alert"]').textContent, /Unable to fetch orders/);
  assert.equal(container.querySelector(".order-empty"), null);
  assert.equal(container.querySelector(".list-pagination"), null);
});
