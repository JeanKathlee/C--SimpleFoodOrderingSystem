import assert from "node:assert/strict";
import { before, after, test } from "node:test";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { createServer } from "vite";
import { newDrinkOptions, drinkOptionsError, drinkOptionsPayload, includeDrink } from "../src/lib/drinkOptions.js";

test("starter drinks have exactly one included option and independent editor drafts", () => {
  const first = newDrinkOptions();
  assert.equal(drinkOptionsError(first), null);
  first[0].label = "Edited";
  assert.notEqual(newDrinkOptions()[0].label, "Edited");
  assert.equal(first.filter(option => Number(option.addOn) === 0).length, 1);
});

test("selecting an included drink clears the old included price without mutating the draft", () => {
  const original = newDrinkOptions();
  const updated = includeDrink(original, 0);
  assert.equal(updated[0].addOn, "0");
  assert.equal(updated[2].addOn, "");
  assert.equal(original[0].addOn, "50");
  assert.ok(drinkOptionsError(updated));
  updated[2].addOn = "15.50";
  assert.equal(drinkOptionsError(updated), null);
  assert.deepEqual(drinkOptionsPayload(updated)[2], { label: "Go Regular Iced Tea", addOn: 15.5 });
});

test("empty options support standalone products; payloads trim labels and convert prices", () => {
  assert.equal(drinkOptionsError([]), null);
  assert.deepEqual(drinkOptionsPayload([]), []);
  assert.deepEqual(drinkOptionsPayload([{ label: " Juice ", addOn: "0" }]), [{ label: "Juice", addOn: 0 }]);
});

test("reject blank/duplicate labels, invalid prices, too many options and missing/multiple included options", () => {
  const invalid = [
    [{ label: " ", addOn: "0" }], [{ label: "x".repeat(81), addOn: "0" }],
    [{ label: "Tea", addOn: "0" }, { label: " tea ", addOn: "1" }],
    ...["", "-1", "1.001", "1e2", "NaN", "10000.01"].map(addOn => [{ label: "Tea", addOn }]),
    [{ label: "Tea", addOn: "5" }],
    [{ label: "Tea", addOn: "0" }, { label: "Juice", addOn: "0" }],
    Array.from({ length: 11 }, (_, i) => ({ label: `Drink ${i}`, addOn: String(i) })),
  ];
  for (const options of invalid) assert.ok(drinkOptionsError(options), JSON.stringify(options));
  assert.equal(drinkOptionsError([{ label: "Tea", addOn: "0" }, { label: "Shake", addOn: "10000" }]), null);
});

let server;
before(async () => {
  server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false, watch: null }, appType: "custom" });
});
after(async () => { await server?.close(); });

test("admin editor renders saved custom options, included state, accessible labels and validation", async () => {
  const { default: Editor } = await server.ssrLoadModule("/src/components/admin/DrinkOptionsEditor.jsx");
  const html = renderToStaticMarkup(createElement(Editor, {
    options: [{ label: "Calamansi", addOn: "0" }, { label: "Mango Shake", addOn: "25.50" }],
    onChange() {}, error: "Fix this drink",
  }));
  assert.ok(html.includes('value="Calamansi"'));
  assert.ok(html.includes('value="25.50"'));
  assert.equal((html.match(/checked=""/g) || []).length, 1);
  assert.ok(html.includes('aria-label="Include drink 1 in base price"'));
  assert.ok(html.includes('role="alert"'));
  assert.ok(html.includes('name="drinkOptions"'));
  assert.ok(html.includes("selected by default for customers"));
  assert.ok(!html.includes("undefined"));
  const empty = renderToStaticMarkup(createElement(Editor, { options: [], onChange() {} }));
  assert.ok(empty.includes("No drink selection required"));
});

test("admin editor handlers add, rename, reprice, include and remove drink rows", async () => {
  const { default: Editor } = await server.ssrLoadModule("/src/components/admin/DrinkOptionsEditor.jsx");
  let options = [{ label: "Tea", addOn: "0" }, { label: "Juice", addOn: "10" }];
  const nodes = node => !node || typeof node !== "object" ? []
    : Array.isArray(node) ? node.flatMap(nodes) : [node, ...nodes(node.props?.children)];
  const find = predicate => nodes(Editor({ options, onChange(next) { options = next; } })).find(predicate);
  find(node => node.props?.id === "drink-label-1").props.onChange({ target: { value: "Mango Shake" } });
  find(node => node.props?.id === "drink-price-1").props.onChange({ target: { value: "25.50" } });
  assert.equal(options[1].label, "Mango Shake");
  assert.equal(options[1].addOn, "25.50");
  find(node => node.props?.["aria-label"] === "Include drink 2 in base price").props.onChange();
  assert.equal(options[1].addOn, "0");
  assert.equal(options[0].addOn, "");
  find(node => node.props?.["aria-label"] === "Remove drink 1").props.onClick();
  assert.deepEqual(options, [{ label: "Mango Shake", addOn: "0" }]);
  find(node => node.props?.children === "Add drink option").props.onClick();
  assert.deepEqual(options[1], { label: "", addOn: "" });
  assert.ok(drinkOptionsError(options));
});
