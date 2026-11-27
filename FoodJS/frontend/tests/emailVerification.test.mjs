import assert from "node:assert/strict";
import { before, after, test } from "node:test";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { createServer } from "vite";
import { verificationPath, readVerificationLink, registerForVerification, verifyEmail, resendVerification } from "../src/lib/emailVerification.js";

test("verification links use a fragment and reject malformed tokens", () => {
  const token = "A".repeat(64);
  assert.deepEqual(readVerificationLink(`#email=test%2Bfood%40example.test&token=${token}`), { email: "test+food@example.test", token });
  for (const value of ["", "short", "!".repeat(64), "A".repeat(65)])
    assert.equal(readVerificationLink(`#email=test@example.test&token=${value}`).token, "");
  assert.equal(verificationPath(" test+food@example.test "), "/verify-email?email=test%2Bfood%40example.test");
});

test("registration expects pending verification, not a JWT or an automatic login", async t => {
  t.mock.method(globalThis, "fetch", async (url, options) => {
    assert.equal(url, "/api/users/register");
    assert.equal(JSON.parse(options.body).email, "test@example.test");
    return Response.json({ verificationRequired: true, email: "test@example.test", emailSent: true, message: "Check email" }, { status: 201 });
  });
  assert.equal((await registerForVerification({ email: "test@example.test" })).verificationRequired, true);
});

test("legacy auto-login responses are not accepted as a verified registration", async t => {
  t.mock.method(globalThis, "fetch", async () => Response.json({ message: "Account created successfully.", token: "unexpected-jwt", user: { email: "test@example.test" } }, { status: 201 }));
  await assert.rejects(registerForVerification({ email: "test@example.test" }), /expected email-verification/);
});

test("verify and resend send only the expected email/token payload", async t => {
  const calls = [];
  t.mock.method(globalThis, "fetch", async (url, options) => {
    calls.push([url, JSON.parse(options.body)]);
    return Response.json({ message: "Done" });
  });
  await verifyEmail(" test@example.test ", "A".repeat(64));
  await resendVerification(" test@example.test ");
  assert.deepEqual(calls, [
    ["/api/auth/verify-email", { email: "test@example.test", token: "A".repeat(64) }],
    ["/api/auth/resend-verification", { email: "test@example.test" }],
  ]);
});

test("delivery errors reach the UI without claiming success", async t => {
  t.mock.method(globalThis, "fetch", async () => Response.json({ message: "Email delivery is not configured." }, { status: 503 }));
  await assert.rejects(registerForVerification({ email: "test@example.test" }), /not configured/);
  await assert.rejects(resendVerification("test@example.test"), /not configured/);
});

test("an empty 404 from an outdated backend explains the required restart", async t => {
  t.mock.method(globalThis, "fetch", async () => new Response(null, { status: 404 }));
  await assert.rejects(resendVerification("test@example.test"), /API was not found.*Restart/);
});

test("HTML server errors are handled without parsing crashes or exposing raw output", async t => {
  t.mock.method(globalThis, "fetch", async () => new Response("<html>private server output</html>", { status: 500 }));
  await assert.rejects(resendVerification("test@example.test"), error =>
    /backend could not complete/.test(error.message) && !/private server output|JSON/.test(error.message));
});

test("an empty success response cannot falsely confirm email verification", async t => {
  t.mock.method(globalThis, "fetch", async () => new Response(null, { status: 200 }));
  await assert.rejects(verifyEmail("test@example.test", "A".repeat(64)), /incomplete response/);
});

test("a stopped backend produces an actionable connection error", async t => {
  t.mock.method(globalThis, "fetch", async () => { throw new TypeError("fetch failed"); });
  await assert.rejects(resendVerification("test@example.test"), /Cannot connect.*backend/);
});

let server;
before(async () => {
  server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false, watch: null }, appType: "custom" });
});
after(async () => { await server?.close(); });

test("confirmation link renders a pending state, without a second confirmation button or render-time POST", async t => {
  const fetch = t.mock.method(globalThis, "fetch", async () => { throw new Error("Unexpected automatic verification"); });
  const { default: Page } = await server.ssrLoadModule("/src/pages/VerifyEmail.jsx");
  const html = renderToStaticMarkup(createElement(MemoryRouter, {
    initialEntries: [`/verify-email#email=test%40example.test&token=${"A".repeat(64)}`],
  }, createElement(Page)));
  assert.ok(html.includes("Verifying your email..."));
  assert.ok(!html.includes("Verify my email"));
  assert.ok(!html.includes("Resend verification email"));
  assert.ok(!html.includes("A".repeat(64)));
  assert.equal(fetch.mock.callCount(), 0);
});

test("pending signup shows delivery feedback and a resend form, not an authenticated menu", async () => {
  const { default: Page } = await server.ssrLoadModule("/src/pages/VerifyEmail.jsx");
  const html = renderToStaticMarkup(createElement(MemoryRouter, {
    initialEntries: [{ pathname: "/verify-email", state: { email: "pending@example.test", message: "Delivery failed. Please resend." } }],
  }, createElement(Page)));
  assert.ok(html.includes("Delivery failed. Please resend."));
  assert.ok(html.includes('value="pending@example.test"'));
  assert.ok(!html.includes("Verify my email"));
  assert.ok(html.includes("Back to sign in"));
});
