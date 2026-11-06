import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import logo from "../../assets/logo.png";
import { clearAuthSession, getAuthSession } from "../lib/auth";
import {
  fetchMenuFromBackend,
  getCategoriesFromMenu,
  enrichMenuWithImages,
  formatPrice,
} from "../data/menuItems";
import { useNotification } from "../hooks/useNotification";

export default function Menu() {
  const navigate = useNavigate();
  const { success, error: errorNotif } = useNotification();
  const [tabs, setTabs] = useState(["All"]);
  const [activeTab, setActiveTab] = useState("All");
  const [query, setQuery] = useState("");
  const [logoutConfirm, setLogoutConfirm] = useState(false);
  const [cartItems, setCartItems] = useState([]);
  const [loading, setLoading] = useState(true);
  const [showBag, setShowBag] = useState(false);
  const [showAccountMenu, setShowAccountMenu] = useState(false);

  // Load sa menu and cart
  useEffect(() => {
    const loadData = async () => {
      try {
        // Load menu gikan backend, only to make the category tabs
        const items = await fetchMenuFromBackend();
        setTabs(getCategoriesFromMenu(items));

        // Load cart gikan API
        const session = getAuthSession();
        const token = session?.token;
        if (token) {
          const cartResponse = await fetch("/api/cart", {
            headers: {
              Authorization: `Bearer ${token}`,
            },
          });

          if (cartResponse.ok) {
            const cartData = await cartResponse.json();
            setCartItems(cartData || []);
          }
        }
      } catch (err) {
        console.error("Failed to load data:", err);
      } finally {
        setLoading(false);
      }
    };

    loadData();
  }, []);

  // search and category filter are done by the C# backend (/api/menu?search=&category=)
  const [visibleItems, setVisibleItems] = useState([]);
  useEffect(() => {
    let stale = false;
    fetchMenuFromBackend({ search: query, category: activeTab }).then((items) => {
      if (!stale) setVisibleItems(enrichMenuWithImages(items));
    });
    // ignore old answers if the user typed again before this one came back
    return () => {
      stale = true;
    };
  }, [activeTab, query]);

  const handleLogout = () => {
    setLogoutConfirm(true);
  };

  const confirmLogout = () => {
    clearAuthSession();
    setLogoutConfirm(false);
    navigate("/");
  };

  const openProductDetails = (item) => {
    navigate(`/product/${item.id}`);
  };

  const adjustQuantity = async (menuItemId, newQuantity) => {
    if (newQuantity <= 0) {
      await removeFromCart(menuItemId);
      return;
    }

    try {
      const session = getAuthSession();
      const token = session?.token;
      
      if (!token) {
        errorNotif("Please log in to update cart");
        return;
      }

      const response = await fetch(`/api/cart/items/${menuItemId}`, {
        method: "PATCH",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ quantity: newQuantity }),
      });

      if (!response.ok) {
        const error = await response.json();
        console.error("Failed to update cart:", response.status, error);
        errorNotif(error?.message || "Failed to update cart item");
        return;
      }

      setCartItems((prev) =>
        prev.map((item) =>
          item.menuItemId === menuItemId
            ? { ...item, quantity: newQuantity }
            : item
        )
      );
      success("Cart updated successfully!");
    } catch (err) {
      console.error("Failed to update cart:", err);
      errorNotif("Network error: " + err.message);
    }
  };

  const removeFromCart = async (menuItemId) => {
    try {
      const session = getAuthSession();
      const token = session?.token;
      
      if (!token) {
        errorNotif("Please log in to remove items from cart");
        return;
      }

      const response = await fetch(`/api/cart/items/${menuItemId}`, {
        method: "DELETE",
        headers: {
          Authorization: `Bearer ${token}`,
        },
      });

      if (!response.ok) {
        const error = await response.json();
        console.error("Failed to remove from cart:", response.status, error);
        errorNotif(error?.message || "Failed to remove from cart");
        return;
      }

      setCartItems((prev) =>
        prev.filter((item) => item.menuItemId !== menuItemId)
      );
      success("Item removed from cart");
    } catch (err) {
      console.error("Failed to remove from cart:", err);
      errorNotif("Network error: " + err.message);
    }
  };

  const clearCart = async () => {
    try {
      const session = getAuthSession();
      const token = session?.token;
      
      if (!token) {
        errorNotif("Please log in to clear cart");
        return;
      }

      const response = await fetch("/api/cart", {
        method: "DELETE",
        headers: {
          Authorization: `Bearer ${token}`,
        },
      });

      if (!response.ok) {
        const error = await response.json();
        console.error("Failed to clear cart:", response.status, error);
        errorNotif(error?.message || "Failed to clear cart");
        return;
      }

      setCartItems([]);
      success("Cart cleared successfully!");
    } catch (err) {
      console.error("Failed to clear cart:", err);
      errorNotif("Network error: " + err.message);
    }
  };

  const cartCount = useMemo(
    () => cartItems.reduce((total, item) => total + item.quantity, 0),
    [cartItems]
  );

  const subtotal = useMemo(
    () => cartItems.reduce((total, item) => total + item.price * item.quantity, 0),
    [cartItems]
  );

  return (
    <div className="client-page menu-page">
      <div className="client-shell">
        <div className="menu-topbar-wrap">
          <header className="page-topbar menu-topbar">
            <div className="menu-brand">
              <img src={logo} alt="FoodJS" />
              <div>
                <h1>FoodJS Menu</h1>
                <p>Choose your favorites and order fast.</p>
              </div>
            </div>

            <div className="page-actions">
              <button
                type="button"
                className="client-btn ghost"
                onClick={() => setShowBag((prev) => !prev)}
              >
                My Bag ({cartCount})
              </button>
              <div
                className="menu-account"
                tabIndex={0}
                onBlur={(event) => {
                  if (!event.currentTarget.contains(event.relatedTarget)) {
                    setShowAccountMenu(false);
                  }
                }}
                onKeyDown={(event) => {
                  if (event.key === "Escape") {
                    setShowAccountMenu(false);
                  }
                }}
              >
                <button
                  type="button"
                  className="client-btn ghost menu-account-trigger"
                  onClick={() => setShowAccountMenu((prev) => !prev)}
                  aria-haspopup="menu"
                  aria-expanded={showAccountMenu}
                >
                  <span className="avatar">
                    {getAuthSession()?.user?.profileImage ? (
                      <img
                        src={getAuthSession()?.user?.profileImage}
                        alt="Account avatar"
                      />
                    ) : (
                      getAuthSession()?.user?.name?.[0] || "U"
                    )}
                  </span>
                  Account ▾
                </button>
                {showAccountMenu && (
                  <div className="menu-account-dropdown" role="menu">
                    <button
                      type="button"
                      role="menuitem"
                      onClick={() => {
                        setShowAccountMenu(false);
                        navigate("/profile");
                      }}
                    >
                      Profile
                    </button>
                    <button
                      type="button"
                      role="menuitem"
                      onClick={() => {
                        setShowAccountMenu(false);
                        navigate("/order-history");
                      }}
                    >
                      Order History
                    </button>
                    <button
                      type="button"
                      role="menuitem"
                      className="danger"
                      onClick={() => {
                        setShowAccountMenu(false);
                        handleLogout();
                      }}
                    >
                      Logout
                    </button>
                  </div>
                )}
              </div>
            </div>
          </header>

          {showBag && (
            <div className="menu-bag-dropdown">
              <div className="menu-bag-header">
                <div>
                  <h2>My Bag</h2>
                  <p>{cartCount} item{cartCount === 1 ? "" : "s"}</p>
                </div>
                <div className="menu-bag-header-actions">
                  <button
                    type="button"
                    className="client-btn ghost"
                    onClick={clearCart}
                    disabled={!cartItems.length}
                  >
                    Clear
                  </button>
                </div>
              </div>

              {cartItems.length === 0 ? (
                <div className="menu-bag-empty">
                  <p>Your bag is empty.</p>
                </div>
              ) : (
                <>
                  <div className="menu-bag-list">
                    {cartItems.map((item) => (
                      <div key={item.menuItemId} className="menu-bag-item">
                        <div>
                          <h3>{item.name}</h3>
                          <p>{formatPrice(item.price * item.quantity)}</p>
                        </div>
                        <div className="menu-bag-actions">
                          <button type="button" onClick={() => adjustQuantity(item.menuItemId, item.quantity - 1)}>
                            −
                          </button>
                          <span>{item.quantity}</span>
                          <button type="button" onClick={() => adjustQuantity(item.menuItemId, item.quantity + 1)}>
                            +
                          </button>
                          <button type="button" className="remove" onClick={() => removeFromCart(item.menuItemId)}>
                            Remove
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>

                  <div className="menu-bag-total">
                    <div>
                      <span>Subtotal</span>
                      <strong>{formatPrice(subtotal)}</strong>
                    </div>
                    <button type="button" className="client-btn primary" onClick={() => navigate("/cart")}>
                      Check Cart
                    </button>
                  </div>
                </>
              )}
            </div>
          )}
        </div>

        <section className="panel-card menu-toolbar">
          <div className="menu-toolbar-header">
            <p className="menu-greeting">Hi, {getAuthSession()?.user?.name || "Customer"}</p>
          </div>
          <div className="menu-search-row">
            <input
              className="menu-search-input"
              type="search"
              placeholder="Search menu items"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
            />
            <span className="menu-search-icon">⌕</span>
          </div>

          <div className="menu-tab-row">
            {tabs.map((tab) => (
              <button
                key={tab}
                type="button"
                className={`menu-tab ${activeTab === tab ? "active" : ""}`}
                onClick={() => setActiveTab(tab)}
              >
                {tab}
              </button>
            ))}
          </div>
        </section>

        {loading ? (
          <div className="page-loading">Loading menu...</div>
        ) : (
          <section className="menu-layout">
            <div className="menu-grid">
              {visibleItems.map((item) => (
                <article key={item.name} className="menu-card">
                  <button
                    type="button"
                    className="menu-card-image-button"
                    onClick={() => openProductDetails(item)}
                    aria-label={`View ${item.name} details`}
                  >
                    <img
                      src={item.image}
                      alt={item.name}
                      className="menu-card-image"
                      loading="lazy"
                      onError={(event) => {
                        event.currentTarget.src = logo;
                        event.currentTarget.classList.add("is-fallback");
                      }}
                    />
                  </button>
                  <div className="menu-card-body">
                    <h3>{item.name}</h3>
                    <p>{item.description || "Freshly prepared and served hot."}</p>
                  </div>
                  <div className="menu-card-footer">
                    <strong>{formatPrice(item.price)}</strong>
                    <div className="menu-card-actions">
                      <button type="button" className="client-btn primary" onClick={() => openProductDetails(item)}>
                        View item
                      </button>
                    </div>
                  </div>
                </article>
              ))}

              {!visibleItems.length && (
                <div className="panel-card empty-state">
                  <h2>No matches found</h2>
                  <p>Try a different keyword or category.</p>
                </div>
              )}
            </div>

          </section>
        )}

        {logoutConfirm && (
          <div className="cart-popup-backdrop" role="presentation" onClick={() => setLogoutConfirm(false)}>
            <section
              className="panel-card cart-popup"
              role="dialog"
              aria-modal="true"
              aria-label="Confirm logout"
              onClick={(event) => event.stopPropagation()}
            >
              <div>
                <h2>Confirm Logout</h2>
                <p>Are you sure you want to logout?</p>
              </div>
              <div className="cart-popup-actions">
                <button type="button" className="client-btn ghost" onClick={() => setLogoutConfirm(false)}>
                  Cancel
                </button>
                <button type="button" className="client-btn" onClick={confirmLogout}>Logout</button>
              </div>
            </section>
          </div>
        )}
      </div>
    </div>
  );
}
