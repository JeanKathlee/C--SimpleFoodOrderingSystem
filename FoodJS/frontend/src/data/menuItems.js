// middleman between frontend components and the backend API.
export { fetchMenuFromBackend, fetchMenuItem, getCategoriesFromMenu, getMenuItemById, formatPrice, enrichMenuWithImages } from "../lib/menuApi";

export const tabs = ["All", "Burgers", "Sides", "Drinks", "Desserts"];

export const menuItems = [];

export function getMenuItemByName() {
  console.warn(
    "getMenuItemByName is deprecated. Use getMenuItemById with backend ID instead."
  );
  return null;
}
