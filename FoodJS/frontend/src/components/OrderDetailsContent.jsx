import { orderDate, orderMoney } from "../lib/orders";
import { cartItemKey } from "../lib/cartItems";

export default function OrderDetailsContent({ order }) {
  return <div className="order-details-content">
    <dl className="order-detail-grid">
      {[["Customer", order.customerName], ["Mobile", order.mobileNumber || "Not provided"], ["Status", order.status], ["Payment method", order.paymentMethod], ["Placed", orderDate(order.createdAt)], ["Delivery address", order.address || "Not provided"], ["Delivery note", order.note || "None"]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}
    </dl>
    <h3>Ordered items</h3><ul className="order-detail-items">{order.items.map(item => <li key={cartItemKey(item)}><div><strong>{item.name}</strong>{item.selectedChoice && <p>{item.selectedChoice}</p>}<small>{item.quantity} × {orderMoney(item.price)}</small></div><strong>{orderMoney(item.price * item.quantity)}</strong></li>)}</ul>
    <dl className="order-breakdown"><div><dt>Subtotal</dt><dd>{orderMoney(order.subtotal)}</dd></div><div><dt>Discount{order.couponCode ? ` (${order.couponCode})` : ""}</dt><dd>−{orderMoney(order.discount)}</dd></div><div><dt>Total</dt><dd>{orderMoney(order.total)}</dd></div></dl>
    <h3>Status history</h3><ol className="order-status-history">{(order.statusHistory || []).map((entry, index) => <li key={`${entry.changedAt}-${index}`}><strong>{entry.toStatus}</strong><time dateTime={entry.changedAt}>{orderDate(entry.changedAt)}</time></li>)}</ol>
    {!order.statusHistory?.length && <p>No status history is available.</p>}
  </div>;
}
