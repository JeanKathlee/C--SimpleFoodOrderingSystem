import assert from "node:assert/strict";
import { before, after, test } from "node:test";
import { readFile } from "node:fs/promises";
import { act, createElement } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { JSDOM } from "jsdom";
import { createServer } from "vite";

let dom, server, createRoot, Menu, Cart, NotificationContext;
const descriptors = new Map();
const rows = Array.from({ length: 23 }, (_, index) => ({
  id: index + 1, menuItemId: `M-${index + 1}`, name: `Food ${index + 1}`,
  price: 10, quantity: index === 22 ? 2 : 1, selectedChoice: "Included drink",
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
  ({ default: Menu } = await server.ssrLoadModule("/src/pages/Menu.jsx"));
  ({ default: Cart } = await server.ssrLoadModule("/src/pages/Cart.jsx"));
  ({ NotificationContext } = await server.ssrLoadModule("/src/context/NotificationContext.jsx"));
});

after(async () => {
  await server?.close();
  dom?.window.close();
  for (const [key, descriptor] of descriptors) {
    if (descriptor) Object.defineProperty(globalThis, key, descriptor);
    else delete globalThis[key];
  }
});

async function mount(t, { page = "menu", items = rows } = {}) {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600, auth_version: 0 }));
  localStorage.setItem("foodjs_auth", JSON.stringify({ token: `test.${payload}.test`, user: { name: "Test", isEmailVerified: true } }));
  const calls = [];
  t.mock.method(globalThis, "fetch", async (url, options = {}) => {
    calls.push({ url, options });
    if (options.method) return Response.json({ message: "Done" });
    if (url === "/api/cart") return Response.json(items);
    if (url.startsWith("/api/menu")) return Response.json(rows.map(row => ({ ...row, category: "Burgers" })));
    throw new Error(`Unexpected request: ${url}`);
  });
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  await act(async () => root.render(createElement(MemoryRouter, { initialEntries: [`/${page}`] },
    createElement(NotificationContext.Provider, { value: { showNotification() {} } },
      createElement(Routes, null,
        createElement(Route, { path: "/menu", element: createElement(Menu) }),
        createElement(Route, { path: "/cart", element: page === "cart" ? createElement(Cart) : createElement("h1", null, "Cart screen") }))))));
  t.after(async () => { await act(async () => root.unmount()); container.remove(); localStorage.clear(); });
  if (page === "menu") {
    const trigger = [...container.querySelectorAll("button")].find(button => button.textContent.startsWith("My Bag"));
    assert.equal(trigger.getAttribute("aria-expanded"), "false");
    await act(async () => trigger.click());
    assert.equal(trigger.getAttribute("aria-expanded"), "true");
  }
  return { container, calls };
}

test("My Bag displays all 23 lines without pagination and keeps the complete subtotal outside the scroller", async t => {
  const { container } = await mount(t);
  const bag = container.querySelector("#menu-bag");
  const list = bag.querySelector('[aria-label="Bag items"]');
  assert.equal(list.getAttribute("role"), "region");
  assert.equal(list.tabIndex, 0);
  assert.equal(list.querySelectorAll(".menu-bag-item").length, 23);
  assert.match(list.textContent, /Food 23/);
  assert.equal(bag.querySelector(".list-pagination"), null);
  assert.equal(bag.querySelector("select"), null);
  assert.match(bag.querySelector(".menu-bag-header").textContent, /24 items/);
  const footer = bag.querySelector(".menu-bag-total");
  assert.equal(list.contains(footer), false);
  assert.match(footer.textContent, /240\.00/);
  // Product browsing remains paginated independently of the bag.
  assert.equal(container.querySelectorAll(".menu-grid .menu-card").length, 10);
  assert.ok(container.querySelector('[aria-label="Menu items pagination"]'));
  await act(async () => footer.querySelector("button").click());
  assert.match(container.textContent, /Cart screen/);
});

test("quantity, remove and clear still work for an item beyond the old first page", async t => {
  const { container, calls } = await mount(t);
  await act(async () => container.querySelector('[aria-label="Increase Food 23 quantity"]').click());
  assert.equal(calls.at(-1).url, "/api/cart/items/23");
  assert.equal(calls.at(-1).options.method, "PATCH");
  assert.deepEqual(JSON.parse(calls.at(-1).options.body), { quantity: 3 });
  assert.match(container.querySelector(".menu-bag-total").textContent, /250\.00/);
  await act(async () => container.querySelectorAll(".menu-bag-item")[22].querySelector(".remove").click());
  assert.equal(calls.at(-1).options.method, "DELETE");
  assert.equal(container.querySelectorAll(".menu-bag-item").length, 22);
  assert.match(container.querySelector(".menu-bag-total").textContent, /220\.00/);
  await act(async () => container.querySelector(".menu-bag-header-actions button").click());
  assert.equal(calls.at(-1).url, "/api/cart");
  assert.equal(calls.at(-1).options.method, "DELETE");
  assert.match(container.querySelector("#menu-bag").textContent, /Your bag is empty/);
  assert.equal(container.querySelector(".menu-bag-header-actions button").disabled, true);
});

test("empty bag has no scrolling list, subtotal or page controls", async t => {
  const { container } = await mount(t, { items: [] });
  const bag = container.querySelector("#menu-bag");
  assert.match(bag.textContent, /0 items/);
  assert.match(bag.textContent, /Your bag is empty/);
  assert.equal(bag.querySelector(".menu-bag-list, .menu-bag-total, .list-pagination"), null);
});

test("full Cart shows every line and the complete total without page controls", async t => {
  const { container } = await mount(t, { page: "cart" });
  assert.equal(container.querySelectorAll(".cart-item").length, 23);
  assert.match(container.querySelector(".cart-list").textContent, /Food 23/);
  assert.equal(container.querySelector(".list-pagination"), null);
  assert.match(container.querySelector(".cart-summary").textContent, /240\.00/);
});

test("bag CSS bounds the panel and scrolls only items without shrinking cards or the footer", async () => {
  const css = await readFile("src/styles.css", "utf8");
  const rule = selector => css.match(new RegExp(`\\.${selector} \\{([^}]+)\\}`))[1];
  assert.match(rule("menu-bag-dropdown"), /max-height:[^;]+100dvh/);
  assert.match(rule("menu-bag-list"), /min-height:\s*0/);
  assert.match(rule("menu-bag-list"), /overflow-y:\s*auto/);
  assert.match(rule("menu-bag-item"), /flex-shrink:\s*0/);
  assert.match(rule("menu-bag-total"), /flex-shrink:\s*0/);
});
