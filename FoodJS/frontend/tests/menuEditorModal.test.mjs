import assert from "node:assert/strict";
import { before, after, test } from "node:test";
import { act, createElement, StrictMode } from "react";
import { JSDOM } from "jsdom";
import { createServer } from "vite";

let dom, server, createRoot, Menu;
const descriptors = new Map();
const item = { id: "M-1", name: "Test burger", description: "Original description", categoryId: "C-1", category: "Burgers",
  price: 100, prepTime: 10, isAvailable: true, image: null, drinkOptions: [{ label: "Included juice", addOn: 0 }] };

before(async () => {
  dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "https://foodjs.example.test" });
  for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document,
    navigator: dom.window.navigator, IS_REACT_ACT_ENVIRONMENT: true })) {
    descriptors.set(key, Object.getOwnPropertyDescriptor(globalThis, key));
    Object.defineProperty(globalThis, key, { configurable: true, writable: true, value });
  }
  ({ createRoot } = await import("react-dom/client"));
  server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false, watch: null }, appType: "custom" });
  ({ default: Menu } = await server.ssrLoadModule("/src/components/admin/MenuManagement.jsx"));
});

after(async () => {
  await server?.close();
  dom?.window.close();
  for (const [key, descriptor] of descriptors) {
    if (descriptor) Object.defineProperty(globalThis, key, descriptor);
    else delete globalThis[key];
  }
});

async function mount(t, overrides = {}) {
  t.mock.method(globalThis, "fetch", async url => {
    assert.ok(url.startsWith("/api/menu/paged?"));
    return Response.json({ items: [item], totalCount: 1, page: 1, pageSize: 10, totalPages: 1 });
  });
  const add = t.mock.fn(async () => ({ ok: true }));
  const update = t.mock.fn(async () => ({ ok: true }));
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  await act(async () => root.render(createElement(StrictMode, null, createElement(Menu, {
    categories: [{ id: "C-1", name: "Burgers" }], onAddMenuItem: add, onUpdateMenuItem: update, ...overrides,
  }))));
  t.after(async () => { await act(async () => root.unmount()); container.remove(); });
  return { container, add, update };
}

async function open(container, editing = false) {
  const trigger = editing ? container.querySelector('[aria-label="Edit Test burger"]')
    : [...container.querySelectorAll("button")].find(button => button.textContent === "Add menu item");
  await act(async () => { trigger.focus(); trigger.click(); });
  return { trigger, dialog: container.querySelector('[role="dialog"]') };
}

async function fill(dialog, selector, value) {
  const input = dialog.querySelector(selector);
  await act(async () => {
    Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value").set.call(input, value);
    input.dispatchEvent(new window.Event("input", { bubbles: true }));
  });
}

const submit = dialog => act(async () => dialog.querySelector("form").dispatchEvent(new window.Event("submit", { bubbles: true, cancelable: true })));
const escape = dialog => act(async () => dialog.dispatchEvent(new window.KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })));

test("Add opens a modal, focuses the name, traps Tab, and restores focus and page scrolling on close", async t => {
  const { container } = await mount(t);
  assert.equal(container.querySelector('[role="dialog"]'), null);
  const { trigger, dialog } = await open(container);
  assert.equal(dialog.getAttribute("aria-modal"), "true");
  assert.match(dialog.textContent, /Add a menu item/);
  assert.equal(document.activeElement.id, "menu-name");
  assert.equal(document.body.style.overflow, "hidden");
  assert.ok(dialog.querySelector("form"));
  assert.equal(dialog.querySelector("#menu-includesDrink").checked, false);
  assert.equal(dialog.querySelector("#menu-drinkOptions"), null);
  const close = dialog.querySelector('[aria-label="Close menu item form"]');
  const cancel = [...dialog.querySelectorAll("button")].find(button => button.textContent === "Cancel");
  await act(async () => { cancel.focus(); cancel.dispatchEvent(new window.KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true })); });
  assert.ok(document.activeElement === close);
  await escape(dialog);
  assert.equal(container.querySelector('[role="dialog"]'), null);
  assert.equal(document.body.style.overflow, "");
  assert.ok(document.activeElement === trigger);
});

test("closing a dirty form asks before discarding and nested dialogs preserve scroll locking", async t => {
  const { container, add } = await mount(t);
  const { dialog } = await open(container);
  await fill(dialog, "#menu-name", "Unsaved burger");
  await escape(dialog);
  assert.equal(container.querySelectorAll('[role="dialog"]').length, 2);
  let confirmation = container.querySelectorAll('[role="dialog"]')[1];
  assert.match(confirmation.textContent, /Discard unsaved changes/);
  await act(async () => confirmation.querySelector("[data-modal-cancel]").click());
  assert.equal(container.querySelectorAll('[role="dialog"]').length, 1);
  assert.equal(document.body.style.overflow, "hidden");
  assert.equal(dialog.querySelector("#menu-name").value, "Unsaved burger");
  await escape(dialog);
  confirmation = container.querySelectorAll('[role="dialog"]')[1];
  await act(async () => [...confirmation.querySelectorAll("button")].find(button => button.textContent === "Discard changes").click());
  assert.equal(container.querySelector('[role="dialog"]'), null);
  assert.equal(document.body.style.overflow, "");
  assert.equal(add.mock.callCount(), 0);
});

test("Edit preserves saved drink options and saves through the existing update handler", async t => {
  const { container, update } = await mount(t);
  const { trigger, dialog } = await open(container, true);
  assert.equal(dialog.querySelector("#menu-name").value, item.name);
  assert.equal(dialog.querySelector("#menu-includesDrink").checked, true);
  assert.equal(dialog.querySelector("#drink-label-0").value, "Included juice");
  assert.equal(dialog.querySelector('[name="included-drink"]').checked, true);
  await fill(dialog, "#menu-price", "125.50");
  await submit(dialog);
  assert.equal(update.mock.callCount(), 1);
  assert.equal(update.mock.calls[0].arguments[0], item.id);
  const payload = update.mock.calls[0].arguments[1];
  assert.equal(payload.price, 125.5);
  assert.equal(payload.categoryId, "C-1");
  assert.deepEqual(payload.drinkOptions, [{ label: "Included juice", addOn: 0 }]);
  assert.equal(container.querySelector('[role="dialog"]'), null);
  assert.ok(document.activeElement === trigger);
});

test("required-field and 5 MB image validation remain inside the modal", async t => {
  const { container, add } = await mount(t);
  const { dialog } = await open(container);
  await submit(dialog);
  assert.equal(dialog.querySelector("#menu-name").getAttribute("aria-invalid"), "true");
  assert.equal(document.activeElement.id, "menu-name");
  assert.match(dialog.textContent, /Enter an item name/);
  const file = dialog.querySelector('input[type="file"]');
  Object.defineProperty(file, "files", { configurable: true, value: [{ type: "image/png", size: 5 * 1024 * 1024 + 1, name: "oversize.png" }] });
  await act(async () => file.dispatchEvent(new window.Event("change", { bubbles: true })));
  assert.equal(file.getAttribute("aria-invalid"), "true");
  assert.match(dialog.textContent, /5 MB/);
  assert.equal(add.mock.callCount(), 0);
});

test("new standalone items save no drinks; enabling includes a drink opens the configurable editor", async t => {
  const { container, add } = await mount(t);
  const { dialog } = await open(container);
  await fill(dialog, "#menu-name", "Standalone cola");
  await fill(dialog, "#menu-price", "59");
  await fill(dialog, "#menu-prepTime", "2");
  await act(async () => dialog.querySelector("#menu-includesDrink").click());
  assert.ok(dialog.querySelector("#menu-drinkOptions"));
  assert.equal(dialog.querySelector("#drink-label-0").value, "Go Large Iced Tea");
  // Disabling the bundle ignores hidden draft validation and persists an empty list.
  await fill(dialog, "#drink-label-0", "");
  await act(async () => dialog.querySelector("#menu-includesDrink").click());
  assert.equal(dialog.querySelector("#menu-drinkOptions"), null);
  await submit(dialog);
  assert.equal(add.mock.callCount(), 1);
  assert.deepEqual(add.mock.calls[0].arguments[0].drinkOptions, []);
});

test("switching includes a drink off/on preserves a custom draft; removing its last option turns it off", async t => {
  const { container, update } = await mount(t);
  const { dialog } = await open(container, true);
  await act(async () => dialog.querySelector("#menu-includesDrink").click());
  assert.equal(dialog.querySelector("#menu-drinkOptions"), null);
  await act(async () => dialog.querySelector("#menu-includesDrink").click());
  assert.equal(dialog.querySelector("#drink-label-0").value, "Included juice");
  await act(async () => dialog.querySelector('[aria-label="Remove drink 1"]').click());
  assert.equal(dialog.querySelector("#menu-includesDrink").checked, false);
  assert.equal(dialog.querySelector("#menu-drinkOptions"), null);
  await submit(dialog);
  assert.deepEqual(update.mock.calls[0].arguments[1].drinkOptions, []);
});

test("save failure keeps the modal and draft open; an in-flight save cannot be dismissed", async t => {
  let finish;
  const saving = new Promise(resolve => { finish = resolve; });
  const { container } = await mount(t, { onUpdateMenuItem: () => saving });
  const { dialog } = await open(container, true);
  await fill(dialog, "#menu-price", "150");
  await submit(dialog);
  assert.equal(dialog.getAttribute("aria-busy"), "true");
  assert.equal(dialog.querySelector('[aria-label="Close menu item form"]').disabled, true);
  await escape(dialog);
  assert.equal(container.querySelectorAll('[role="dialog"]').length, 1);
  await act(async () => dialog.parentElement.click());
  assert.equal(container.querySelectorAll('[role="dialog"]').length, 1);
  await act(async () => finish({ ok: false, message: "Could not save the item." }));
  assert.match(dialog.querySelector('[role="alert"]').textContent, /Could not save/);
  assert.equal(dialog.querySelector("#menu-price").value, "150");
  assert.equal(dialog.getAttribute("aria-busy"), "false");
  assert.equal(document.body.style.overflow, "hidden");
});
