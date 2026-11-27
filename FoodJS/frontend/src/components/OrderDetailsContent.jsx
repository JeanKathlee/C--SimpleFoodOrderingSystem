import { orderDate, orderMoney } from "../lib/orders";
import OrderItemsList from "./OrderItemsList";
import Pagination from "./Pagination";
import usePagination from "../hooks/usePagination";

export default function OrderDetailsContent({ order }) {
  const historyPager = usePagination(order.statusHistory || [], 10, order.id);
  const quantity = order.itemCount ?? order.items.reduce((sum, item) => sum + item.quantity, 0);
  return <div className="order-details-content">
    <dl className="order-detail-grid">
      {[["Customer", order.customerName], ["Mobile", order.mobileNumber || "Not provided"], ["Status", order.status], ["Payment method", order.paymentMethod], ["Placed", orderDate(order.createdAt)], ["Delivery address", order.address || "Not provided"], ["Delivery note", order.note || "None"]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}
    </dl>
    <h3>Ordered items · {quantity} {quantity === 1 ? "item" : "items"}</h3><OrderItemsList items={order.items} />
    <dl className="order-breakdown"><div><dt>Subtotal</dt><dd>{orderMoney(order.subtotal)}</dd></div><div><dt>Discount{order.couponCode ? ` (${order.couponCode})` : ""}</dt><dd>−{orderMoney(order.discount)}</dd></div><div><dt>Total</dt><dd>{orderMoney(order.total)}</dd></div></dl>
    <h3>Status history</h3><ol className="order-status-history" start={(historyPager.page - 1) * historyPager.pageSize + 1}>{historyPager.pageItems.map((entry, index) => <li key={`${entry.changedAt}-${index}`}><strong>{entry.toStatus}</strong><time dateTime={entry.changedAt}>{orderDate(entry.changedAt)}</time></li>)}</ol>
    {historyPager.totalCount > 10 && <Pagination {...historyPager} label="Status changes" />}
    {!order.statusHistory?.length && <p>No status history is available.</p>}
  </div>;
}
