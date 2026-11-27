import { useEffect, useState } from "react";
import ConfirmModal from "../ui/Modal";
import OrderDetailsContent from "../OrderDetailsContent";
import { orderRequest } from "../../lib/orders";

export default function OrderDetailsDialog({ orderId, onClose }) {
  const [order, setOrder] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    const controller = new AbortController(); const signal = controller.signal;
    setOrder(null); setError(""); setLoading(true);
    orderRequest(`/api/orders/admin/${encodeURIComponent(orderId)}`, { signal }).then(body => { if (!signal.aborted) setOrder(body); })
      .catch(failure => { if (!signal.aborted) setError(failure.message); }).finally(() => { if (!signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [orderId, revision]);
  return <ConfirmModal open title={`Order ${orderId}`} message="Order details and saved delivery information." cancelLabel="Close" hideConfirm size="wide" onCancel={onClose}>
    {loading && <p role="status">Loading order details…</p>}
    {error && <div className="order-notice" role="alert"><p>{error}</p><button type="button" onClick={() => setRevision(value => value + 1)}>Retry details</button></div>}
    {order && <OrderDetailsContent order={order} />}
  </ConfirmModal>;
}
