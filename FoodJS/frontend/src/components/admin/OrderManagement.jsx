import { useEffect, useRef, useState } from "react";
import useOrderPage from "../../hooks/useOrderPage";
import { nextStatuses, orderDate, orderMoney, orderRequest } from "../../lib/orders";
import OrderFilters from "../OrderFilters";
import Pagination from "../Pagination";
import ConfirmModal from "./ConfirmModal";
import OrderDetailsDialog from "./OrderDetailsDialog";
import "../../orders.css";

export default function OrderManagement({ onBusyChange }) {
  const page = useOrderPage("/api/orders/admin/paged");
  const [choices, setChoices] = useState({});
  const [busy, setBusy] = useState([]);
  const locks = useRef(new Set());
  const [actionError, setActionError] = useState("");
  const [notice, setNotice] = useState("");
  const [confirm, setConfirm] = useState(null);
  const [detailsId, setDetailsId] = useState(null);
  useEffect(() => { onBusyChange?.(busy.length > 0); }, [busy.length, onBusyChange]);
  useEffect(() => () => onBusyChange?.(false), [onBusyChange]);
  const perform = async (type, order, status) => {
    if (locks.current.has(order.id)) return;
    locks.current.add(order.id); setBusy(current => [...current, order.id]); setActionError(""); setNotice("");
    try {
      await orderRequest(`/api/orders/${encodeURIComponent(order.id)}${type === "status" ? "/status" : ""}`, {
        method: type === "status" ? "PATCH" : "DELETE",
        ...(type === "status" ? { headers: { "Content-Type": "application/json" }, body: JSON.stringify({ status }) } : {})
      });
      setChoices(current => ({ ...current, [order.id]: "" })); setConfirm(null);
      setNotice(type === "delete" ? `${order.id} was deleted.` : `${order.id} is now ${status}.`); page.refresh();
    } finally { locks.current.delete(order.id); setBusy(current => current.filter(id => id !== order.id)); }
  };
  const updateStatus = order => {
    const status = choices[order.id];
    if (!nextStatuses(order.status).includes(status)) return;
    if (status === "Cancelled") setConfirm({ type: "status", order, status });
    else perform("status", order, status).catch(failure => setActionError(failure.message));
  };
  const { data, filters, error, loading, hasLoaded } = page;
  const filtered = filters.search || filters.status !== "All" || filters.fromDate || filters.toDate;
  return <section className="admin-panel orders-management" aria-labelledby="orders-title">
    <div className="orders-heading"><div><h2 id="orders-title">Orders</h2><p>Select an order ID to view its details.</p></div><button type="button" className="admin-btn admin-btn-light" onClick={page.refresh} disabled={loading || busy.length > 0}>Refresh orders</button></div>
    <OrderFilters filters={filters} onChange={page.setFilter} onClear={page.clearFilters} />
    {error && <div className="order-notice" role="alert"><p>{error}{hasLoaded ? " Previously loaded orders remain visible." : ""}</p><button type="button" onClick={page.refresh} disabled={loading}>Retry orders</button></div>}
    {actionError && <p className="order-action-error" role="alert">{actionError}</p>}
    {notice && <p className="order-success" role="status">{notice}</p>}
    <p className="order-list-status" role="status">{loading ? "Loading orders…" : error ? "Orders are not up to date." : `${data.totalCount} ${data.totalCount === 1 ? "order" : "orders"}${filtered ? " found" : " in total"}`}</p>
    <ul className="order-card-list" aria-label="Orders" aria-busy={loading}>
      {data.items.map(order => {
        const options = nextStatuses(order.status); const pending = busy.includes(order.id); const disabled = pending || loading || !!error;
        const selected = options.includes(choices[order.id]) ? choices[order.id] : "";
        return <li key={order.id} className="order-admin-card">
          <div className="order-card-main"><h3><button type="button" className="order-id-button" onClick={() => setDetailsId(order.id)} aria-label={`View details for ${order.id}`}>{order.id}</button></h3><p>{order.customerName}</p><time dateTime={order.createdAt}>{orderDate(order.createdAt)}</time></div>
          <div className="order-card-summary"><span className={`admin-status ${order.status.toLowerCase()}`}>{order.status}</span><strong>{orderMoney(order.total)}</strong><span>{order.itemCount} {order.itemCount === 1 ? "item" : "items"}</span></div>
          <div className="order-card-actions">
            {options.length ? <div className="order-status-controls"><label className="order-sr-only" htmlFor={`status-${order.id}`}>Next status for {order.id}</label><select id={`status-${order.id}`} value={selected} disabled={disabled} onChange={e => setChoices(current => ({ ...current, [order.id]: e.target.value }))}><option value="">Next status</option>{options.map(status => <option key={status}>{status}</option>)}</select><button type="button" className="admin-btn admin-btn-primary" disabled={disabled || !selected} onClick={() => updateStatus(order)} aria-label={`Update status for ${order.id}`}>{pending ? "Saving…" : "Update"}</button></div> : <span className="order-terminal">{order.status === "Cancelled" ? "Order cancelled" : "Order completed"}</span>}
            <button type="button" className="admin-btn admin-btn-danger" disabled={disabled} onClick={() => setConfirm({ type: "delete", order })} aria-label={`Delete ${order.id}`}>Delete</button>
          </div>
        </li>;
      })}
    </ul>
    {!loading && !error && !data.items.length && <div className="order-empty"><h3>{filtered ? "No matching orders" : "No orders yet"}</h3><p>{filtered ? "Try another search or clear your filters." : "New customer orders will appear here."}</p></div>}
    <Pagination {...data} label="Orders" disabled={loading || !!error || busy.length > 0} onPageChange={value => page.setFilter("page", value)} onPageSizeChange={value => page.setFilter("pageSize", value)} />
    {detailsId && <OrderDetailsDialog orderId={detailsId} onClose={() => setDetailsId(null)} />}
    <ConfirmModal open={!!confirm} title={confirm?.type === "delete" ? "Delete order?" : "Cancel order?"}
      message={confirm ? `${confirm.order.id} for ${confirm.order.customerName}: ${confirm.type === "delete" ? "permanently remove this order and its saved records?" : "mark this order as Cancelled? This cannot be undone."}` : ""}
      confirmLabel={confirm?.type === "delete" ? "Delete order" : "Cancel order"} onCancel={() => setConfirm(null)}
      onConfirm={() => perform(confirm.type, confirm.order, confirm.status)} />
  </section>;
}
