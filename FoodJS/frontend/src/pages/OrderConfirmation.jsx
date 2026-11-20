import { useEffect, useState } from "react";
import { Link, useLocation, useParams } from "react-router-dom";
import OrderDetailsContent from "../components/OrderDetailsContent";
import { checkoutRequest, readCheckout, cleanupCheckout, clearCheckout } from "../lib/checkout";
import "../orders.css";
import "../checkout.css";

export default function OrderConfirmation() {
  const { orderId } = useParams(); const location = useLocation();
  const [order, setOrder] = useState(null); const [loading, setLoading] = useState(true);
  const [error, setError] = useState(""); const [version, setVersion] = useState(0);
  const [record, setRecord] = useState(() => {
    const stored = readCheckout(); const passed = location.state?.acceptedCheckout;
    if (stored?.status === "accepted" && stored.orderId === orderId) return stored;
    return stored?.key === passed?.key && passed?.orderId === orderId ? passed : null;
  });
  const [cleaning, setCleaning] = useState(!!record); const [cleanupWarning, setCleanupWarning] = useState("");
  const [acceptedOrderId] = useState(record?.orderId);
  const [cleanupVersion, setCleanupVersion] = useState(0);
  useEffect(() => {
    const controller = new AbortController(); const signal = controller.signal;
    setLoading(true); setError("");
    checkoutRequest(`/api/orders/${encodeURIComponent(orderId)}`, { signal }).then(body => {
      if (!body.id || !Array.isArray(body.items)) throw new Error("Order details were incomplete. Please retry.");
      if (!signal.aborted) setOrder(body);
    }).catch(failure => { if (!signal.aborted) setError(failure.message); }).finally(() => { if (!signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [orderId, version]);
  useEffect(() => {
    if (!record || record.orderId !== orderId) return;
    let disposed = false; setCleaning(true);
    cleanupCheckout(record).then(warning => {
      if (disposed) return;
      setCleanupWarning(warning); setCleaning(false); if (!warning) setRecord(null);
    });
    return () => { disposed = true; };
  }, [record, orderId, cleanupVersion]);
  const descriptions = { Pending: "Your order was received and is waiting for preparation.", Preparing: "Your meal is being prepared.", Ready: "Your order is ready for delivery.", Delivered: "Your order has been delivered.", Cancelled: "This order was cancelled." };
  return <div className="client-page order-page checkout-confirmation"><div className="client-shell">
    <header className="page-topbar"><div><h1>Order {orderId}</h1><p>{order ? descriptions[order.status] || "Saved order details." : acceptedOrderId === orderId ? "Your order was accepted. Loading its saved details…" : "Saved order details"}</p></div><Link className="client-btn ghost" to="/menu">Back to menu</Link></header>
    {cleaning && <p className="order-success" role="status">Your order is confirmed. Updating your bag…</p>}
    {cleanupWarning && <section className="order-notice" role="alert"><div><p>{cleanupWarning}</p><div className="checkout-cleanup-actions"><button type="button" disabled={cleaning} onClick={() => setCleanupVersion(value => value + 1)}>Retry bag cleanup</button><button type="button" disabled={cleaning} onClick={() => { if (readCheckout()?.key === record?.key) clearCheckout(); setRecord(null); setCleanupWarning(""); }}>Keep bag for later</button><Link to="/cart">Review bag</Link></div></div></section>}
    {loading && <p role="status">Loading saved order details…</p>}
    {error && <div className="order-notice" role="alert"><p>{acceptedOrderId === orderId ? `Order ${orderId} was already accepted. ` : ""}{error}</p><button type="button" disabled={loading} onClick={() => setVersion(value => value + 1)}>Retry order details</button></div>}
    {order && <section className="panel-card"><div className="checkout-summary-heading"><h2>Saved order details</h2><span className={`status-pill ${order.status.toLowerCase()}`}>{order.status}</span></div><OrderDetailsContent order={order} /></section>}
    <div className="order-actions"><Link className="client-btn primary" to={`/order-tracking/${orderId}`}>Track order</Link><Link className="client-btn ghost" to="/order-history">View order history</Link></div>
  </div></div>;
}
