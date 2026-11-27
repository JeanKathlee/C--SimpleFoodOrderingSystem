import assert from "node:assert/strict";
import { test } from "node:test";
import { getAuthSession, saveAuthSession } from "../src/lib/auth.js";

const jwt = claims => `header.${Buffer.from(JSON.stringify(claims)).toString("base64url")}.signature`;

function withStorage(t) {
  const previous = Object.getOwnPropertyDescriptor(globalThis, "localStorage");
  const values = new Map();
  Object.defineProperty(globalThis, "localStorage", { configurable: true, value: {
    setItem(key, value) { values.set(key, value); },
    getItem(key) { return values.get(key) ?? null; },
    removeItem(key) { values.delete(key); },
  } });
  t.after(() => {
    if (previous) Object.defineProperty(globalThis, "localStorage", previous);
    else Reflect.deleteProperty(globalThis, "localStorage");
  });
  return values;
}

test("old pre-verification sessions and unverified accounts are cleared locally", t => {
  const values = withStorage(t);
  const expiry = Date.now() / 1000 + 3600;
  for (const session of [
    { token: jwt({ exp: expiry }), user: { isEmailVerified: true } },
    { token: jwt({ exp: expiry, auth_version: 0 }), user: { isEmailVerified: false } },
    { token: jwt({ exp: expiry, auth_version: 0 }), user: {} },
  ]) {
    saveAuthSession(session);
    assert.equal(getAuthSession(), null);
    assert.equal(values.size, 0);
  }
});

test("verified versioned sessions work until their JWT expires", t => {
  withStorage(t);
  const session = { token: jwt({ exp: Date.now() / 1000 + 3600, auth_version: 1 }), user: { isEmailVerified: true, role: "customer" } };
  saveAuthSession(session);
  assert.deepEqual(getAuthSession(), session);
  saveAuthSession({ ...session, token: jwt({ exp: 1, auth_version: 1 }) });
  assert.equal(getAuthSession(), null);
});
