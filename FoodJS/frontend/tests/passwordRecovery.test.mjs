import assert from "node:assert/strict";
import { before, after, test } from "node:test";
import { act, createElement, StrictMode } from "react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { JSDOM } from "jsdom";
import { createServer } from "vite";
import { readPasswordResetLink, requestPasswordReset, resetPassword } from "../src/lib/passwordRecovery.js";

const token = "A".repeat(64);
test("reset links use fragments and reject malformed tokens", () => {
  assert.deepEqual(readPasswordResetLink(`#email=qa%2Bfood%40example.test&token=${token}`), { email: "qa+food@example.test", token });
  for (const value of ["", "short", "!".repeat(64), "A".repeat(65)])
    assert.equal(readPasswordResetLink(`#email=qa@example.test&token=${value}`).token, "");
});
test("recovery requests post the expected fields, with no auth requirement", async t => {
  const calls = [];
  t.mock.method(globalThis, "fetch", async (path, options) => {
    calls.push([path, JSON.parse(options.body)]); return Response.json({ message: "Done" });
  });
  await requestPasswordReset(" qa@example.test "); await resetPassword("qa@example.test", token, "new-password");
  assert.deepEqual(calls, [["/api/auth/forgot-password", { email: "qa@example.test" }],
    ["/api/auth/reset-password", { email: "qa@example.test", token, newPassword: "new-password" }]]);
});
test("old development API responses cannot bypass email ownership", async t => {
  t.mock.method(globalThis, "fetch", async () => Response.json({ message: "Done", resetToken: token }));
  await assert.rejects(requestPasswordReset("qa@example.test"), /unexpected response/);
});
test("empty or HTML failures never cause JSON parsing crashes or leak server output", async t => {
  t.mock.method(globalThis, "fetch", async () => new Response("<html>private details</html>", { status: 500 }));
  await assert.rejects(requestPasswordReset("qa@example.test"), error => /backend could not/.test(error.message) && !/private|JSON/.test(error.message));
});
test("rate limits and network failures have actionable errors", async t => {
  const fetch = t.mock.method(globalThis, "fetch", async () => new Response(null, { status: 429 }));
  await assert.rejects(requestPasswordReset("qa@example.test"), /Too many requests/);
  fetch.mock.mockImplementation(async () => { throw new TypeError("Offline"); });
  await assert.rejects(requestPasswordReset("qa@example.test"), /Cannot connect/);
});

let server, dom, createRoot, Forgot, Reset;
const globals = new Map();
before(async () => {
  dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "https://foodjs.example.test" });
  for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document,
    navigator: dom.window.navigator, localStorage: dom.window.localStorage, IS_REACT_ACT_ENVIRONMENT: true })) {
    globals.set(key, Object.getOwnPropertyDescriptor(globalThis, key));
    Object.defineProperty(globalThis, key, { configurable: true, writable: true, value });
  }
  ({ createRoot } = await import("react-dom/client"));
  server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false, watch: null }, appType: "custom" });
  ({ default: Forgot } = await server.ssrLoadModule("/src/pages/ForgotPassword.jsx"));
  ({ default: Reset } = await server.ssrLoadModule("/src/pages/ResetPassword.jsx"));
});
after(async () => {
  await server?.close(); dom?.window.close();
  for (const [key, descriptor] of globals) {
    if (descriptor) Object.defineProperty(globalThis, key, descriptor); else delete globalThis[key];
  }
});
function LocationStatus() {
  const location = useLocation();
  return createElement("output", { "data-location": true }, location.pathname + location.search + location.hash);
}
async function mount(t, url) {
  const container = document.createElement("div"); document.body.appendChild(container);
  const root = createRoot(container);
  await act(async () => root.render(createElement(StrictMode, null, createElement(MemoryRouter, { initialEntries: [url] },
    createElement(LocationStatus), createElement(Routes, null,
      createElement(Route, { path: "/forgot-password", element: createElement(Forgot) }),
      createElement(Route, { path: "/reset-password", element: createElement(Reset) }),
      createElement(Route, { path: "/login", element: createElement("h1", null, "Login screen") }))))));
  t.after(async () => { await act(async () => root.unmount()); container.remove(); localStorage.clear(); });
  return container;
}
async function fill(input, value) {
  await act(async () => {
    Object.getOwnPropertyDescriptor(dom.window.HTMLInputElement.prototype, "value").set.call(input, value);
    input.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
  });
}
async function submit(container) {
  await act(async () => container.querySelector("form").dispatchEvent(new dom.window.Event("submit", { bubbles: true, cancelable: true })));
}
test("forgot password stays on the request page and starts a resend cooldown", async t => {
  const fetch = t.mock.method(globalThis, "fetch", async () => Response.json({ message: "Check your inbox and spam folder." }));
  const container = await mount(t, "/forgot-password");
  await fill(container.querySelector("input"), "qa@example.test"); await submit(container);
  assert.equal(fetch.mock.callCount(), 1);
  assert.match(container.querySelector('[role="status"]').textContent, /Check your inbox/);
  assert.equal(container.querySelector("[data-location]").textContent, "/forgot-password");
  assert.equal(container.querySelector('button[type="submit"]').disabled, true);
  await submit(container); assert.equal(fetch.mock.callCount(), 1);
});
test("reset scrubs the URL, rejects mismatches, then shows success and OK returns to login", async t => {
  const fetch = t.mock.method(globalThis, "fetch", async () => Response.json({ message: "Password reset successfully." }));
  localStorage.setItem("foodjs_auth", "old-session");
  const container = await mount(t, `/reset-password#email=qa%40example.test&token=${token}`);
  assert.equal(container.querySelector("[data-location]").textContent, "/reset-password");
  assert.ok(!container.innerHTML.includes(token)); assert.equal(fetch.mock.callCount(), 0);
  await fill(container.querySelector('[aria-label="New password"]'), "new-password");
  await fill(container.querySelector('[aria-label="Confirm password"]'), "different-password");
  await submit(container); assert.equal(fetch.mock.callCount(), 0);
  assert.match(container.querySelector('[role="alert"]').textContent, /do not match/);
  await fill(container.querySelector('[aria-label="Confirm password"]'), "new-password"); await submit(container);
  assert.equal(fetch.mock.callCount(), 1);
  assert.deepEqual(JSON.parse(fetch.mock.calls[0].arguments[1].body), { email: "qa@example.test", token, newPassword: "new-password" });
  assert.match(container.querySelector('[role="status"]').textContent, /Password changed successfully/);
  assert.equal(localStorage.getItem("foodjs_auth"), null); assert.equal(container.querySelector("form"), null);
  const ok = container.querySelector("button"); assert.equal(ok.textContent, "OK");
  await act(async () => ok.click()); assert.match(container.textContent, /Login screen/);
});
test("missing links never submit and expired links never display success", async t => {
  const fetch = t.mock.method(globalThis, "fetch", async () => Response.json({ message: "The password-reset link is invalid or expired." }, { status: 400 }));
  const missing = await mount(t, "/reset-password");
  assert.equal(missing.querySelector("form"), null); assert.equal(fetch.mock.callCount(), 0);
  const container = await mount(t, `/reset-password#email=qa%40example.test&token=${token}`);
  await fill(container.querySelector('[aria-label="New password"]'), "new-password");
  await fill(container.querySelector('[aria-label="Confirm password"]'), "new-password"); await submit(container);
  assert.match(container.querySelector('[role="alert"]').textContent, /expired/);
  assert.equal(container.querySelector('[role="status"]'), null);
});
test("blocked browser storage cannot turn a successful reset into an error", async t => {
  t.mock.method(globalThis, "fetch", async () => Response.json({ message: "Password reset successfully." }));
  t.mock.method(dom.window.Storage.prototype, "removeItem", () => { throw new Error("Storage blocked"); });
  const container = await mount(t, `/reset-password#email=qa%40example.test&token=${token}`);
  await fill(container.querySelector('[aria-label="New password"]'), "new-password");
  await fill(container.querySelector('[aria-label="Confirm password"]'), "new-password"); await submit(container);
  assert.match(container.querySelector('[role="status"]').textContent, /Password changed successfully/);
  assert.equal(container.querySelector('[role="alert"]'), null);
});
