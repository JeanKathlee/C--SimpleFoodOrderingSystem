import usePagination from "../hooks/usePagination";
import Pagination from "./Pagination";
import { orderMoney } from "../lib/orders";
import { cartItemKey } from "../lib/cartItems";

export default function OrderItemsList({ items, label = "Order items" }) {
  const pager = usePagination(items);
  return <div className="order-items-list"><ul className="order-detail-items" aria-label={label}>
    {pager.pageItems.map(item => <li key={cartItemKey(item)}><div><strong>{item.name}</strong>{item.selectedChoice && <p>{item.selectedChoice}</p>}<small>{item.quantity} × {orderMoney(item.price)}</small></div><strong>{orderMoney(item.price * item.quantity)}</strong></li>)}
  </ul>{items.length > 10 && <Pagination {...pager} label="Item types" />}</div>;
}
