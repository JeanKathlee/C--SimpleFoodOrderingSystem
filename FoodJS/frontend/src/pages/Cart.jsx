import { useEffect, useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { formatPrice } from "../data/menuItems";
import { getAuthSession } from "../lib/auth";
import { cartItemKey } from "../lib/cartItems";

export default function Cart() {
  const navigate = useNavigate();
  const [cartItems, setCartItems] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const loadCart = async () => {
      try {
        const session = getAuthSession();
        const token = session?.token;

        if (!token) {
          navigate("/login");
          return;
        }

        const response = await fetch("/api/cart", {
          headers: { Authorization: `Bearer ${token}` },
        });

        if (response.ok) {
          const data = await response.json();
          setCartItems(data || []);
        }
      } catch (err) {
        console.error("Failed to load cart:", err);
      } finally {
        setLoading(false);
      }
    };

    loadCart();
  }, [navigate]);

  const adjustQuantity = async (cartItemId, newQuantity) => {
    if (newQuantity <= 0) {
      await removeFromCart(cartItemId);
      return;
    }

    try {
      const session = getAuthSession();
      const token = session?.token;

      const response = await fetch(`/api/cart/items/${cartItemId}`, {
        method: "PATCH",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ quantity: newQuantity }),
      });

      if (response.ok) {
        setCartItems((prev) =>
          prev.map((item) =>
            (item.id ?? item.menuItemId) === cartItemId
              ? { ...item, quantity: newQuantity }
              : item
          )
        );
      }
    } catch (err) {
      console.error("Failed to update cart:", err);
    }
  };

  const removeFromCart = async (cartItemId) => {
    try {
      const session = getAuthSession();
      const token = session?.token;

      const response = await fetch(`/api/cart/items/${cartItemId}`, {
        method: "DELETE",
        headers: { Authorization: `Bearer ${token}` },
      });

      if (response.ok) {
        setCartItems((prev) =>
          prev.filter((item) => (item.id ?? item.menuItemId) !== cartItemId)
        );
      }
    } catch (err) {
      console.error("Failed to remove from cart:", err);
    }
  };

  const clearCart = async () => {
    try {
      const session = getAuthSession();
      const token = session?.token;

      const response = await fetch("/api/cart", {
        method: "DELETE",
        headers: { Authorization: `Bearer ${token}` },
      });

      if (response.ok) {
        setCartItems([]);
      }
    } catch (err) {
      console.error("Failed to clear cart:", err);
    }
  };

  const total = useMemo(
    () => cartItems.reduce((sum, item) => sum + item.price * item.quantity, 0),
    [cartItems]
  );

  if (loading) {
    return <div className="page-loading">Loading...</div>;
  }

  return (
    <div className="client-page cart-page">
      <div className="client-shell">
        <header className="page-topbar">
          <div>
            <h1>My Cart</h1>
            <p>Review your items before checkout.</p>
          </div>
          <div className="page-actions">
            <button className="client-btn ghost" onClick={() => navigate("/menu")}>← Back to Menu</button>
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

        {cartItems.length === 0 ? (
          <section className="panel-card empty-state">
            <h2>Your cart is empty</h2>
            <p>Browse the menu to add your first meal.</p>
            <button className="client-btn primary" onClick={() => navigate("/menu")}>Start Adding Items</button>
          </section>
        ) : (
          <div className="cart-layout">
            <section className="panel-card cart-list">
              {cartItems.map((item) => (
                <div key={cartItemKey(item)} className="cart-item">
                  <div className="cart-item-info">
                    <h3>{item.name}</h3>
                    {item.selectedChoice && <p>{item.selectedChoice}</p>}
                    <p>{formatPrice(item.price)} each</p>
                  </div>

                  <div className="cart-item-controls">
                    <div className="qty-control">
                      <button type="button" aria-label={`Decrease ${item.name} quantity`} onClick={() => adjustQuantity(item.id ?? item.menuItemId, item.quantity - 1)}>
                        −
                      </button>
                      <span>{item.quantity}</span>
                      <button type="button" aria-label={`Increase ${item.name} quantity`} onClick={() => adjustQuantity(item.id ?? item.menuItemId, item.quantity + 1)}>
                        +
                      </button>
                    </div>

                    <strong>{formatPrice(item.price * item.quantity)}</strong>

                    <button
                      type="button"
                      className="client-btn danger ghost"
                      onClick={() => removeFromCart(item.id ?? item.menuItemId)}
                    >
                      Remove
                    </button>
                  </div>
                </div>
              ))}
            </section>

            <aside className="panel-card cart-summary">
              <h2>Summary</h2>
              <div className="summary-row">
                <span>Total</span>
                <strong>{formatPrice(total)}</strong>
              </div>
              <button className="client-btn primary" onClick={() => navigate("/checkout")}>
                Checkout
              </button>
              <button className="client-btn danger" onClick={clearCart}>Clear Cart</button>
              <button className="client-btn ghost" onClick={() => navigate("/menu")}>
                Order More
              </button>
            </aside>
          </div>
        )}
      </div>
    </div>
  );
}
