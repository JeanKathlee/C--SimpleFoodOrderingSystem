import { test, expect } from "@playwright/test";
import { Buffer } from "node:buffer";

const runtimeErrors = new WeakMap();
test.beforeEach(async ({ page }) => {
  const errors = []; runtimeErrors.set(page, errors);
  page.on("pageerror", error => errors.push(error.message));
  // Deterministic assets only: these are not FoodJS API mocks.
  await page.route("https://images.unsplash.com/**", route => route.fulfill({ contentType: "image/png",
    body: Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aV3kAAAAASUVORK5CYII=", "base64") }));
  await page.route("https://fonts.googleapis.com/**", route => route.fulfill({ contentType: "text/css", body: "" }));
});
test.afterEach(async ({ page }) => { expect(runtimeErrors.get(page), "Uncaught browser JavaScript errors").toEqual([]); });

async function login(page, role = "customer") {
  // Tests may switch synthetic roles; the real login screen redirects existing sessions.
  if (page.url() !== "about:blank") await page.evaluate(() => localStorage.removeItem("foodjs_auth"));
  await page.goto("/login");
  await page.getByPlaceholder("Email", { exact: true }).fill(`${role}@foodjs.demo`);
  await page.getByPlaceholder("Password", { exact: true }).fill(`${role}123`);
  await page.getByRole("button", { name: "Login", exact: true }).click();
  await expect(page).toHaveURL(role === "admin" ? /\/dashboard$/ : /\/menu$/);
}
async function mockSession(page) {
  const token = `test.${Buffer.from(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600, auth_version: 0 })).toString("base64url")}.test`;
  await page.addInitScript(session => localStorage.setItem("foodjs_auth", JSON.stringify(session)),
    { token, user: { id: 999, name: "QA Customer", role: "customer", isEmailVerified: true } });
}
async function apiLogin(request, role = "customer") {
  const response = await request.post("/api/users/login", { data: { email: `${role}@foodjs.demo`, password: `${role}123` } });
  expect(response.status()).toBe(200);
  const session = await response.json();
  return { Authorization: `Bearer ${session.token}` };
}

test.describe("Smoke", () => {
  for (const [path, heading] of [["/", "FoodJS"], ["/login", "LOGIN"], ["/register", "CREATE ACCOUNT"],
    ["/verify-email", "VERIFY YOUR EMAIL"], ["/forgot-password", "RESET PASSWORD"]]) {
    test(`page loads: ${path}`, async ({ page }) => {
      const response = await page.goto(path);
      expect(response.status()).toBe(200);
      await expect(page.getByRole("heading", { name: heading, exact: true })).toBeVisible();
      if (path === "/") {
        await expect(page.getByText("What Customers Say")).toHaveCount(0);
        await expect(page.getByText("Average Rating")).toHaveCount(0);
        await expect(page.getByRole("link", { name: "Start Ordering Now" })).toBeVisible();
      }
    });
  }
  test("anonymous customer/admin routes return to login", async ({ page }) => {
    for (const path of ["/menu", "/cart", "/checkout", "/order-history", "/dashboard"]) {
      await page.goto(path); await expect(page).toHaveURL(/\/login$/);
    }
  });
});

test.describe("Mocked functional UI", () => {
  test("forgot password requests an email rather than navigating with a returned token", async ({ page }) => {
    let submissions = 0;
    await page.route("**/api/auth/forgot-password", route => {
      submissions++;
      return route.fulfill({ json: { message: "Check your inbox and spam folder for a password-reset link." } });
    });
    await page.goto("/forgot-password");
    await page.getByRole("textbox", { name: "Email address", exact: true }).fill("qa@example.test");
    await page.getByRole("button", { name: "Send reset link", exact: true }).click();
    await expect(page.getByRole("status")).toContainText("Check your inbox");
    await expect(page).toHaveURL(/\/forgot-password$/);
    await expect(page.getByRole("button", { name: /Request again in/ })).toBeDisabled();
    expect(submissions).toBe(1);
  });
  test("email reset page scrubs the link and shows success only after submitting matching passwords", async ({ page }) => {
    let submissions = 0;
    await page.route("**/api/auth/reset-password", route => {
      submissions++;
      expect(route.request().postDataJSON()).toEqual({ email: "qa@example.test", token: "C".repeat(64), newPassword: "new-password-123" });
      return route.fulfill({ json: { message: "Password reset successfully. You can now sign in." } });
    });
    await page.goto(`/reset-password#email=qa%40example.test&token=${"C".repeat(64)}`);
    await expect(page).toHaveURL(/\/reset-password$/);
    expect(submissions).toBe(0);
    await page.getByLabel("New password", { exact: true }).fill("new-password-123");
    await page.getByLabel("Confirm password", { exact: true }).fill("different-password");
    await page.getByRole("button", { name: "Reset Password", exact: true }).click();
    await expect(page.getByRole("alert")).toContainText("Passwords do not match");
    expect(submissions).toBe(0);
    await page.getByLabel("Confirm password", { exact: true }).fill("new-password-123");
    await page.getByRole("button", { name: "Reset Password", exact: true }).click();
    await expect(page.getByRole("status")).toContainText("Password changed successfully");
    await page.getByRole("button", { name: "OK", exact: true }).click();
    await expect(page).toHaveURL(/\/login$/);
    expect(submissions).toBe(1);
  });
  test("signup mismatched passwords do not submit; successful signup stays pending", async ({ page }) => {
    let submissions = 0;
    await page.route("**/api/users/register", route => { submissions++;
      return route.fulfill({ json: { verificationRequired: true, emailSent: true, email: "qa@example.test", message: "Check your email." }, status: 201 }); });
    await page.goto("/register");
    await page.getByPlaceholder("First Name", { exact: true }).fill("QA");
    await page.getByPlaceholder("Last Name", { exact: true }).fill("Customer");
    await page.getByPlaceholder("Email", { exact: true }).fill("qa@example.test");
    await page.getByPlaceholder("Password", { exact: true }).fill("test-password-123");
    await page.getByPlaceholder("Confirm Password", { exact: true }).fill("different-password");
    await page.getByRole("button", { name: "Sign-up", exact: true }).click();
    await expect(page.getByText("Passwords do not match.")).toBeVisible();
    expect(submissions).toBe(0);
    await page.getByPlaceholder("Confirm Password", { exact: true }).fill("test-password-123");
    await page.getByRole("button", { name: "Sign-up", exact: true }).click();
    await expect(page).toHaveURL(/\/verify-email$/);
    await expect(page.getByText("Check your email.")).toBeVisible();
    expect(submissions).toBe(1);
    expect(await page.evaluate(() => localStorage.getItem("foodjs_auth"))).toBeNull();
  });
  test("email link verifies automatically once and OK returns to login", async ({ page }) => {
    let submissions = 0;
    await page.route("**/api/auth/verify-email", route => { submissions++;
      return route.fulfill({ json: { message: "Email verified successfully." } }); });
    await page.goto(`/verify-email#email=qa%40example.test&token=${"A".repeat(64)}`);
    await expect(page.getByText("Email verified successfully!")).toBeVisible();
    expect(new URL(page.url()).hash).toBe("");
    await page.getByRole("button", { name: "OK", exact: true }).click();
    await expect(page).toHaveURL(/\/login$/);
    expect(submissions).toBe(1);
  });
  test("expired link shows an error and resend, never verified success", async ({ page }) => {
    await page.route("**/api/auth/verify-email", route => route.fulfill({ status: 400, json: { message: "This verification link is invalid, expired, or already used." } }));
    await page.goto(`/verify-email#email=qa%40example.test&token=${"B".repeat(64)}`);
    await expect(page.getByRole("alert")).toContainText("expired");
    await expect(page.getByRole("button", { name: "Resend verification email" })).toBeVisible();
    await expect(page.getByRole("button", { name: "OK", exact: true })).toHaveCount(0);
  });
  test("mobile bag scrolls all lines while subtotal and Check Cart remain visible", async ({ page }, info) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await mockSession(page);
    const items = Array.from({ length: 23 }, (_, index) => ({ id: index + 1, menuItemId: `M-${index + 1}`,
      name: `QA Food ${index + 1}`, price: 10, quantity: 1, selectedChoice: "Included tea" }));
    await page.route("**/api/menu**", route => route.fulfill({ json: [] }));
    await page.route("**/api/cart", route => route.fulfill({ json: items }));
    await page.goto("/menu");
    await page.getByRole("button", { name: "My Bag (23)" }).click();
    const bag = page.locator("#menu-bag");
    await expect(bag.locator(".menu-bag-item")).toHaveCount(23);
    await expect(bag.getByRole("navigation")).toHaveCount(0);
    expect(await bag.getByRole("region", { name: "Bag items" }).evaluate(el => el.scrollHeight > el.clientHeight)).toBe(true);
    await bag.getByRole("heading", { name: "QA Food 23", exact: true }).scrollIntoViewIfNeeded();
    await expect(bag.getByRole("button", { name: "Check Cart" })).toBeInViewport();
    await expect(bag.locator(".menu-bag-total")).toContainText("230.00");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath("bag-mobile.png"), fullPage: true });
  });
});

test.describe("Real C# API integration", () => {
  test("standalone beverages have no drink choices; admin can explicitly enable a custom drink bundle", async ({ page, request }, info) => {
    await login(page, "admin");
    await page.getByRole("button", { name: "Menu", exact: true }).click();
    await page.getByRole("button", { name: "Edit Classic Iced Cola", exact: true }).click();
    let dialog = page.getByRole("dialog", { name: "Edit Classic Iced Cola", exact: true });
    await expect(dialog.getByRole("checkbox", { name: "Includes a drink", exact: true })).not.toBeChecked();
    await expect(dialog.locator("#menu-drinkOptions")).toHaveCount(0);
    await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
    const name = `QA Beverage ${Date.now()}`;
    await page.getByRole("button", { name: "Add menu item", exact: true }).click();
    dialog = page.getByRole("dialog", { name: "Add a menu item" });
    await expect(dialog.getByRole("checkbox", { name: "Includes a drink", exact: true })).not.toBeChecked();
    await dialog.getByLabel("Item name *", { exact: true }).fill(name);
    await dialog.getByLabel("Category *", { exact: true }).selectOption({ label: "Drinks" });
    await dialog.getByLabel("Price (PHP) *", { exact: true }).fill("59");
    await dialog.getByLabel("Preparation time (minutes) *", { exact: true }).fill("2");
    await dialog.getByRole("button", { name: "Add item", exact: true }).click();
    await expect(dialog).not.toBeVisible();
    const response = await request.get(`/api/menu?search=${encodeURIComponent(name)}`);
    const [saved] = await response.json();
    expect(saved.drinkOptions).toEqual([]);
    await login(page);
    await page.goto(`/product/${saved.id}`);
    await expect(page.getByRole("heading", { name, exact: true })).toBeVisible();
    await expect(page.getByRole("radio")).toHaveCount(0);
    await expect(page.getByRole("heading", { name: "Drink Choice", exact: true })).toHaveCount(0);
    await page.screenshot({ path: info.outputPath("standalone-beverage.png") });
    await login(page, "admin");
    await page.getByRole("button", { name: "Menu", exact: true }).click();
    await page.getByLabel("Search menu", { exact: true }).fill(name);
    await page.getByRole("button", { name: `Edit ${name}`, exact: true }).click();
    dialog = page.getByRole("dialog", { name: `Edit ${name}`, exact: true });
    await dialog.getByRole("checkbox", { name: "Includes a drink", exact: true }).check();
    await dialog.locator("#drink-label-2").fill("Included lemonade");
    await dialog.getByRole("button", { name: "Save changes", exact: true }).click();
    await expect(dialog).not.toBeVisible();
    await login(page);
    await page.goto(`/product/${saved.id}`);
    await expect(page.getByRole("radio", { name: /Included lemonade/ })).toBeChecked();
    await page.getByRole("radio", { name: /Go Large Iced Tea/ }).check();
    await expect(page.getByRole("button", { name: /Add To Cart/ })).toContainText("109.00");
    const headers = await apiLogin(request, "admin");
    expect((await request.delete(`/api/menu/${saved.id}`, { headers })).status()).toBe(200);
  });
  test("admin Add/Edit Menu popup validates and saves; deleted items disappear from public results", async ({ page, request }, info) => {
    await login(page, "admin");
    await page.getByRole("button", { name: "Menu", exact: true }).click();
    await page.getByRole("button", { name: "Add menu item", exact: true }).click();
    let dialog = page.getByRole("dialog", { name: "Add a menu item" });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByLabel("Item name *", { exact: true })).toBeFocused();
    await dialog.getByRole("button", { name: "Add item", exact: true }).click();
    await expect(dialog.getByText("Enter an item name.")).toBeVisible();
    await dialog.locator('input[type="file"]').setInputFiles({ name: "too-large.png", mimeType: "image/png", buffer: Buffer.alloc(5 * 1024 * 1024 + 1) });
    await expect(dialog.locator("#menu-image")).toHaveAttribute("aria-invalid", "true");
    await page.keyboard.press("Escape");
    await expect(dialog).not.toBeVisible();
    const name = `QA Browser Burger ${Date.now()}`;
    await page.getByRole("button", { name: "Add menu item", exact: true }).click();
    dialog = page.getByRole("dialog", { name: "Add a menu item" });
    await dialog.getByLabel("Item name *", { exact: true }).fill(name);
    await dialog.getByLabel("Price (PHP) *", { exact: true }).fill("125");
    await dialog.getByLabel("Preparation time (minutes) *", { exact: true }).fill("10");
    await page.screenshot({ path: info.outputPath("menu-popup-desktop.png") });
    await dialog.getByRole("button", { name: "Add item", exact: true }).click();
    await expect(dialog).not.toBeVisible();
    const search = page.getByLabel("Search menu", { exact: true });
    await search.fill(name);
    await page.getByRole("button", { name: `Edit ${name}`, exact: true }).click();
    dialog = page.getByRole("dialog", { name: `Edit ${name}`, exact: true });
    await dialog.getByLabel("Price (PHP) *", { exact: true }).fill("150");
    await dialog.getByRole("button", { name: "Save changes", exact: true }).click();
    await expect(dialog).not.toBeVisible();
    await expect(page.getByRole("list", { name: "Menu items" })).toContainText("150.00");
    await page.getByRole("button", { name: `Delete ${name}`, exact: true }).click();
    await page.getByRole("dialog", { name: "Delete menu item", exact: true }).getByRole("button", { name: "Delete Item", exact: true }).click();
    await expect(page.getByRole("heading", { name, exact: true })).toHaveCount(0);
    const response = await request.get(`/api/menu?search=${encodeURIComponent(name)}`);
    expect(await response.json()).toEqual([]);
  });
  test("customer gets the included drink by default, updates cart, places cash order and finds it in history", async ({ page, request }) => {
    await login(page);
    await page.getByRole("button", { name: /View .* details/ }).first().click();
    await expect(page.getByRole("heading", { name: "Product Details", exact: true })).toBeVisible();
    await expect(page.getByRole("radio", { name: /Go Regular Iced Tea/ })).toBeChecked();
    await page.getByRole("button", { name: /Add To Cart/ }).click();
    await expect(page).toHaveURL(/\/menu$/);
    await page.getByRole("button", { name: /^My Bag/ }).click();
    await page.getByRole("button", { name: /Increase .* quantity/ }).first().click();
    await page.getByRole("button", { name: "Check Cart", exact: true }).click();
    await expect(page.getByRole("heading", { name: "My Cart", exact: true })).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Cart lines pagination" })).toHaveCount(0);
    await page.getByRole("button", { name: "Checkout", exact: true }).click();
    await page.locator("#checkout-firstName").fill("QA");
    await page.locator("#checkout-lastName").fill("Customer");
    await page.locator("#checkout-mobileNumber").fill("09123456789");
    await page.locator("#checkout-address").fill("QA Test Building, Cebu City (not a real delivery)");
    await page.getByRole("button", { name: "Review order", exact: true }).click();
    await page.getByRole("dialog", { name: "Review and place your order" }).getByRole("button", { name: "Place order", exact: true }).click();
    await expect(page).toHaveURL(/\/order-confirmation\/[^/]+$/);
    await expect(page.getByRole("heading", { name: "Saved order details", exact: true })).toBeVisible();
    const orderId = new URL(page.url()).pathname.split("/").at(-1);
    await page.getByRole("link", { name: "View order history", exact: true }).click();
    await expect(page.getByRole("link", { name: `View order ${orderId}`, exact: true })).toBeVisible();
    await page.getByRole("searchbox").fill("not-a-real-order");
    await expect(page.getByRole("heading", { name: "No matching orders", exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "Browse menu", exact: true })).toHaveCount(0);
    await expect(page.getByRole("navigation", { name: "Orders pagination" })).toHaveCount(0);
    await page.locator(".order-empty").getByRole("button", { name: "Clear filters", exact: true }).click();
    await expect(page.getByRole("link", { name: `View order ${orderId}`, exact: true })).toBeVisible();
    const headers = await apiLogin(request, "admin");
    const updated = await request.patch(`/api/orders/${orderId}/status`, { headers, data: { status: "Preparing" } });
    expect(updated.status()).toBe(200);
    await page.getByRole("link", { name: `View order ${orderId}`, exact: true }).click();
    await expect(page.locator('[aria-label="Order progress: Preparing"]')).toBeVisible();
    await expect(page.locator('[aria-current="step"]')).toHaveText("✓Preparing");
  });
  test("wrong password and missing SMTP produce clear errors without signing in", async ({ page, request }) => {
    await page.goto("/login");
    await page.getByPlaceholder("Email", { exact: true }).fill("customer@foodjs.demo");
    await page.getByPlaceholder("Password", { exact: true }).fill("wrong-password");
    await page.getByRole("button", { name: "Login", exact: true }).click();
    await expect(page.getByText("Invalid email or password.", { exact: true })).toBeVisible();
    const signup = await request.post("/api/users/register", { data: { firstName: "QA", lastName: "Pending", email: "qa-no-smtp@example.test", password: "password-123" } });
    expect(signup.status()).toBe(503);
    expect((await signup.json()).message).toContain("Email delivery is not configured");
  });
});

test.describe("Security and basic performance gates", () => {
  test("anonymous requests cannot read account/cart/orders/admin data", async ({ request }) => {
    for (const route of ["/api/users/me", "/api/users", "/api/cart", "/api/orders", "/api/orders/admin/paged"]) {
      expect((await request.get(route)).status(), route).toBe(401);
    }
  });
  test("a customer cannot create menu records or list admin orders", async ({ request }) => {
    const headers = await apiLogin(request);
    expect((await request.post("/api/menu", { headers, data: {} })).status()).toBe(403);
    expect((await request.get("/api/orders/admin/paged", { headers })).status()).toBe(403);
    expect((await request.get("/api/users", { headers })).status()).toBe(403);
  });
  test("SQL-like search input returns no unrelated orders and profiles are not cached", async ({ request }) => {
    const headers = await apiLogin(request);
    const response = await request.get(`/api/orders/paged?search=${encodeURIComponent("' OR 1=1 --")}`, { headers });
    expect(response.status()).toBe(200);
    expect((await response.json()).totalCount).toBe(0);
    const profile = await request.get("/api/users/me", { headers });
    expect(profile.headers()["cache-control"]).toContain("no-store");
    expect(await profile.text()).not.toMatch(/passwordHash|tokenHash|clientSecret/i);
  });
  test("local menu API timing sample (not a production load test)", async ({ request }, info) => {
    const durations = [];
    for (let i = 0; i < 10; i++) {
      const started = performance.now();
      expect((await request.get("/api/menu/paged?page=1&pageSize=10")).status()).toBe(200);
      durations.push(performance.now() - started);
    }
    durations.sort((a, b) => a - b);
    await info.attach("local-api-timing.json", { contentType: "application/json", body: JSON.stringify({
      samples: durations.length, medianMs: durations[5], maxMs: durations.at(-1), scope: "loopback QA API; not production capacity testing",
    }) });
  });
});
