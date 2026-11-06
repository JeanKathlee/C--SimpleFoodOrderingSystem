import { useState, useEffect } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { fetchMenuItem, enrichMenuWithImages, formatPrice } from "../data/menuItems";
import { getAuthSession } from "../lib/auth";
import { useNotification } from "../hooks/useNotification";

export default function ProductDetails() {
  const navigate = useNavigate();
  const { success, error: errorNotif } = useNotification();
  const { id } = useParams();
  const [item, setItem] = useState(null);
  const [quantity, setQuantity] = useState(1);
  const [choice, setChoice] = useState("Go Regular Iced Tea");
  const [loading, setLoading] = useState(true);
  const [showFirstAddPopup, setShowFirstAddPopup] = useState(false);
  const [addedItemName, setAddedItemName] = useState("");

  const choiceOptions = [
    { label: "Go Large Iced Tea", addOn: 50 },
    { label: "Go Medium Iced Tea", addOn: 45 },
    { label: "Go Regular Iced Tea", addOn: 0 },
  ];

  // load the food item from the C# backend (/api/menu/{id})
  useEffect(() => {
    fetchMenuItem(id).then((found) => {
      setItem(found ? enrichMenuWithImages([found])[0] : null);
      setLoading(false);
    });
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

  const selectedChoice = choiceOptions.find((option) => option.label === choice) || choiceOptions[2];
  const itemTotal = (item.price + selectedChoice.addOn) * quantity;

  const description = item.description || "No description available";

  const handleAddToBag = async () => {
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
          name: item.name,
          price: item.price + selectedChoice.addOn,
          quantity,
          selectedChoice: choice,
        }),
      });

      if (response.ok) {
        setAddedItemName(item.name);
        setShowFirstAddPopup(true);
        success(`${item.name} added to cart!`);
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

        {showFirstAddPopup && (
          <div className="cart-popup-backdrop" role="presentation" onClick={() => setShowFirstAddPopup(false)}>
            <section
              className="panel-card cart-popup"
              role="dialog"
              aria-modal="true"
              aria-label="Item added to cart"
              onClick={(event) => event.stopPropagation()}
            >
              <div className="cart-popup-icon">✓</div>
              <div>
                <h2>Added to cart</h2>
                <p>{addedItemName} has been added to your cart.</p>
              </div>
              <div className="cart-popup-actions">
                <button
                  type="button"
                  className="client-btn ghost"
                  onClick={() => {
                    setShowFirstAddPopup(false);
                    navigate("/menu");
                  }}
                >
                  Continue shopping
                </button>
                <button type="button" className="client-btn" onClick={() => navigate("/cart")}>Go to cart</button>
              </div>
            </section>
          </div>
        )}

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

            <div className="product-choice-block">
              <h3>Drink Choice</h3>
              <p>Select 1 option</p>

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
            </div>

            <div className="product-action-row">
              <div className="product-qty-row">
                <button type="button" onClick={() => setQuantity((q) => Math.max(1, q - 1))}>
                  −
                </button>
                <span>{quantity}</span>
                <button type="button" onClick={() => setQuantity((q) => q + 1)}>
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