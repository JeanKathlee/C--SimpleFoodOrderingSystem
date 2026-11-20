import { useId } from "react";
import { ORDER_STATUSES } from "../lib/orders";

export default function OrderFilters({ filters, onChange, onClear }) {
  const id = useId();
  return <div className="order-filters">
    <label htmlFor={`${id}-search`}>Search orders<input id={`${id}-search`} type="search" maxLength={120} placeholder="Order ID or customer name" value={filters.search} onChange={e => onChange("search", e.target.value)} /></label>
    <label htmlFor={`${id}-status`}>Status<select id={`${id}-status`} value={filters.status} onChange={e => onChange("status", e.target.value)}><option value="All">All statuses</option>{ORDER_STATUSES.map(status => <option key={status}>{status}</option>)}</select></label>
    <label htmlFor={`${id}-from`}>From date<input id={`${id}-from`} type="date" max={filters.toDate || "9999-12-30"} value={filters.fromDate} onChange={e => onChange("fromDate", e.target.value)} /></label>
    <label htmlFor={`${id}-to`}>To date<input id={`${id}-to`} type="date" min={filters.fromDate || undefined} max="9999-12-30" value={filters.toDate} onChange={e => onChange("toDate", e.target.value)} /></label>
    <button type="button" className="order-clear" onClick={onClear} disabled={!filters.search && filters.status === "All" && !filters.fromDate && !filters.toDate}>Clear filters</button>
    <p className="order-date-hint">Dates and times are shown in Philippine time (UTC+8).</p>
  </div>;
}
