import assert from "node:assert/strict";
import { before, after, test } from "node:test";
import { act, createElement, StrictMode } from "react";
import { MemoryRouter, Route, Routes, useNavigate } from "react-router-dom";
import { JSDOM } from "jsdom";
import { createServer } from "vite";

let dom, server, createRoot, Product, NotificationContext;
const descriptors = new Map();
const item = { id: "M-1", name: "Test burger", price: 100, category: "Burgers", drinkOptions: [
  { label: "Cola upgrade", addOn: 25 }, { label: "Included juice", addOn: 0 }, { label: "Tea upgrade", addOn: 15 },
] };
const next = { ...item, id: "M-2", name: "Second burger", price: 200,
  drinkOptions: [{ label: "Included water", addOn: 0 }, { label: "Cola upgrade", addOn: 30 }] };

before(async () => {
  dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "https://foodjs.example.test" });
  for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document,
    navigator: dom.window.navigator, localStorage: dom.window.localStorage, IS_REACT_ACT_ENVIRONMENT: true })) {
    descriptors.set(key, Object.getOwnPropertyDescriptor(globalThis, key));
    Object.defineProperty(globalThis, key, { configurable: true, writable: true, value });
  }
  ({ createRoot } = await import("react-dom/client"));
  server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false, watch: null }, appType: "custom" });
  ({ default: Product } = await server.ssrLoadModule("/src/pages/ProductDetails.jsx"));
  ({ NotificationContext } = await server.ssrLoadModule("/src/context/NotificationContext.jsx"));
});
after(async () => {
  await server?.close(); dom?.window.close();
  for (const [key, descriptor] of descriptors) {
    if (descriptor) Object.defineProperty(globalThis, key, descriptor); else delete globalThis[key];
  }
});
function SwitchProduct() {
  const navigate = useNavigate();
  return createElement("button", { onClick: () => navigate("/product/M-2"), "data-next": true }, "Next product");
}
async function mount(t, { first = item, getItem } = {}) {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600, auth_version: 0 }));
  localStorage.setItem("foodjs_auth", JSON.stringify({ token: `test.${payload}.test`, user: { isEmailVerified: true } }));
  const submissions = [], notices = [];
  t.mock.method(globalThis, "fetch", async (url, options = {}) => {
    if (url === "/api/cart/items") { submissions.push(JSON.parse(options.body)); return Response.json({ message: "Added" }); }
    if (getItem) return getItem(url);
    return Response.json(url === "/api/menu/M-2" ? next : first);
  });
  const container = document.createElement("div"); document.body.appendChild(container);
  const root = createRoot(container);
  await act(async () => root.render(createElement(StrictMode, null,
    createElement(MemoryRouter, { initialEntries: ["/product/M-1"] },
      createElement(NotificationContext.Provider, { value: { showNotification: (...args) => notices.push(args) } },
        createElement(SwitchProduct), createElement(Routes, null,
          createElement(Route, { path: "/product/:id", element: createElement(Product) }),
          createElement(Route, { path: "/menu", element: createElement("h1", null, "Menu screen") })))))));
  t.after(async () => { await act(async () => root.unmount()); container.remove(); localStorage.clear(); });
  return { container, submissions, notices };
}
const radio = (container, name) => [...container.querySelectorAll('input[type="radio"]')]
  .find(input => input.closest("label").textContent.includes(name));
const add = container => container.querySelector(".product-add");

test("saved included drink is selected by default, not the first option, and is submitted without another click", async t => {
  const { container, submissions, notices } = await mount(t);
  assert.equal(radio(container, "Included juice").checked, true);
  assert.equal(radio(container, "Cola upgrade").checked, false);
  assert.match(add(container).textContent, /100\.00/);
  await act(async () => add(container).click());
  assert.deepEqual(submissions, [{ menuItemId: "M-1", quantity: 1, selectedChoice: "Included juice" }]);
  assert.equal(notices.length, 0);
});
test("customer may override the default; quantity updates preserve that choice and recalculate the full price", async t => {
  const { container, submissions } = await mount(t);
  await act(async () => radio(container, "Cola upgrade").click());
  await act(async () => container.querySelector('[aria-label="Increase quantity"]').click());
  assert.equal(radio(container, "Cola upgrade").checked, true);
  assert.equal(radio(container, "Included juice").checked, false);
  assert.match(add(container).textContent, /250\.00/);
  await act(async () => add(container).click());
  assert.deepEqual(submissions, [{ menuItemId: "M-1", quantity: 2, selectedChoice: "Cola upgrade" }]);
});
test("standalone items have no drink controls and submit an empty drink choice", async t => {
  const { container, submissions } = await mount(t, { first: { ...item, drinkOptions: [] } });
  assert.equal(container.querySelector('input[type="radio"]'), null);
  await act(async () => add(container).click());
  assert.deepEqual(submissions, [{ menuItemId: "M-1", quantity: 1, selectedChoice: "" }]);
});
test("switching products resets quantity and uses the next product's own included drink", async t => {
  const { container } = await mount(t);
  await act(async () => radio(container, "Cola upgrade").click());
  await act(async () => container.querySelector('[aria-label="Increase quantity"]').click());
  await act(async () => container.querySelector("[data-next]").click());
  assert.equal(radio(container, "Included water").checked, true);
  assert.equal(radio(container, "Cola upgrade").checked, false);
  assert.match(add(container).textContent, /200\.00/);
});
test("a late response from a previous product cannot replace the new product/default", async t => {
  const pending = [];
  const { container } = await mount(t, { getItem: url => url === "/api/menu/M-2"
    ? Response.json(next) : new Promise(resolve => pending.push(resolve)) });
  await act(async () => container.querySelector("[data-next]").click());
  assert.equal(radio(container, "Included water").checked, true);
  await act(async () => { for (const resolve of pending) resolve(Response.json(item)); });
  assert.equal(radio(container, "Included water").checked, true);
  assert.equal(radio(container, "Included juice"), undefined);
});
