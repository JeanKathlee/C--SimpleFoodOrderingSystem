import Pagination from "../Pagination";
import usePagination from "../../hooks/usePagination";

function formatDate(value) {
  return new Date(value).toLocaleString("en-PH", {
    timeZone: "Asia/Manila",
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function formatCurrency(value) {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
}

function getNextStatusOptions(currentStatus) {
  const transitionMap = {
    Pending: ["Preparing", "Cancelled"],
    Preparing: ["Ready", "Cancelled"],
    Ready: ["Delivered"],
    Delivered: [],
    Cancelled: [],
  };

  return transitionMap[currentStatus] || [];
}

export default function OrdersPanel({
  orders,
  statusOptions,
  filters,
  onFilterChange,
  title = "Order Management",
  showFilters = true,
  isAdmin = false,
  onUpdateStatus,
  onDeleteOrder,
}) {
  const pager = usePagination(orders, 10, JSON.stringify(filters));

  return (
    <section className="admin-panel">
      <div className="admin-panel-header">
        <h2>{title}</h2>
      </div>

      {showFilters ? (
        <div className="admin-order-filters">
          <label>
            <span>From</span>
            <input
              type="date"
              value={filters.fromDate}
              onChange={(event) => onFilterChange("fromDate", event.target.value)}
            />
          </label>

          <label>
            <span>To</span>
            <input
              type="date"
              value={filters.toDate}
              onChange={(event) => onFilterChange("toDate", event.target.value)}
            />
          </label>

          <label>
            <span>Status</span>
            <select
              value={filters.status}
              onChange={(event) => onFilterChange("status", event.target.value)}
            >
              <option value="All">All</option>
              {statusOptions.map((status) => (
                <option value={status} key={status}>
                  {status}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Customer Filter</span>
            <input
              type="text"
              placeholder="Filter by customer"
              value={filters.customer}
              onChange={(event) => onFilterChange("customer", event.target.value)}
            />
          </label>

          <label>
            <span>Search</span>
            <input
              type="text"
              placeholder="Order ID or customer"
              value={filters.search}
              onChange={(event) => onFilterChange("search", event.target.value)}
            />
          </label>
        </div>
      ) : null}

      <div className="admin-table-wrap">
        <table className="admin-table">
          <thead>
            <tr>
              <th>Order ID</th>
              <th>Customer</th>
              <th>Status</th>
              <th>Items</th>
              <th>Total</th>
              <th>Created</th>
              {isAdmin && <th>Update</th>}
              {isAdmin && <th>Delete</th>}
            </tr>
          </thead>
          <tbody>
            {orders.length === 0 ? (
              <tr>
                <td colSpan={isAdmin ? 8 : 6} className="admin-empty-cell">
                  No orders match the selected filters.
                </td>
              </tr>
            ) : (
              pager.pageItems.map((order) => (
                <tr key={order.id}>
                  <td>{order.id}</td>
                  <td>{order.customerName}</td>
                  <td>
                    <span className={`admin-status ${order.status.toLowerCase()}`}>
                      {order.status}
                    </span>
                  </td>
                  <td>{order.itemCount}</td>
                  <td>{formatCurrency(order.total)}</td>
                  <td>{formatDate(order.createdAt)}</td>
                  {isAdmin && (
                    <td>
                      {(() => {
                        const nextOptions = getNextStatusOptions(order.status);
                        const isLocked = nextOptions.length === 0;

                        return (
                      <select
                        value=""
                        disabled={isLocked}
                        aria-label={`Change status of order ${order.id}`}
                        onChange={(e) => {
                          if (e.target.value && onUpdateStatus) {
                            onUpdateStatus(order.id, e.target.value);
                            e.target.value = "";
                          }
                        }}
                        style={{
                          padding: "6px",
                          border: "1px solid #ddd",
                          borderRadius: "4px",
                          cursor: "pointer",
                          fontSize: "12px",
                        }}
                      >
                        <option value="">{isLocked ? "Locked" : "Change..."}</option>
                        {nextOptions.map((nextStatus) => (
                          <option value={nextStatus} key={nextStatus}>
                            {nextStatus}
                          </option>
                        ))}
                      </select>
                        );
                      })()}
                    </td>
                  )}
                  {isAdmin && (
                    <td>
                      <button
                        type="button"
                        className="admin-btn admin-btn-danger admin-table-action"
                        onClick={() => onDeleteOrder && onDeleteOrder(order.id)}
                        aria-label={`Delete order ${order.id}`}
                      >
                        Delete
                      </button>
                    </td>
                  )}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
      <Pagination pager={pager} buttonClass="admin-btn admin-btn-light" label="orders" />
    </section>
  );
}
