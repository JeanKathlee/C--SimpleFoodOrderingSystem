import { useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { orderDate, orderRequest } from "../lib/orders";
import OrderDetailsContent from "../components/OrderDetailsContent";
import ConfirmModal from "../components/ui/Modal";
import Pagination from "../components/Pagination";
import usePagination from "../hooks/usePagination";
import "../orders.css";

export default function OrderTracking() {
  const { orderId } = useParams();
  const [order, setOrder] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  const [orderNotifications, setOrderNotifications] = useState([]);
  const noticePager = usePagination(orderNotifications, 10, orderId);
  const [noticesError, setNoticesError] = useState("");
  const [noticesLoading, setNoticesLoading] = useState(true);
  const [markingRead, setMarkingRead] = useState(false);
  const [cancelConfirmOpen, setCancelConfirmOpen] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [notice, setNotice] = useState("");
  const cancelLock = useRef(false);
  const readController = useRef(null);
  const displayedId = useRef(orderId);
  useEffect(() => {
    let disposed = false;
    if (displayedId.current !== orderId) { setOrder(null); setLoading(true); setNotice(""); setCancelConfirmOpen(false); displayedId.current = orderId; }
    const load = async () => {
      if (cancelLock.current) return;
      setLoading(true);
      readController.current?.abort(); const controller = new AbortController(); readController.current = controller;
      try {
        const body = await orderRequest(`/api/orders/${encodeURIComponent(orderId)}`, { signal: controller.signal });
        if (!body.id || !Array.isArray(body.items)) throw new Error("The order response was incomplete. Please retry.");
        if (!disposed && !controller.signal.aborted) { setOrder(body); setError(""); }
      } catch (failure) { if (!disposed && !controller.signal.aborted) setError(failure.message); }
      finally { if (!disposed && !controller.signal.aborted) setLoading(false); }
    };
    load(); const timer = setInterval(load, 5000);
    return () => { disposed = true; readController.current?.abort(); clearInterval(timer); };
  }, [orderId, revision]);
  useEffect(() => {
    let controller; let disposed = false;
    setOrderNotifications([]); setNoticesLoading(true);
    const load = async () => {
      controller?.abort(); controller = new AbortController(); const signal = controller.signal;
      try {
        const body = await orderRequest("/api/notifications", { signal });
        if (!disposed && !signal.aborted) { setOrderNotifications(Array.isArray(body) ? body.filter(item => item.orderId === orderId) : []); setNoticesError(""); }
      } catch (_failure) { if (!disposed && !signal.aborted) setNoticesError("Status notifications are currently unavailable."); }
      finally { if (!disposed && !signal.aborted) setNoticesLoading(false); }
    };
    load(); const timer = setInterval(load, 10000);
    return () => { disposed = true; controller?.abort(); clearInterval(timer); };
  }, [orderId]);
  const canCancel = order && ["Pending", "Preparing"].includes(order.status);
  const confirmCancel = async () => {
    if (cancelLock.current) return;
    if (!canCancel || error) throw new Error("This order cannot be cancelled from its current state. Refresh the order first.");
    cancelLock.current = true; setCancelling(true); readController.current?.abort(); setNotice("");
    try {
      const body = await orderRequest(`/api/orders/${encodeURIComponent(orderId)}/cancel`, { method: "PATCH" });
      setOrder(body); setCancelConfirmOpen(false); setNotice("Your order was cancelled."); setError(""); setLoading(false);
    } catch (failure) { setRevision(value => value + 1); throw failure; }
    finally { cancelLock.current = false; setCancelling(false); }
  };
  const unread = orderNotifications.filter(item => !item.read);
  const markRead = async () => {
    if (markingRead) return;
    setMarkingRead(true);
    try {
      await Promise.all(unread.map(item => orderRequest(`/api/notifications/${item.id}/read`, { method: "PATCH" })));
      setOrderNotifications(current => current.map(item => ({ ...item, read: true }))); setNoticesError("");
    } catch (_failure) { setNoticesError("Could not mark notifications as read. Please retry."); }
    finally { setMarkingRead(false); }
  };
  const steps = ["Pending", "Preparing", "Ready", "Delivered"];
  const currentStep = steps.indexOf(order?.status);
  return <div className="client-page tracking-page"><div className="client-shell">
    <header className="page-topbar"><div><h1>Order Tracking</h1><p>Order {orderId} · Philippine time (UTC+8)</p></div><Link className="client-btn ghost" to="/order-history">Back to orders</Link></header>
    {loading && !order && <p role="status" className="panel-card">Loading order…</p>}
    {error && <div className="order-notice" role="alert"><p>{error}{order ? " Previously loaded details remain visible." : ""}</p><button type="button" disabled={cancelling || loading} onClick={() => setRevision(value => value + 1)}>Retry order</button></div>}
    {notice && <p className="order-success" role="status">{notice}</p>}
    {order && <div className="tracking-layout"><section className="panel-card">
      {order.status === "Cancelled" ? <div className="order-cancelled-banner" role="status"><strong>Order cancelled</strong><p>This order is no longer being prepared or delivered.</p></div> : <div className="status-track" aria-label={`Order progress: ${order.status}`}>
        {steps.map((step, index) => <div key={step} aria-current={index === currentStep ? "step" : undefined} className={`status-step ${index <= currentStep ? "active" : ""}`}><div className="status-dot" aria-hidden="true">{index <= currentStep ? "✓" : index + 1}</div><span>{step}</span></div>)}
        <div className="status-bar"><div className="status-bar-fill" style={{ width: `${Math.max(0, currentStep) / (steps.length - 1) * 100}%` }} /></div>
      </div>}
      <OrderDetailsContent order={order} />
      {canCancel && <button type="button" className="client-btn danger" disabled={cancelling || loading || !!error} onClick={() => setCancelConfirmOpen(true)}>Cancel order</button>}
    </section><aside className="panel-card tracking-sidebar"><div className="tracking-sidebar-header"><h3>Recent status notifications</h3><button type="button" className="client-btn ghost" disabled={!unread.length || markingRead} onClick={markRead}>{markingRead ? "Updating…" : "Mark all read"}</button></div>
      {noticesError && <p role="status">{noticesError}</p>}
      {noticesLoading ? <p>Loading notifications…</p> : !orderNotifications.length && !noticesError ? <p>No status notifications yet.</p> : <><div className="notification-list">{noticePager.pageItems.map(item => <div key={item.id} className={`notification-item ${item.read ? "" : "unread"}`}><div><strong>{item.title}</strong><p>{item.message}</p></div><span>{orderDate(item.createdAt)}</span></div>)}</div>{!!orderNotifications.length && <Pagination {...noticePager} label="Notifications" />}</>}
      <div className="tracking-actions"><Link className="client-btn ghost" to="/order-history">View all orders</Link></div>
    </aside></div>}
    <ConfirmModal open={cancelConfirmOpen} title="Cancel this order?" message={`Order ${orderId} will be marked as Cancelled. This cannot be undone.`} cancelLabel="Keep order" confirmLabel="Yes, cancel order" onCancel={() => setCancelConfirmOpen(false)} onConfirm={confirmCancel} />
  </div></div>;
}
