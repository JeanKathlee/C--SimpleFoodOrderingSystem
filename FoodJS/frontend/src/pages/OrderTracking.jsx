import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { formatPrice } from "../data/menuItems";
import { getAuthSession } from "../lib/auth";
import { cartItemKey } from "../lib/cartItems";
import { useNotification } from "../hooks/useNotification";

export default function OrderTracking() {
  const navigate = useNavigate();
  const { success, error: errorNotif } = useNotification();
  const { orderId } = useParams();
  const [order, setOrder] = useState(null);
  const [loading, setLoading] = useState(true);
  const [orderNotifications, setOrderNotifications] = useState([]);
  const [noticesLoading, setNoticesLoading] = useState(true);
  const [cancelConfirmOpen, setCancelConfirmOpen] = useState(false);

  const loadOrder = async () => {
    try {
      const session = getAuthSession();
      const token = session?.token;
      
      if (!token || !orderId) {
        navigate("/menu");
        return;
      }

      const response = await fetch(`/api/orders/${orderId}`, {
        headers: { Authorization: `Bearer ${token}` },
      });

      if (response.ok) {
        const data = await response.json();
        setOrder(data);
      } else {
        navigate("/menu");
      }
    } catch (err) {
      console.error("Failed to load order:", err);
    } finally {
      setLoading(false);
    }
  };

  const loadNotifications = async () => {
    try {
      const session = getAuthSession();
      const token = session?.token;

      if (!token) {
        return;
      }

      const response = await fetch("/api/notifications", {
        headers: { Authorization: `Bearer ${token}` },
      });

      if (response.ok) {
        const data = await response.json();
        const notifications = Array.isArray(data)
          ? data.filter((notice) => notice.orderId === orderId)
          : [];
        setOrderNotifications(notifications);
      }
    } catch (err) {
      console.error("Failed to load notifications:", err);
    } finally {
      setNoticesLoading(false);
    }
  };

  useEffect(() => {
    loadOrder();
    loadNotifications();
    
    // matic update every5 seconds
    const interval = setInterval(() => {
      loadOrder();
      loadNotifications();
    }, 5000);
    
    return () => clearInterval(interval);
    // Reload only when the tracked order or navigation context changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [orderId, navigate]);

  const confirmCancelOrder = async () => {
    try {
      const session = getAuthSession();
      const token = session?.token;

      const response = await fetch(`/api/orders/${orderId}/cancel`, {
        method: "PATCH",
        headers: { Authorization: `Bearer ${token}` },
      });

      if (response.ok) {
        const updated = await response.json();
        setOrder(updated);
        setCancelConfirmOpen(false);
        success("Order cancelled successfully!");
      } else {
        const error = await response.json();
        setCancelConfirmOpen(false);
        errorNotif(error.message || "Failed to cancel order");
      }
    } catch (err) {
      console.error("Error cancelling order:", err);
      setCancelConfirmOpen(false);
      errorNotif("Error: " + err.message);
    }
  };

  const canCancel = order && (order.status === "Pending" || order.status === "Preparing");
  const statusSteps = ["Pending", "Preparing", "Ready", "Delivered"];
  const currentStatusIndex = Math.max(
    0,
    statusSteps.indexOf(order?.status || "Pending")
  );
  const unreadNotifications = orderNotifications.filter((notice) => !notice.read);

  const markOrderNotificationsRead = async () => {
    if (unreadNotifications.length === 0) {
      return;
    }

    try {
      const session = getAuthSession();
      const token = session?.token;

      await Promise.all(
        unreadNotifications.map((notice) =>
          fetch(`/api/notifications/${notice.id}/read`, {
            method: "PATCH",
            headers: { Authorization: `Bearer ${token}` },
          })
        )
      );

      setOrderNotifications((prev) =>
        prev.map((notice) =>
          unreadNotifications.some((unread) => unread.id === notice.id)
            ? { ...notice, read: true }
            : notice
        )
      );
    } catch (err) {
      console.error("Failed to mark notifications as read:", err);
    }
  };

  if (loading) {
    return <div className="page-loading">Loading...</div>;
  }

  if (!order) {
    return <div className="page-loading">Order not found</div>;
  }

  return (
    <div className="client-page tracking-page">
      <div className="client-shell">
        <header className="page-topbar">
          <div>
            <h1>Order Tracking</h1>
            <p>Order ID: {order.id}</p>
          </div>
          <div className="page-actions">
            <button className="client-btn ghost" onClick={() => navigate("/menu")}>← Back</button>
            <button className="client-btn ghost" onClick={() => navigate("/menu")}>Home</button>
          </div>
        </header>

        <div className="tracking-layout">
          <section className="panel-card">
            <div className="status-track">
              {statusSteps.map((step, index) => (
                <div key={step} className={`status-step ${index <= currentStatusIndex ? "active" : ""}`}>
                  <div className="status-dot">
                    {index <= currentStatusIndex ? "✓" : index + 1}
                  </div>
                  <span>{step}</span>
                </div>
              ))}
              <div className="status-bar">
                <div
                  className="status-bar-fill"
                  style={{ width: `${(currentStatusIndex / (statusSteps.length - 1)) * 100}%` }}
                />
              </div>
            </div>

            <div className="tracking-status-card">
              <div>
                <p>Current Status</p>
                <h2>{order.status}</h2>
              </div>
              <span className={`status-pill ${order.status.toLowerCase()}`}>{order.status}</span>
            </div>

            <div className="tracking-details">
              <div>
                <span>Customer</span>
                <strong>{order.customerName}</strong>
              </div>
              <div>
                <span>Payment</span>
                <strong>{order.paymentMethod}</strong>
              </div>
              <div>
                <span>Placed</span>
                <strong>{new Date(order.createdAt).toLocaleString()}</strong>
              </div>
            </div>

            <div className="order-items">
              <h3>Items</h3>
              {order.items.map((item) => (
                <div key={cartItemKey(item)} className="order-item-row">
                  <div>
                    <strong>{item.name}</strong>
                    {item.selectedChoice && <p>{item.selectedChoice}</p>}
                    <p>Qty: {item.quantity}</p>
                  </div>
                  <span>{formatPrice(item.price * item.quantity)}</span>
                </div>
              ))}
            </div>

            <div className="order-total-row">
              <span>Total</span>
              <strong>{formatPrice(order.total)}</strong>
            </div>

            {canCancel && (
              <button className="client-btn danger" onClick={() => setCancelConfirmOpen(true)}>
                Cancel Order
              </button>
            )}
          </section>

          <aside className="panel-card tracking-sidebar">
            <div className="tracking-sidebar-header">
              <h3>Status Notifications</h3>
              <button
                type="button"
                className="client-btn ghost"
                onClick={markOrderNotificationsRead}
                disabled={unreadNotifications.length === 0}
              >
                Mark all read
              </button>
            </div>

            {noticesLoading ? (
              <p className="muted-text">Loading notifications...</p>
            ) : orderNotifications.length === 0 ? (
              <p className="muted-text">No status updates yet.</p>
            ) : (
              <div className="notification-list">
                {orderNotifications.map((notice) => (
                  <div key={notice.id} className={`notification-item ${notice.read ? "" : "unread"}`}>
                    <div>
                      <strong>{notice.title}</strong>
                      <p>{notice.message}</p>
                    </div>
                    <span>{new Date(notice.createdAt).toLocaleTimeString()}</span>
                  </div>
                ))}
              </div>
            )}

            <div className="tracking-actions">
              <button className="client-btn ghost" onClick={() => navigate("/order-history")}>
                View All Orders
              </button>
            </div>
          </aside>
        </div>
      </div>

      {cancelConfirmOpen && (
        <div
          className="client-modal-backdrop"
          role="presentation"
          onClick={() => setCancelConfirmOpen(false)}
        >
          <div
            className="client-confirm-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="cancel-order-title"
            onClick={(event) => event.stopPropagation()}
          >
            <span className="client-modal-kicker">Cancel order</span>
            <h2 id="cancel-order-title">Cancel this order?</h2>
            <p>
              Order {order.id} will be marked as cancelled. You can place a new order from the menu anytime.
            </p>
            <div className="client-modal-summary">
              <span>Total</span>
              <strong>{formatPrice(order.total)}</strong>
            </div>
            <div className="client-modal-actions">
              <button
                type="button"
                className="client-btn ghost"
                onClick={() => setCancelConfirmOpen(false)}
              >
                Keep Order
              </button>
              <button type="button" className="client-btn danger" onClick={confirmCancelOrder}>
                Yes, Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
