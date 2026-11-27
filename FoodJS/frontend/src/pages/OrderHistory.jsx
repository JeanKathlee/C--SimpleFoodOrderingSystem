import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import useOrderPage from "../hooks/useOrderPage";
import OrderFilters from "../components/OrderFilters";
import Pagination from "../components/Pagination";
import usePagination from "../hooks/usePagination";
import { orderDate, orderMoney, orderRequest } from "../lib/orders";
import "../orders.css";

export default function OrderHistory() {
  const page = useOrderPage("/api/orders/paged", 10000);
  const { data, filters, loading, error } = page;
  const [notifications, setNotifications] = useState([]);
  const [noticesLoading, setNoticesLoading] = useState(true);
  const [noticesError, setNoticesError] = useState("");
  const noticePager = usePagination(notifications);
  useEffect(() => {
    const controller = new AbortController(); const signal = controller.signal;
    orderRequest("/api/notifications", { signal }).then(body => { if (!signal.aborted) setNotifications(Array.isArray(body) ? body : []); })
      .catch(() => { if (!signal.aborted) setNoticesError("Status notifications are currently unavailable."); })
      .finally(() => { if (!signal.aborted) setNoticesLoading(false); });
    return () => controller.abort();
  }, []);
  const filtered = filters.search || filters.status !== "All" || filters.fromDate || filters.toDate;
  return <div className="client-page history-page"><div className="client-shell">
    <header className="page-topbar"><div><h1>Order History</h1><p>Your orders, newest first.</p></div><Link className="client-btn ghost" to="/menu">Back to menu</Link></header>
    <div className="history-layout">
      <section className="panel-card filter-card"><h2>Search orders</h2><OrderFilters filters={filters} onChange={page.setFilter} onClear={page.clearFilters} /></section>
      <aside className="panel-card history-notifications"><h3>Recent status notifications</h3>
        {noticesLoading ? <p>Loading notifications…</p> : noticesError ? <p role="status">{noticesError}</p> : !notifications.length ? <p>No status updates yet.</p> : <><div className="notification-list">{noticePager.pageItems.map(notice => <div key={notice.id} className={`notification-item ${notice.read ? "" : "unread"}`}><div><strong>{notice.title}</strong><p>{notice.message}</p></div><span>{orderDate(notice.createdAt)}</span></div>)}</div><Pagination {...noticePager} label="Notifications" /></>}
      </aside>
    </div>
    {error && <div className="order-notice" role="alert"><p>{error}{page.hasLoaded ? " Previously loaded orders remain visible." : ""}</p><button type="button" onClick={page.refresh} disabled={loading}>Retry orders</button></div>}
    <p className="order-list-status" role="status">{loading ? "Loading orders…" : error ? "Order history is not up to date." : `${data.totalCount} ${data.totalCount === 1 ? "order" : "orders"} found`}</p>
    {!loading && !error && !data.items.length && <section className="panel-card order-empty"><h2>{filtered ? "No matching orders" : "No orders yet"}</h2><p>{filtered ? "Try another search or clear your filters." : "Start an order from the menu."}</p><Link className="client-btn primary" to="/menu">Browse menu</Link></section>}
    <section className="history-list" aria-label="Order history" aria-busy={loading}>
      {data.items.map(order => <Link key={order.id} className="panel-card history-card order-history-link" to={`/order-tracking/${order.id}`} aria-label={`View order ${order.id}`}>
        <div><h3>Order {order.id}</h3><time dateTime={order.createdAt}>{orderDate(order.createdAt)}</time></div>
        <div className="history-meta"><strong>{orderMoney(order.total)}</strong><span className={`status-pill ${order.status.toLowerCase()}`}>{order.status}</span><p>{order.itemCount} {order.itemCount === 1 ? "item" : "items"}</p></div>
      </Link>)}
    </section>
    <Pagination {...data} label="Orders" disabled={loading || !!error} onPageChange={value => page.setFilter("page", value)} onPageSizeChange={value => page.setFilter("pageSize", value)} />
  </div></div>;
}
