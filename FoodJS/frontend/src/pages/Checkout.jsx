import { useState, useEffect, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import DeliveryMapPicker from "../components/DeliveryMapPicker";
import { formatPrice } from "../data/menuItems";
import { getAuthSession } from "../lib/auth";
import { cartItemKey } from "../lib/cartItems";
import { useNotification } from "../hooks/useNotification";

function normalizeMobileNumber(value = "") {
  return value.replace(/[\s-]/g, "");
}

function isValidMobileNumber(value = "") {
  const normalized = normalizeMobileNumber(value);
  return /^(09\d{9}|\+639\d{9}|639\d{9})$/.test(normalized);
}

export default function Checkout() {
  const navigate = useNavigate();
  const { success, error: errorNotif } = useNotification();
  const [cartItems, setCartItems] = useState([]);
  const [paymentMethod, setPaymentMethod] = useState("cash");
  const [couponCode, setCouponCode] = useState("");
  const [appliedCoupon, setAppliedCoupon] = useState(null);
  const [discountAmount, setDiscountAmount] = useState(0);
  const [couponError, setCouponError] = useState("");
  const [loading, setLoading] = useState(false);
  const [orderConfirm, setOrderConfirm] = useState(false);
  const [savedAddresses, setSavedAddresses] = useState([]);
  const [selectedAddress, setSelectedAddress] = useState("");
  const [profileDefaults, setProfileDefaults] = useState({
    address: "",
    lastOrderAddress: "",
    lastOrderMobileNumber: "",
  });
  const [formData, setFormData] = useState({
    firstName: "",
    lastName: "",
    mobileNumber: "",
    address: "",
    note: "",
  });

  // Load cart gikan API
  useEffect(() => {
    const loadData = async () => {
      try {
        const session = getAuthSession();
        const token = session?.token;
        if (!token) {
          return;
        }

        const cartResponse = await fetch("/api/cart", {
          headers: {
            Authorization: `Bearer ${token}`,
          },
        });

        if (cartResponse.ok) {
          const data = await cartResponse.json();
          setCartItems(data || []);
        }

        const profileResponse = await fetch("/api/users/me", {
          headers: {
            Authorization: `Bearer ${token}`,
          },
        });

        if (profileResponse.ok) {
          const profile = await profileResponse.json();
          const defaultAddress = profile.address || profile.lastOrderAddress || "";
          const defaultMobile = profile.mobileNumber || profile.lastOrderMobileNumber || "";

          setProfileDefaults({
            address: profile.address || "",
            lastOrderAddress: profile.lastOrderAddress || "",
            lastOrderMobileNumber: profile.lastOrderMobileNumber || "",
          });
          setSavedAddresses(Array.isArray(profile.addressBook) ? profile.addressBook : []);
          setSelectedAddress(defaultAddress || "");
          setFormData((prev) => ({
            ...prev,
            firstName: profile.firstName || "",
            lastName: profile.lastName || "",
            mobileNumber: defaultMobile,
            address: defaultAddress,
          }));
          return;
        }

        // input form gamit user data
        if (session?.user) {
          const fullName = session.user.name || "";
          const names = fullName.split(" ").filter(Boolean);
          setFormData((prev) => ({
            ...prev,
            firstName: session.user.firstName || names[0] || "",
            lastName: session.user.lastName || names.slice(1).join(" ") || "",
            mobileNumber: session.user.mobileNumber || "",
            address: session.user.address || "",
          }));
        }
      } catch (err) {
        console.error("Failed to load data:", err);
      }
    };

    loadData();
  }, []);

  const handleInputChange = (e) => {
    const { name, value } = e.target;
    if (name === "address") {
      setSelectedAddress("");
    }
    setFormData((prev) => ({
      ...prev,
      [name]: value,
    }));
  };

  const addressOptions = useMemo(() => {
    const options = [];
    const seen = new Set();

    const pushOption = (label, value) => {
      const clean = String(value || "").trim();
      const key = clean.toLowerCase();
      if (!clean || seen.has(key)) {
        return;
      }
      seen.add(key);
      options.push({ label, value: clean });
    };

    if (profileDefaults.address) {
      pushOption(`Default: ${profileDefaults.address}`, profileDefaults.address);
    }

    if (profileDefaults.lastOrderAddress && profileDefaults.lastOrderAddress !== profileDefaults.address) {
      pushOption(`Last used: ${profileDefaults.lastOrderAddress}`, profileDefaults.lastOrderAddress);
    }

    savedAddresses.forEach((entry) => pushOption(`Saved: ${entry}`, entry));

    return options;
  }, [profileDefaults, savedAddresses]);

  const handleApplyCoupon = async () => {
    const normalizedCode = couponCode.trim();
    const isValidFormat = /^[A-Z0-9]{4,12}$/.test(normalizedCode);

    if (!normalizedCode) {
      setCouponError("Please enter a coupon code");
      return;
    }

    if (!isValidFormat) {
      setCouponError("Coupon codes are case-sensitive. Use uppercase letters and numbers only.");
      return;
    }

    try {
      const response = await fetch("/api/coupons/validate", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ code: normalizedCode, orderAmount: subtotal }),
      });

      const result = await response.json();

      if (result.valid) {
        setAppliedCoupon(result.coupon);
        setDiscountAmount(result.discountAmount);
        setCouponError("");
      } else {
        setCouponError(result.error);
        setAppliedCoupon(null);
        setDiscountAmount(0);
      }
    } catch (_err) {
      setCouponError("Error validating coupon");
    }
  };

  const subtotal = useMemo(
    () => cartItems.reduce((total, item) => total + item.price * item.quantity, 0),
    [cartItems]
  );

  const total = subtotal - discountAmount;

  function handlePlaceOrder() {
    if (cartItems.length === 0) {
      errorNotif("Cart is empty");
      return;
    }

    if (!formData.firstName || !formData.lastName || !formData.mobileNumber || !formData.address) {
      errorNotif("Please fill in all contact details, search or pin your delivery address, and add any delivery note if needed.");
      return;
    }

    if (!isValidMobileNumber(formData.mobileNumber)) {
      errorNotif("Please enter a valid mobile number (09XXXXXXXXX or +639XXXXXXXXX).");
      return;
    }

    setOrderConfirm(true);
  }

  async function confirmPlaceOrder() {
    setLoading(true);
    try {
      const session = getAuthSession();
      const token = session?.token;

      if (!token) {
        errorNotif("Please log in to place an order");
        navigate("/login");
        return;
      }

      const customerFullName = `${formData.firstName} ${formData.lastName}`;
      const normalizedMobileNumber = normalizeMobileNumber(formData.mobileNumber);

      if (!isValidMobileNumber(normalizedMobileNumber)) {
        errorNotif("Please enter a valid mobile number (09XXXXXXXXX or +639XXXXXXXXX).");
        setOrderConfirm(false);
        return;
      }

      // Create order
      const response = await fetch("/api/orders", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          items: cartItems,
          customerName: customerFullName,
          paymentMethod,
          couponCode: appliedCoupon?.code || null,
          discount: discountAmount,
          address: formData.address,
          note: formData.note,
          mobileNumber: normalizedMobileNumber,
        }),
      });

      if (response.ok) {
        const order = await response.json();
        success("Order placed successfully!");
        setOrderConfirm(false);

        await fetch("/api/cart", {
          method: "DELETE",
          headers: { Authorization: `Bearer ${token}` },
        });

        setFormData({
          firstName: "",
          lastName: "",
          mobileNumber: "",
          address: "",
          note: "",
        });
        setCouponCode("");
        setAppliedCoupon(null);
        setDiscountAmount(0);
        setCouponError("");

        navigate(`/order-confirmation/${order.id}`);
      } else {
        const error = await response.json();
        errorNotif(error.message || "Failed to place order");
        setOrderConfirm(false);
      }
    } catch (err) {
      console.error("Error placing order:", err);
      errorNotif("Error: " + err.message);
      setOrderConfirm(false);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="checkout-view client-page checkout-page">
      <div className="client-shell">
        <header className="page-topbar">
          <div>
            <h1>Checkout</h1>
            <p>Confirm details, payment, and delivery in one place.</p>
          </div>
          <div className="page-actions">
            <button type="button" className="client-btn ghost" onClick={() => navigate(-1)}>
              Back
            </button>
          </div>
        </header>

        <div className="checkout-grid">
          <section className="checkout-main">
            <article className="panel-card checkout-panel">
              <div className="checkout-section-header">
                <h2>Contact Details</h2>
                <span>Step 1</span>
              </div>

              <div className="checkout-name-row">
                <label>
                  <span>First Name</span>
                  <input
                    type="text"
                    name="firstName"
                    value={formData.firstName}
                    onChange={handleInputChange}
                    placeholder="Enter your first name"
                  />
                </label>
                <label>
                  <span>Last Name</span>
                  <input
                    type="text"
                    name="lastName"
                    value={formData.lastName}
                    onChange={handleInputChange}
                    placeholder="Enter your last name"
                  />
                </label>
              </div>

              <label className="checkout-single-input">
                <span>Mobile Number</span>
                <input
                  type="tel"
                  name="mobileNumber"
                  value={formData.mobileNumber}
                  onChange={handleInputChange}
                  inputMode="numeric"
                  pattern="^(09\\d{9}|\\+639\\d{9}|639\\d{9})$"
                  title="Use 09XXXXXXXXX or +639XXXXXXXXX"
                  maxLength={13}
                  placeholder="Enter your mobile number"
                />
              </label>
            </article>

            <article className="panel-card checkout-panel">
              <div className="checkout-section-header">
                <h2>Delivery</h2>
                <span>Step 2</span>
              </div>

              <div className="checkout-delivery-grid">
                <div className="checkout-delivery-form">
                  {addressOptions.length > 0 && (
                    <label className="checkout-single-input">
                      <span>Saved Addresses</span>
                      <select
                        value={selectedAddress}
                        onChange={(event) => {
                          const value = event.target.value;
                          setSelectedAddress(value);
                          if (value) {
                            setFormData((prev) => ({
                              ...prev,
                              address: value,
                            }));
                          }
                        }}
                      >
                        <option value="">Add a new address</option>
                        {addressOptions.map((option) => (
                          <option key={option.value} value={option.value}>
                            {option.label}
                          </option>
                        ))}
                      </select>
                    </label>
                  )}
                  <p className="checkout-field-label">Delivery Address</p>
                  <textarea
                    className="checkout-address"
                    name="address"
                    value={formData.address}
                    onChange={handleInputChange}
                    placeholder="Enter your delivery address"
                  />

                  <p className="checkout-field-label">Delivery Note</p>
                  <textarea
                    className="checkout-address checkout-note"
                    name="note"
                    value={formData.note}
                    onChange={handleInputChange}
                    placeholder="Add delivery instructions, landmarks, or special requests"
                  />
                </div>

                <div className="checkout-map-card">
                  <div className="checkout-map-header">
                    <strong>Delivery Map</strong>
                    <span>Pin it</span>
                  </div>
                  <DeliveryMapPicker
                    value={formData.address}
                    onChange={(address) =>
                      {
                        setSelectedAddress("");
                        setFormData((prev) => ({
                          ...prev,
                          address,
                        }));
                      }
                    }
                  />
                  <p className="checkout-map-caption">Search a place, tap the map, or drag the pin to auto-fill the address.</p>
                </div>
              </div>
            </article>
          </section>

          <aside className="checkout-aside">
            <article className="panel-card checkout-panel">
              <div className="checkout-section-header">
                <h2>Payment</h2>
                <span>Step 3</span>
              </div>

              <div className="checkout-payment-row">
                <button
                  type="button"
                  className={`checkout-payment ${paymentMethod === "cash" ? "active" : ""}`}
                  onClick={() => setPaymentMethod("cash")}
                >
                  <strong>Cash</strong>
                  <span>Pay on delivery</span>
                </button>

                <button
                  type="button"
                  className={`checkout-payment ${paymentMethod === "card" ? "active" : ""}`}
                  onClick={() => setPaymentMethod("card")}
                >
                  <strong>Credit Card</strong>
                  <span>Online payment</span>
                </button>
              </div>

              <div className="checkout-coupon">
                <div className="checkout-section-header compact">
                  <h3>Discount / Coupon</h3>
                </div>
                <div className="checkout-coupon-row">
                  <input
                    placeholder="Enter code (ex: SAVE10)"
                    value={couponCode}
                    onChange={(event) => setCouponCode(event.target.value)}
                  />
                  <button type="button" onClick={handleApplyCoupon}>Apply</button>
                </div>
                {couponError && <p className="checkout-coupon-error">{couponError}</p>}
                {appliedCoupon && (
                  <div className="checkout-coupon-success">
                    <strong>✓ Coupon Applied!</strong>
                    <p>{appliedCoupon.description}</p>
                    <p className="checkout-coupon-savings">-{formatPrice(discountAmount)}</p>
                  </div>
                )}
              </div>
            </article>

            <article className="panel-card checkout-summary-card">
              <div className="checkout-section-header">
                <h2>Order Summary</h2>
                <span>{cartItems.length} item{cartItems.length === 1 ? "" : "s"}</span>
              </div>

              <div className="checkout-summary-list">
                {!cartItems.length && <p className="checkout-empty">No items in bag yet.</p>}

                {cartItems.map((item) => (
                  <div key={cartItemKey(item)} className="checkout-summary-item">
                    <div>
                      <h3>
                        x{item.quantity} {item.name}
                      </h3>
                      <p>
                        {item.selectedChoice || item.selectedDrink || "Meal option"}
                        {item.selectedSize ? ` ${item.selectedSize}` : ""}
                      </p>
                    </div>
                    <strong>{formatPrice(item.price * item.quantity)}</strong>
                  </div>
                ))}
              </div>

              <div className="checkout-summary-total">
                <div>
                  <span>Subtotal</span>
                  <span>{formatPrice(subtotal)}</span>
                </div>
                {discountAmount > 0 && (
                  <div className="checkout-discount">
                    <span>Discount</span>
                    <span>-{formatPrice(discountAmount)}</span>
                  </div>
                )}
                <div className="checkout-summary-total-row">
                  <strong>Total</strong>
                  <strong>{formatPrice(total)}</strong>
                </div>
              </div>

              <button
                type="button"
                className="client-btn primary checkout-place-order"
                disabled={!cartItems.length || loading}
                onClick={handlePlaceOrder}
              >
                {loading ? "Placing Order..." : "Place Order"}
              </button>
            </article>
          </aside>
        </div>

        {orderConfirm && (
          <div className="cart-popup-backdrop" role="presentation" onClick={() => setOrderConfirm(false)}>
            <section
              className="panel-card cart-popup"
              role="dialog"
              aria-modal="true"
              aria-label="Confirm order placement"
              onClick={(event) => event.stopPropagation()}
            >
              <div>
                <h2>Confirm Order</h2>
                <p>Are you sure you want to place this order for {formatPrice(total)}?</p>
              </div>
              <div className="cart-popup-actions">
                <button type="button" className="client-btn ghost" onClick={() => setOrderConfirm(false)}>
                  Cancel
                </button>
                <button type="button" className="client-btn" onClick={confirmPlaceOrder} disabled={loading}>
                  {loading ? "Placing..." : "Confirm Order"}
                </button>
              </div>
            </section>
          </div>
        )}
      </div>
    </div>
  );
}
