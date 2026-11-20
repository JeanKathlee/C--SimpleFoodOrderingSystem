import { useState } from "react";
import Pagination from "./Pagination";
import { orderMoney } from "../lib/orders";
import { cartItemKey } from "../lib/cartItems";

export default function OrderItemsList({ items, label = "Order items" }) {
  const [requestedPage, setPage] = useState(1);
  const [pageSize, setSize] = useState(10);
  const totalPages = Math.max(1, Math.ceil(items.length / pageSize)); const page = Math.min(requestedPage, totalPages);
  return <div className="order-items-list"><ul className="order-detail-items" aria-label={label}>
    {items.slice((page - 1) * pageSize, page * pageSize).map(item => <li key={cartItemKey(item)}><div><strong>{item.name}</strong>{item.selectedChoice && <p>{item.selectedChoice}</p>}<small>{item.quantity} × {orderMoney(item.price)}</small></div><strong>{orderMoney(item.price * item.quantity)}</strong></li>)}
  </ul>{items.length > 10 && <Pagination page={page} pageSize={pageSize} totalCount={items.length} totalPages={totalPages} label="Item types" onPageChange={setPage} onPageSizeChange={value => { setSize(value); setPage(1); }} />}</div>;
}
