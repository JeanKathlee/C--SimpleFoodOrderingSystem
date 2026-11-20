export function cartItemKey(item) {
  return JSON.stringify([item.menuItemId, item.selectedChoice || null]);
}
