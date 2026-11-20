import { getAuthSession } from "./auth";

export const PHONE = /^(09\d{9}|\+639\d{9}|639\d{9})$/;
export const normalizePhone = value => value.replace(/[\s-]/g, "");
export const checkoutStorageKey = () => `foodjs_checkout_${getAuthSession()?.user?.id ?? "anonymous"}`;
export function readCheckout() {
  try { return JSON.parse(sessionStorage.getItem(checkoutStorageKey()) || "null"); } catch { return null; }
}
export function saveCheckout(value) { sessionStorage.setItem(checkoutStorageKey(), JSON.stringify(value)); }
export function clearCheckout() { try { sessionStorage.removeItem(checkoutStorageKey()); } catch { /* Storage may be blocked by browser settings. */ } }
export async function checkoutRequest(url, options = {}) {
  let response;
  try { response = await fetch(url, { ...options, headers: { Authorization: `Bearer ${getAuthSession()?.token || ""}`, ...options.headers } }); }
  catch (failure) { if (failure.name === "AbortError") throw failure; throw new Error("Could not connect to the server. Please retry."); }
  const body = await response.json().catch(() => null);
  if (!response.ok) {
    const failure = new Error(body?.message || (response.status === 401 ? "Please sign in again to continue checkout." : "The request could not be completed. Please retry."));
    failure.status = response.status; throw failure;
  }
  if (body === null) throw new Error("The server response could not be read. Retry the same submission safely.");
  return body;
}
export const quoteRequest = (items, couponCode, signal) => checkoutRequest("/api/orders/quote", {
  method: "POST", headers: { "Content-Type": "application/json" }, signal,
  body: JSON.stringify({ items: items.map(({ menuItemId, quantity, selectedChoice }) => ({ menuItemId, quantity, selectedChoice })), couponCode: couponCode || null })
});
const cleanupRequests = new Map();
const cartSignature = items => JSON.stringify(items.map(item => [String(item.menuItemId), item.selectedChoice || "", Number(item.quantity)]).sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b))));
export function cleanupCheckout(record) {
  if (cleanupRequests.has(record.key)) return cleanupRequests.get(record.key);
  const session = getAuthSession();
  const headers = { Authorization: `Bearer ${session?.token || ""}` };
  if (record.userId != null && record.userId !== session?.user?.id)
    return Promise.resolve("Your order is confirmed. Sign in to the original account to review its bag.");
  const work = (async () => {
    const controller = new AbortController(); const timer = setTimeout(() => controller.abort(), 8000);
    try {
      const cart = await checkoutRequest("/api/cart", { headers, signal: controller.signal });
      if (!Array.isArray(cart)) throw new Error("The bag response was incomplete.");
      if (cart.length && cartSignature(cart) !== cartSignature(record.body.items))
        return "Your order is confirmed. Your bag has changed, so its current items were kept.";
      if (cart.length) await checkoutRequest("/api/cart", { method: "DELETE", headers, signal: controller.signal });
      const current = readCheckout(); if (current?.key === record.key) clearCheckout();
      return "";
    } catch { return "Your order is confirmed, but the purchased items could not be cleared from your bag. Do not place the order again."; }
    finally { clearTimeout(timer); }
  })();
  cleanupRequests.set(record.key, work);
  work.finally(() => cleanupRequests.delete(record.key));
  return work;
}
