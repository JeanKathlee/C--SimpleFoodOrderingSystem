import { useState, useEffect } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { fetchMenuItem, enrichMenuWithImages, formatPrice } from "../data/menuItems";
import { getAuthSession } from "../lib/auth";
import { useNotification } from "../hooks/useNotification";

export default function ProductDetails() {
  const navigate = useNavigate();
  const { error: errorNotif } = useNotification();
  const { id } = useParams();
  const [item, setItem] = useState(null);
  const [quantity, setQuantity] = useState(1);
  const [choice, setChoice] = useState("");
  const [loading, setLoading] = useState(true);

  const choiceOptions = item?.drinkOptions ?? [];

  // load the food item from the C# backend (/api/menu/{id})
  useEffect(() => {
    let active = true;
    setLoading(true);
    setItem(null);
    setChoice("");
    setQuantity(1);
    fetchMenuItem(id).then((found) => {
      if (!active) return;
      const loaded = found ? enrichMenuWithImages([found])[0] : null;
      setItem(loaded);
      setChoice(loaded?.drinkOptions?.find(option => option.addOn === 0)?.label ?? "");
      setLoading(false);
    });
    return () => { active = false; };
  }, [id]);

  if (!item) {
    if (loading) return null;
    return (
      <div className="client-page product-page">
        <div className="client-shell">
          <div className="panel-card product-empty">
            <h2>Product not found.</h2>
            <button type="button" className="client-btn" onClick={() => navigate("/menu")}>
              Back to Menu
            </button>
          </div>
        </div>
      </div>
    );
  }

  const selectedChoice = choiceOptions.find((option) => option.label === choice);
  const itemTotal = (item.price + (selectedChoice?.addOn ?? 0)) * quantity;

  const description = item.description || "No description available";

  const handleAddToBag = async () => {
    if (choiceOptions.length > 0 && !choice) {
      errorNotif("Please select a drink before adding this item to your cart.");
      return;
    }

    setLoading(true);
    try {
      const session = getAuthSession();
      const token = session?.token;
      
      if (!token) {
        errorNotif("Please log in to add items to cart");
        setLoading(false);
        return;
      }

      const response = await fetch("/api/cart/items", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          menuItemId: item.id,
          quantity,
          selectedChoice: choice,
        }),
      });

      if (response.ok) {
        navigate("/menu");
      } else {
        const error = await response.json();
        console.error("Failed to add to cart:", response.status, error);
        errorNotif(error?.message || "Failed to add item to cart");
      }
    } catch (err) {
      console.error("Error adding to cart:", err);
      errorNotif("Error adding item to cart: " + err.message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="client-page product-page">
      <div className="client-shell">
        <header className="page-topbar">
          <div>
            <h1>Product Details</h1>
            <p>Customize your order and add it to the cart.</p>
          </div>
          <div className="page-actions">
            <button type="button" className="client-btn ghost" onClick={() => navigate("/menu")}>← Back</button>
            <button type="button" className="client-btn ghost" onClick={() => navigate("/cart")}>Cart</button>
            <button type="button" className="client-btn" onClick={() => navigate("/checkout")}>
              Checkout
            </button>
          </div>
        </header>

        <section className="product-layout">
          <div className="panel-card product-media">
            <img src={item.image} alt={item.name} className="product-media-image" />
          </div>

          <div className="panel-card product-info">
            <div className="product-heading">
              <div>
                <span className="product-label">Featured Item</span>
                <h2>{item.name}</h2>
              </div>
              <strong>{formatPrice(item.price)}</strong>
            </div>

            <p className="product-description">{description}</p>

            {choiceOptions.length > 0 && <div className="product-choice-block">
              <h3>Drink Choice</h3>
              <p>Included drink selected by default. You can choose another option.</p>

              <div className="choice-list">
                {choiceOptions.map((option) => (
                  <label key={option.label} className="choice-row">
                    <div>
                      <input
                        type="radio"
                        name="drink-choice"
                        checked={choice === option.label}
                        onChange={() => setChoice(option.label)}
                      />
                      <span>{option.label}</span>
                    </div>
                    <em>{option.addOn > 0 ? `+ ${formatPrice(option.addOn)}` : "Included"}</em>
                  </label>
                ))}
              </div>
            </div>}

            <div className="product-action-row">
              <div className="product-qty-row">
                <button type="button" aria-label="Decrease quantity" onClick={() => setQuantity((q) => Math.max(1, q - 1))}>
                  −
                </button>
                <span>{quantity}</span>
                <button type="button" aria-label="Increase quantity" onClick={() => setQuantity((q) => q + 1)}>
                  +
                </button>
              </div>

              <button
                type="button"
                className="client-btn primary product-add"
                onClick={handleAddToBag}
                disabled={loading}
              >
                {loading ? "Adding..." : `Add To Cart - ${formatPrice(itemTotal)}`}
              </button>
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}
