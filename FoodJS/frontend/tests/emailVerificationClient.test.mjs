import assert from "node:assert/strict";
import { before, after, test } from "node:test";
import { act, createElement, StrictMode } from "react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { JSDOM } from "jsdom";
import { createServer } from "vite";

let server, dom, createRoot, Page;
const globals = new Map();
const token = "A".repeat(64);
const confirmationUrl = `/verify-email#email=buyer%40example.test&token=${token}`;

before(async () => {
  dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "https://foodjs.example.test" });
  for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document,
    navigator: dom.window.navigator, IS_REACT_ACT_ENVIRONMENT: true })) {
    globals.set(key, Object.getOwnPropertyDescriptor(globalThis, key));
    Object.defineProperty(globalThis, key, { configurable: true, writable: true, value });
  }
  ({ createRoot } = await import("react-dom/client"));
  server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false, watch: null }, appType: "custom" });
  ({ default: Page } = await server.ssrLoadModule("/src/pages/VerifyEmail.jsx"));
});

after(async () => {
  await server?.close();
  dom?.window.close();
  for (const [key, descriptor] of globals) {
    if (descriptor) Object.defineProperty(globalThis, key, descriptor);
    else delete globalThis[key];
  }
});

function LocationStatus() {
  const location = useLocation();
  return createElement("output", { "data-location": true }, location.pathname + location.search + location.hash);
}

async function mount(t, url = confirmationUrl) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  await act(async () => root.render(createElement(StrictMode, null,
    createElement(MemoryRouter, { initialEntries: [url] },
      createElement(LocationStatus),
      createElement(Routes, null,
        createElement(Route, { path: "/verify-email", element: createElement(Page) }),
        createElement(Route, { path: "/login", element: createElement("h1", null, "Login screen") }))))));
  t.after(async () => { await act(async () => root.unmount()); container.remove(); });
  return container;
}

test("opening a link verifies once under StrictMode, scrubs the token, and OK navigates to login", async t => {
  let finish;
  const request = new Promise(resolve => { finish = resolve; });
  const fetch = t.mock.method(globalThis, "fetch", (path, options) => {
    assert.equal(path, "/api/auth/verify-email");
    assert.equal(options.method, "POST");
    assert.deepEqual(JSON.parse(options.body), { email: "buyer@example.test", token });
    return request;
  });
  const container = await mount(t);
  assert.equal(fetch.mock.callCount(), 1);
  assert.match(container.textContent, /Verifying your email/);
  assert.equal(container.querySelector("[data-location]").textContent, "/verify-email");
  assert.ok(!container.innerHTML.includes(token));
  await act(async () => finish(Response.json({ message: "Email verified successfully. You can now sign in." })));
  assert.match(container.textContent, /Email verified successfully!/);
  assert.equal(container.querySelector("[data-location]").textContent, "/verify-email");
  assert.equal(container.querySelector("form"), null);
  const ok = container.querySelector("button");
  assert.equal(ok.textContent, "OK");
  assert.equal(document.activeElement, ok);
  await act(async () => ok.click());
  assert.match(container.textContent, /Login screen/);
  assert.equal(container.querySelector("[data-location]").textContent, "/login");
  assert.equal(fetch.mock.callCount(), 1);
});

test("expired or consumed links show an error and resend, never false success", async t => {
  const calls = [];
  t.mock.method(globalThis, "fetch", async path => {
    calls.push(path);
    return path.endsWith("verify-email")
      ? Response.json({ message: "This verification link is invalid, expired, or already used." }, { status: 400 })
      : Response.json({ message: "Check your inbox and spam folder." });
  });
  const container = await mount(t);
  assert.match(container.querySelector('[role="alert"]').textContent, /expired/);
  assert.ok(!container.textContent.includes("Email verified successfully!"));
  assert.equal(container.querySelector("input").readOnly, true);
  await act(async () => container.querySelector("form").dispatchEvent(new window.Event("submit", { bubbles: true, cancelable: true })));
  assert.deepEqual(calls, ["/api/auth/verify-email", "/api/auth/resend-verification"]);
  assert.match(container.textContent, /Check your inbox/);
  assert.match(container.querySelector("button").textContent, /Resend in 60s/);
  assert.equal(container.querySelector("button").disabled, true);
});

test("pending signup without a token sends nothing until the user requests a resend", async t => {
  const fetch = t.mock.method(globalThis, "fetch", async () => Response.json({ message: "Check your inbox." }));
  const container = await mount(t, "/verify-email?email=buyer%40example.test");
  assert.equal(fetch.mock.callCount(), 0);
  assert.equal(container.querySelector("input").readOnly, false);
  assert.match(container.textContent, /Resend verification email/);
});

test("malformed or incomplete links do not POST a token and show the resend form", async t => {
  const fetch = t.mock.method(globalThis, "fetch", async () => { throw new Error("Must not verify"); });
  const container = await mount(t, "/verify-email#email=buyer%40example.test&token=invalid");
  assert.equal(fetch.mock.callCount(), 0);
  assert.match(container.querySelector('[role="alert"]').textContent, /incomplete or invalid/);
  assert.equal(container.querySelector("[data-location]").textContent, "/verify-email");
});

test("network failures cannot display success or redirect to login", async t => {
  const fetch = t.mock.method(globalThis, "fetch", async () => { throw new TypeError("Offline"); });
  const container = await mount(t);
  assert.equal(fetch.mock.callCount(), 1);
  assert.match(container.querySelector('[role="alert"]').textContent, /Cannot connect/);
  assert.ok(!container.textContent.includes("Email verified successfully!"));
  assert.equal(container.querySelector("[data-location]").textContent, "/verify-email");
});
