import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { formatPrice } from "../data/menuItems";
import { getAuthSession } from "../lib/auth";
import { cartItemKey } from "../lib/cartItems";

export default function OrderConfirmation() {
  const navigate = useNavigate();
  const { orderId } = useParams();
  const [order, setOrder] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
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
        navigate("/menu");
      } finally {
        setLoading(false);
      }
    };

    loadOrder();
  }, [orderId, navigate]);

  if (loading) {
    return <div className="page-loading">Loading...</div>;
  }

  if (!order) {
    return <div className="page-loading">Order not found</div>;
  }

  return (
    <div className="client-page order-page">
      <div className="client-shell">
        <header className="page-topbar">
          <div>
            <h1>Order Success</h1>
            <p>Thank you for ordering with FoodJS.</p>
          </div>
          <div className="page-actions">
            <button className="client-btn ghost" onClick={() => navigate("/menu")}>Menu</button>
            <button
              className="client-btn ghost"
              onClick={() => {
                const session = getAuthSession();
                navigate(session?.token ? "/menu" : "/");
              }}
            >
              Home
            </button>
          </div>
        </header>

        <section className="panel-card order-hero">
          <div>
            <span className="order-hero-label">Order confirmed</span>
            <h2>Your meal is being prepared</h2>
            <p>Track this order anytime using the ID below.</p>
          </div>
          <div className="order-hero-meta">
            <div>
              <span>Order ID</span>
              <strong>{order.id}</strong>
            </div>
            <span className={`status-pill ${order.status.toLowerCase()}`}>{order.status}</span>
          </div>
        </section>

        <div className="order-confirm-grid">
          <section className="panel-card order-details">
            <div className="order-section-header">
              <h3>Order Details</h3>
              <span>Summary</span>
            </div>
            <div className="order-detail-grid">
              <div>
                <span>Customer: </span>
                <strong>{order.customerName}</strong>
              </div>
              <div>
                <span>Payment: </span>
                <strong>{order.paymentMethod}</strong>
              </div>
              <div>
                <span>Placed: </span>
                <strong>{new Date(order.createdAt).toLocaleString()}</strong>
              </div>
            </div>
          </section>

          <section className="panel-card order-items-card">
            <div className="order-section-header">
              <h3>Items</h3>
              <span>{order.items.length} item{order.items.length === 1 ? "" : "s"}</span>
            </div>
            <div className="order-items">
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
          </section>
        </div>

        <div className="order-actions">
          <button className="client-btn primary" onClick={() => navigate(`/order-tracking/${order.id}`)}>
            Track Order
          </button>
          <button className="client-btn ghost" onClick={() => navigate("/order-history")}
          >
            View Order History
          </button>
        </div>
      </div>
    </div>
  );
}
