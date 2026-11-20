import { getAuthSession } from "./auth";

export const ORDER_STATUSES = ["Pending", "Preparing", "Ready", "Delivered", "Cancelled"];
export const nextStatuses = status => ({ Pending: ["Preparing", "Cancelled"], Preparing: ["Ready", "Cancelled"], Ready: ["Delivered"] }[status] || []);
export const orderHeaders = () => ({ Authorization: `Bearer ${getAuthSession()?.token || ""}` });
export const orderDate = value => new Date(value).toLocaleString("en-PH", { timeZone: "Asia/Manila", year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });
export const orderMoney = value => new Intl.NumberFormat("en-PH", { style: "currency", currency: "PHP", minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value || 0);
export async function orderRequest(url, options = {}) {
  const response = await fetch(url, { ...options, headers: { ...orderHeaders(), ...options.headers } });
  const body = await response.json().catch(() => null);
  if (!response.ok) throw new Error(body?.message || (response.status === 401 ? "Your session has expired. Please sign in again." : response.status === 403 ? "Administrator access is required." : response.status === 404 ? "This order is no longer available." : "Could not complete the request. Please retry."));
  if (body === null) throw new Error("The server returned an incomplete response. Please retry.");
  return body;
}
