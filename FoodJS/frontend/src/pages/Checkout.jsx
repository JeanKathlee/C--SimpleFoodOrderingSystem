import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import DeliveryMapPicker from "../components/DeliveryMapPicker";
import OrderItemsList from "../components/OrderItemsList";
import ConfirmModal from "../components/admin/ConfirmModal";
import { orderMoney } from "../lib/orders";
import { getAuthSession } from "../lib/auth";
import { PHONE, normalizePhone, checkoutRequest, quoteRequest, readCheckout, saveCheckout, clearCheckout } from "../lib/checkout";
import "../orders.css";
import "../checkout.css";

const EMPTY = { firstName: "", lastName: "", mobileNumber: "", address: "", note: "" };
export default function Checkout() {
  const navigate = useNavigate();
  const [initial] = useState(readCheckout);
  const [pending, setPending] = useState(initial?.status === "pending" ? initial : null);
  const [form, setForm] = useState(initial?.form || EMPTY);
  const [cart, setCart] = useState(initial?.cart || []);
  const [cartLoading, setCartLoading] = useState(true);
  const [profileLoading, setProfileLoading] = useState(true);
  const [cartError, setCartError] = useState("");
  const [profileError, setProfileError] = useState("");
  const [addresses, setAddresses] = useState([]);
  const [selectedAddress, setSelectedAddress] = useState("");
  const [version, setVersion] = useState(0);
  const [errors, setErrors] = useState({});
  const [error, setError] = useState("");
  const [couponInput, setCouponInput] = useState(initial?.body?.couponCode || "");
  const [couponCode, setCouponCode] = useState(initial?.body?.couponCode || "");
  const [couponError, setCouponError] = useState("");
  const [prices, setPrices] = useState(initial?.quote || null);
  const [quoting, setQuoting] = useState(false);
  const [preparing, setPreparing] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [review, setReview] = useState(null);
  const [mapOpen, setMapOpen] = useState(false);
  const dirty = useRef(new Set(initial ? Object.keys(EMPTY) : []));
  const quoteController = useRef(null);
  const quoteGeneration = useRef(0);
  const submitLock = useRef(false);
  const formRef = useRef(null);
  useEffect(() => {
    if (initial?.status === "accepted") navigate(`/order-confirmation/${initial.orderId}`, { replace: true, state: { acceptedCheckout: initial } });
  }, [initial, navigate]);
  useEffect(() => {
    const controller = new AbortController(); const signal = controller.signal;
    setCartLoading(true); setProfileLoading(true);
    checkoutRequest("/api/cart", { signal }).then(body => {
      if (!Array.isArray(body)) throw new Error("The bag response was incomplete. Please retry.");
      if (!signal.aborted) { setCart(body); setCartError(""); }
    }).catch(failure => { if (!signal.aborted) setCartError(failure.message); }).finally(() => { if (!signal.aborted) setCartLoading(false); });
    checkoutRequest("/api/users/me", { signal }).then(profile => {
      if (signal.aborted) return;
      const defaults = { firstName: profile.firstName || "", lastName: profile.lastName || "", mobileNumber: profile.mobileNumber || profile.lastOrderMobileNumber || "", address: profile.address || profile.lastOrderAddress || "" };
      setForm(current => Object.fromEntries(Object.entries({ ...current, ...defaults }).map(([key, value]) => [key, dirty.current.has(key) ? current[key] : value])));
      const saved = [profile.address, profile.lastOrderAddress, ...(Array.isArray(profile.addressBook) ? profile.addressBook : [])].filter(value => typeof value === "string" && value.trim()).map(value => value.trim());
      setAddresses(saved.filter((value, index) => saved.findIndex(other => other.toLowerCase() === value.toLowerCase()) === index));
      if (!dirty.current.has("address")) setSelectedAddress(defaults.address);
      setProfileError("");
    }).catch(() => { if (!signal.aborted) setProfileError("Saved contact details are unavailable. You can enter your details below or retry loading them."); }).finally(() => { if (!signal.aborted) setProfileLoading(false); });
    return () => controller.abort();
  }, [version]);
  useEffect(() => () => quoteController.current?.abort(), []);
  const update = useCallback((field, value) => {
    dirty.current.add(field); setForm(current => ({ ...current, [field]: value }));
    setErrors(current => ({ ...current, [field]: undefined })); setError("");
    if (field === "address") setSelectedAddress("");
  }, []);
  const updateMapAddress = useCallback(value => update("address", value), [update]);
  const editCoupon = value => {
    quoteGeneration.current++; quoteController.current?.abort(); setQuoting(false);
    setCouponInput(value.toUpperCase()); setCouponCode(""); setPrices(null); setCouponError("");
  };
  const applyCoupon = async () => {
    if (quoting || !cart.length) return;
    const code = couponInput.trim();
    if (!/^[A-Z0-9]{4,12}$/.test(code)) { setCouponError("Use 4–12 uppercase letters or numbers, or clear the code."); return; }
    const generation = ++quoteGeneration.current; quoteController.current?.abort(); const controller = new AbortController(); quoteController.current = controller;
    setQuoting(true); setCouponError("");
    try {
      const body = await quoteRequest(cart, code, controller.signal);
      if (generation === quoteGeneration.current && !controller.signal.aborted) { setPrices(body); setCouponCode(code); }
    } catch (failure) { if (!controller.signal.aborted && generation === quoteGeneration.current) { setCouponError(failure.message); setCouponCode(""); setPrices(null); } }
    finally { if (generation === quoteGeneration.current) setQuoting(false); }
  };
  const prepareReview = async event => {
    event.preventDefault(); if (preparing || submitting || pending || quoting || cartLoading || profileLoading || cartError || !cart.length) return;
    const next = {};
    if (!form.firstName.trim()) next.firstName = "Enter the recipient’s first name.";
    if (!form.lastName.trim()) next.lastName = "Enter the recipient’s last name.";
    if (`${form.firstName.trim()} ${form.lastName.trim()}`.length > 120) next.firstName = "Use up to 120 characters for the full name.";
    if (!PHONE.test(normalizePhone(form.mobileNumber))) next.mobileNumber = "Use 09XXXXXXXXX or +639XXXXXXXXX.";
    if (!form.address.trim()) next.address = "Enter or choose a delivery address.";
    setErrors(next);
    if (Object.keys(next).length) { requestAnimationFrame(() => formRef.current?.querySelector('[aria-invalid="true"]')?.focus()); return; }
    if (couponInput.trim() !== couponCode) { setCouponError("Apply or clear this code before reviewing your order."); return; }
    setPreparing(true); setError(""); quoteController.current?.abort(); const controller = new AbortController(); quoteController.current = controller;
    try {
      const quote = await quoteRequest(cart, couponCode, controller.signal);
      setPrices(quote); setReview({ quote, form: { ...form }, cart: [...cart] });
    } catch (failure) { if (!controller.signal.aborted) { setError(failure.message); setPrices(null); setCouponCode(""); } }
    finally { setPreparing(false); }
  };
  const submit = async () => {
    if (submitLock.current) return;
    submitLock.current = true; setSubmitting(true); setError("");
    let sent = false;
    try {
      const snapshot = pending || (() => {
        const key = crypto.randomUUID(); const recipient = review.form;
        return { key, userId: getAuthSession()?.user?.id, status: "pending", form: recipient, cart: review.cart, quote: review.quote, body: {
          idempotencyKey: key, quoteToken: review.quote.quoteToken,
          items: review.quote.items.map(({ menuItemId, quantity, selectedChoice }) => ({ menuItemId, quantity, selectedChoice })),
          customerName: `${recipient.firstName.trim()} ${recipient.lastName.trim()}`, mobileNumber: normalizePhone(recipient.mobileNumber),
          address: recipient.address.trim(), note: recipient.note.trim(), paymentMethod: "cash", couponCode: review.quote.couponCode || null
        } };
      })();
      try { saveCheckout(snapshot); } catch { throw new Error("Checkout recovery details could not be saved. Allow session storage in this browser before placing an order."); }
      setPending(snapshot); sent = true;
      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), 20000);
      let order;
      try { order = await checkoutRequest("/api/orders", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(snapshot.body), signal: controller.signal }); }
      finally { clearTimeout(timer); }
      if (!order.id) throw new Error("The order response was incomplete. Retry the same submission safely.");
      const accepted = { ...snapshot, status: "accepted", orderId: order.id };
      try { saveCheckout(accepted); } catch { /* The pending key still makes a retry safe. */ }
      navigate(`/order-confirmation/${order.id}`, { replace: true, state: { acceptedCheckout: accepted } });
    } catch (failure) {
      if (!sent || (failure.status >= 400 && failure.status < 500)) { clearCheckout(); setPending(null); setReview(null); setPrices(null); setCouponCode(""); setError(failure.message); }
      else setError("The result could not be confirmed. Retry the same submission safely below, or check your order history. Your details are preserved.");
      throw new Error(failure.status ? failure.message : "The result is uncertain. Retry this same submission safely; do not start another order.");
    } finally { submitLock.current = false; setSubmitting(false); }
  };
  const locked = preparing || submitting || !!pending || !!review;
  const display = review?.quote || prices;
  const items = display?.items || cart;
  const subtotal = display?.subtotal ?? cart.reduce((sum, item) => sum + Number(item.price) * Number(item.quantity), 0);
  const total = display?.total ?? subtotal;
  const itemCount = items.reduce((sum, item) => sum + Number(item.quantity), 0);
  const field = name => ({ id: `checkout-${name}`, name, value: form[name], onChange: event => update(name, event.target.value), "aria-invalid": !!errors[name], "aria-describedby": errors[name] ? `checkout-${name}-error` : undefined });
  const fieldError = name => errors[name] && <small id={`checkout-${name}-error`} className="checkout-field-error">{errors[name]}</small>;
  const frozen = review || (pending && { quote: pending.quote, form: pending.form });
  return <div className="client-page checkout-page checkout-revised"><div className="client-shell">
    <header className="page-topbar"><div><h1>Checkout</h1><p>Review your delivery details and current total before ordering.</p></div>{!locked && <Link className="client-btn ghost" to="/cart">Back to bag</Link>}</header>
    {cartError && <div className="order-notice" role="alert"><p>{cartError}</p><button type="button" disabled={cartLoading || locked} onClick={() => setVersion(value => value + 1)}>Retry checkout data</button></div>}
    {profileError && <div className="order-notice" role="status"><p>{profileError}</p><button type="button" disabled={profileLoading || locked} onClick={() => setVersion(value => value + 1)}>Retry saved details</button></div>}
    {error && <p className="order-action-error" role="alert">{error}</p>}
    {pending && <section className="panel-card checkout-recovery" aria-label="Pending checkout"><h2>Check your previous submission</h2><p>Retry checks your previous attempt and shows the existing order if it was accepted. It will not place another copy.</p><button type="button" className="client-btn primary" disabled={submitting} onClick={() => submit().catch(() => {})}>{submitting ? "Checking order…" : "Retry same submission safely"}</button><Link className="client-btn ghost" to="/order-history">Check order history</Link></section>}
    {cartLoading && <p role="status">Loading your bag…</p>}{profileLoading && <p role="status">Loading saved contact details…</p>}
    {!cartLoading && !cartError && !cart.length && !pending ? <section className="panel-card order-empty"><h2>Your bag is empty</h2><p>Add something from the menu before checking out.</p><Link className="client-btn primary" to="/menu">Browse menu</Link></section> : <form ref={formRef} className="checkout-grid" onSubmit={prepareReview} noValidate>
      <fieldset className="checkout-fields" disabled={locked}><legend className="order-sr-only">Delivery and contact details</legend>
        <section className="panel-card"><h2>Contact details</h2><p className="checkout-hint">Fields marked * are required.</p><div className="checkout-name-row">
          <label htmlFor="checkout-firstName">First name *<input {...field("firstName")} required maxLength={60} autoComplete="given-name" />{fieldError("firstName")}</label>
          <label htmlFor="checkout-lastName">Last name *<input {...field("lastName")} required maxLength={60} autoComplete="family-name" />{fieldError("lastName")}</label>
        </div><label className="checkout-single-input" htmlFor="checkout-mobileNumber">Mobile number *<input {...field("mobileNumber")} type="tel" inputMode="tel" autoComplete="tel" required maxLength={32} pattern={PHONE.source} placeholder="09XXXXXXXXX or +639XXXXXXXXX" />{fieldError("mobileNumber")}</label></section>
        <section className="panel-card"><h2>Delivery</h2>{!!addresses.length && <label className="checkout-single-input" htmlFor="checkout-saved">Saved address<select id="checkout-saved" value={selectedAddress} onChange={event => { const value = event.target.value; update("address", value); setSelectedAddress(value); }}><option value="">Add a new address</option>{addresses.map(address => <option key={address}>{address}</option>)}</select></label>}
          <label className="checkout-single-input" htmlFor="checkout-address">Delivery address *<textarea {...field("address")} required maxLength={2000} rows={3} autoComplete="street-address" placeholder="Street, building, unit, and city" />{fieldError("address")}</label>
          <label className="checkout-single-input" htmlFor="checkout-note">Delivery note <span className="checkout-hint">Optional · Up to 1000 characters</span><textarea {...field("note")} maxLength={1000} rows={3} placeholder="Landmark or delivery instructions" /></label>
          <button type="button" className="client-btn ghost checkout-map-toggle" aria-expanded={mapOpen} onClick={() => setMapOpen(value => !value)}>{mapOpen ? "Hide map" : "Choose address on map (optional)"}</button>
          {mapOpen && <DeliveryMapPicker value={form.address} onChange={updateMapAddress} />}
        </section>
      </fieldset>
      <aside className="checkout-aside"><section className="panel-card"><h2>Payment</h2><fieldset className="checkout-payment-options" disabled={locked}><legend className="order-sr-only">Payment method</legend><label><input type="radio" name="payment" value="cash" checked readOnly />Cash on delivery</label><label className="checkout-disabled-payment"><input type="radio" name="payment" disabled aria-describedby="checkout-card-help" />Credit card</label><p id="checkout-card-help" className="checkout-hint">Card payments are not available yet. No online payment will be collected.</p></fieldset>
        <div className="checkout-coupon-block"><label htmlFor="checkout-coupon">Coupon code <span className="checkout-hint">Optional</span></label><div className="checkout-coupon-actions"><input id="checkout-coupon" value={couponInput} maxLength={12} autoComplete="off" aria-describedby={couponError ? "checkout-coupon-error" : undefined} aria-invalid={!!couponError} disabled={locked} placeholder="e.g. SAVE10" onChange={event => editCoupon(event.target.value)} /><button type="button" className="client-btn ghost" disabled={locked || quoting || cartLoading || !!cartError || !cart.length} onClick={applyCoupon}>{quoting ? "Checking…" : "Apply"}</button></div>
          {couponError && <p className="checkout-field-error" id="checkout-coupon-error" role="alert">{couponError}</p>}{couponCode && <p className="checkout-applied" role="status">{couponCode} applied · Save {orderMoney(prices?.discount)}</p>}{!!couponInput && <button type="button" className="order-clear" disabled={locked} onClick={() => editCoupon("")}>{couponCode ? "Remove coupon" : "Clear code"}</button>}
        </div></section>
        <section className="panel-card checkout-summary"><div className="checkout-summary-heading"><h2>Order summary</h2><span>{items.length ? `${itemCount} ${itemCount === 1 ? "item" : "items"}` : cartLoading ? "Loading…" : "Unavailable"}</span></div><OrderItemsList items={items} label="Checkout items" />
          {!!items.length && <dl className="order-breakdown"><div><dt>Subtotal</dt><dd>{orderMoney(subtotal)}</dd></div>{display?.discount > 0 && <div><dt>Discount ({display.couponCode})</dt><dd>−{orderMoney(display.discount)}</dd></div>}<div><dt>{display ? "Current total" : "Estimated total"}</dt><dd>{orderMoney(total)}</dd></div></dl>}
          <p className="checkout-hint">{display ? "Menu prices and discounts were checked by the server. You’ll review this total before placing the order." : "Current menu prices and availability will be checked before you confirm."}</p>
          <button type="submit" className="client-btn primary checkout-place-order" disabled={locked || quoting || cartLoading || profileLoading || !!cartError || !cart.length}>{preparing ? "Checking current total…" : "Review order"}</button>
        </section></aside>
    </form>}
    <ConfirmModal open={!!review} title="Review and place your order" message="Check the recipient, delivery address, and current total." confirmLabel={pending ? "Retry same submission safely" : "Place order"} cancelLabel="Back to checkout" confirmVariant="primary" dialogClassName="order-details-dialog" onCancel={() => setReview(null)} onConfirm={submit}>
      {frozen && <><dl className="order-detail-grid"><div><dt>Recipient</dt><dd>{frozen.form.firstName.trim()} {frozen.form.lastName.trim()}</dd></div><div><dt>Mobile</dt><dd>{normalizePhone(frozen.form.mobileNumber)}</dd></div><div><dt>Delivery address</dt><dd>{frozen.form.address}</dd></div><div><dt>Payment</dt><dd>Cash on delivery</dd></div>{frozen.form.note && <div><dt>Delivery note</dt><dd>{frozen.form.note}</dd></div>}</dl><OrderItemsList items={frozen.quote.items} label="Reviewed items" /><dl className="order-breakdown"><div><dt>Subtotal</dt><dd>{orderMoney(frozen.quote.subtotal)}</dd></div><div><dt>Discount{frozen.quote.couponCode ? ` (${frozen.quote.couponCode})` : ""}</dt><dd>−{orderMoney(frozen.quote.discount)}</dd></div><div><dt>Total to pay on delivery</dt><dd>{orderMoney(frozen.quote.total)}</dd></div></dl></>}
    </ConfirmModal>
  </div></div>;
}
