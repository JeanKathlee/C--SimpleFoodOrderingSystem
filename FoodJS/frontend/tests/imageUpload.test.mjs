import assert from "node:assert/strict";
import { test } from "node:test";
import { checkImageFile, FOOD_IMAGE_MAX_BYTES } from "../src/lib/imageUpload.js";

test("food image limit is exactly 5 MiB and accepts the boundary", () => {
  assert.equal(FOOD_IMAGE_MAX_BYTES, 5 * 1024 * 1024);
  for (const size of [0, 2 * 1024 * 1024 + 1, FOOD_IMAGE_MAX_BYTES]) {
    assert.equal(checkImageFile({ type: "image/png", size }, FOOD_IMAGE_MAX_BYTES), null);
  }
});

test("food image over the limit is rejected with the correct message", () => {
  assert.equal(checkImageFile({ type: "image/jpeg", size: FOOD_IMAGE_MAX_BYTES + 1 }, FOOD_IMAGE_MAX_BYTES), "Image must be 5 MB or smaller.");
});

test("non-image and SVG uploads are still rejected", () => {
  for (const type of ["application/pdf", "image/svg+xml", ""]) {
    assert.match(checkImageFile({ type, size: 1 }, FOOD_IMAGE_MAX_BYTES), /choose a PNG/);
  }
});

test("profile image limit remains 2 MiB", () => {
  assert.equal(checkImageFile({ type: "image/png", size: 2 * 1024 * 1024 }), null);
  assert.equal(checkImageFile({ type: "image/png", size: 2 * 1024 * 1024 + 1 }), "Image must be 2 MB or smaller.");
});
