// Same rules as the C# API (Common/ImageRules.cs). No SVG: it can carry scripts.
export const IMAGE_ACCEPT = "image/png,image/jpeg,image/webp,image/gif";
const MAX_IMAGE_BYTES = 2 * 1024 * 1024;
export const FOOD_IMAGE_MAX_BYTES = 5 * 1024 * 1024;

// returns an error message, or null when the file is fine
export function checkImageFile(file, maxBytes = MAX_IMAGE_BYTES) {
  if (!IMAGE_ACCEPT.split(",").includes(file.type)) {
    return "Please choose a PNG, JPG, WebP or GIF image.";
  }
  if (file.size > maxBytes) {
    return `Image must be ${maxBytes / (1024 * 1024)} MB or smaller.`;
  }
  return null;
}
