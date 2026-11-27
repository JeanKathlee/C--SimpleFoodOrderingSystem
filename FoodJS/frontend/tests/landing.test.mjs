import assert from "node:assert/strict";
import { test } from "node:test";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { createServer } from "vite";

test("landing omits testimonials and placeholder rating/statistics while keeping the ordering action", async () => {
  const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false, watch: null }, appType: "custom" });
  try {
    const { default: Landing } = await server.ssrLoadModule("/src/pages/Landing.jsx");
    const html = renderToStaticMarkup(createElement(MemoryRouter, null, createElement(Landing)));
    assert.doesNotMatch(html, /What Customers Say|Real feedback from real FoodJS lovers|testimonial|Maria Santos|Juan Dela Cruz|Rosa Miguel/);
    assert.doesNotMatch(html, /landing-trust-section|trust-card|50K\+|4\.8★|Happy Customers|Average Rating|Avg Delivery Time|Fresh Guarantee/);
    assert.match(html, /Ready to order\?/);
    assert.match(html, /Start Ordering Now/);
    assert.match(html, /How It Works/);
    assert.match(html, /Featured Menu/);
  } finally { await server.close(); }
});
