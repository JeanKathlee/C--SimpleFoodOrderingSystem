import assert from "node:assert/strict";
import { before, after, test } from "node:test";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { createServer } from "vite";

let server;
before(async () => {
  server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false, watch: null }, appType: "custom" });
});
after(async () => { await server?.close(); });

const adminComponents = ["AdminSidebar", "AdminTopbar", "AnalyticsCards", "RevenueChart", "PopularItems",
  "CategoryManagement", "CouponManagement", "CustomersPanel", "MenuManagement", "OrderManagement", "OrdersPanel"];

test("every literal CSS Module lookup in the Dashboard family resolves", async () => {
  const urls = ["/src/pages/Dashboard.jsx", "/src/components/ui/Modal.jsx", "/src/components/ui/Button.jsx",
    ...adminComponents.map(name => `/src/components/admin/${name}.jsx`)];
  for (const url of urls) {
    const source = await readFile(path.join(server.config.root, url.slice(1)), "utf8");
    for (const imported of source.matchAll(/import (\w+) from "([^"\n]+\.module\.css)"/g)) {
      const [, binding, specifier] = imported;
      const cssUrl = new URL(specifier, `http://local${url}`).pathname;
      const { default: classes } = await server.ssrLoadModule(cssUrl);
      const lookups = new RegExp(`\\b${binding}(?:\\["([^"\\n]+)"\\]|\\.(\\w+))`, "g");
      for (const lookup of source.matchAll(lookups)) {
        const key = lookup[1] || lookup[2];
        assert.ok(classes[key], `${url}: missing ${binding}.${key}`);
        assert.notEqual(classes[key], key, `${cssUrl}: ${key} must be scoped`);
      }
    }
    assert.doesNotMatch(source, /className="admin-/, `${url} retains an unscoped admin class`);
  }
});

test("admin and customer menu toolbar classes cannot collide", async () => {
  const { default: menu } = await server.ssrLoadModule("/src/components/admin/MenuManagement.module.css");
  assert.ok(menu["menu-toolbar"]);
  assert.notEqual(menu["menu-toolbar"], "menu-toolbar");
  const css = await readFile("src/components/admin/MenuManagement.module.css", "utf8");
  assert.ok(css.includes("max-width: 760px"));
  assert.ok(css.includes("prefers-reduced-motion"));
  assert.doesNotMatch(css, /:global/);
});

test("sidebar active state uses the same scoped class as its stylesheet", async () => {
  const { default: Sidebar } = await server.ssrLoadModule("/src/components/admin/AdminSidebar.jsx");
  const { default: styles } = await server.ssrLoadModule("/src/pages/Dashboard.module.css");
  const html = renderToStaticMarkup(createElement(Sidebar, { activeSection: "categories", onSectionChange() {} }));
  assert.ok(html.includes(styles["admin-nav-item"]));
  assert.ok(html.includes(styles.active));
  assert.ok(html.includes('aria-current="page"'));
  assert.ok(!html.includes("undefined"));
});

test("all supported order statuses have a scoped badge style", async () => {
  const { default: styles } = await server.ssrLoadModule("/src/pages/Dashboard.module.css");
  for (const status of ["pending", "preparing", "ready", "delivered", "cancelled", "completed"]) {
    assert.ok(styles[status], `Missing status: ${status}`);
  }
});

test("shared Button preserves native type, disabled, ARIA, click handlers, and refs", async () => {
  const { default: Button } = await server.ssrLoadModule("/src/components/ui/Button.jsx");
  const { default: styles } = await server.ssrLoadModule("/src/components/ui/Button.module.css");
  const onClick = () => {};
  const ref = { current: null };
  const element = Button({ variant: "primary", type: "submit", disabled: true, "aria-label": "Save category", onClick, ref });
  assert.equal(element.type, "button");
  assert.equal(element.props.type, "submit");
  assert.equal(element.props.onClick, onClick);
  assert.equal(element.props.ref, ref);
  assert.ok(element.props.className.includes(styles.primary));
  const html = renderToStaticMarkup(element);
  assert.match(html, /disabled=""/);
  assert.match(html, /aria-label="Save category"/);
  assert.match(html, /data-variant="primary"/);
  assert.equal(Button({}).props.type, "button");
});

test("shared Modal is self-styled outside the Dashboard and supports wide order review", async () => {
  const { default: Modal } = await server.ssrLoadModule("/src/components/ui/Modal.jsx");
  const { default: styles } = await server.ssrLoadModule("/src/components/ui/Modal.module.css");
  const props = { title: "Review order", message: "Check your items.", confirmLabel: "Place order", onCancel() {}, onConfirm() {} };
  assert.equal(renderToStaticMarkup(createElement(Modal, { ...props, open: false })), "");
  const html = renderToStaticMarkup(createElement(Modal, { ...props, open: true, size: "wide", confirmVariant: "primary" }));
  assert.ok(html.includes(styles.dialog));
  assert.ok(html.includes(styles.wide));
  assert.ok(html.includes('role="dialog"'));
  assert.ok(html.includes('aria-modal="true"'));
  assert.ok(html.includes("Place order"));
  assert.ok(!html.includes("admin-modal"));
  assert.ok(!html.includes("undefined"));
});

test("customer profile dialog composes the shared modal without global classes", async () => {
  const { default: Customers } = await server.ssrLoadModule("/src/components/admin/CustomersPanel.jsx");
  const { default: modal } = await server.ssrLoadModule("/src/components/ui/Modal.module.css");
  const html = renderToStaticMarkup(createElement(Customers, {
    customers: [], orders: [], selectedCustomer: { id: 1, name: "Smoke Customer" }, onClose() {},
  }));
  assert.ok(html.includes(modal.dialog));
  assert.ok(html.includes(modal.backdrop));
  assert.ok(html.includes("Customer profile"));
  assert.ok(!html.includes("undefined"));
});

test("design tokens are global but contain no component layout rules", async () => {
  const tokens = await readFile("src/styles/tokens.css", "utf8");
  assert.ok(tokens.includes(":root"));
  assert.ok(tokens.includes("--admin-primary:"));
  assert.doesNotMatch(tokens, /\b(display|padding|margin|width|height|position)\s*:/);
  const [orders, checkout] = await Promise.all([readFile("src/orders.css", "utf8"), readFile("src/checkout.css", "utf8")]);
  assert.doesNotMatch(orders + checkout, /\.admin-(?:btn|panel|modal)\b/);
});
