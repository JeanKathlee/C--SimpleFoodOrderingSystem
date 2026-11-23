import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { formatPrice } from "../data/menuItems";
import { getAuthSession } from "../lib/auth";
import Pagination from "../components/Pagination";
import usePagination from "../hooks/usePagination";

export default function OrderHistory() {
  const navigate = useNavigate();
  const [orders, setOrders] = useState([]);
  const [loading, setLoading] = useState(true);
  const [searchId, setSearchId] = useState("");
  const [searchName, setSearchName] = useState("");
  const [filterStatus, setFilterStatus] = useState("All");
  const [filterDateFrom, setFilterDateFrom] = useState("");
  const [filterDateTo, setFilterDateTo] = useState("");
  const [notifications, setNotifications] = useState([]);
  const [noticesLoading, setNoticesLoading] = useState(true);

  useEffect(() => {
    let isMounted = true;

    const loadOrders = async () => {
      try {
        const session = getAuthSession();
        const token = session?.token;

        if (!token) {
          navigate("/menu");
          return;
        }

        const response = await fetch("/api/orders", {
          headers: { Authorization: `Bearer ${token}` },
        });

        if (response.ok) {
          const data = await response.json();
          if (isMounted) {
            // newest first, whatever order the API sends
            setOrders([...data].sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt)));
          }
        }
      } catch (err) {
        console.error("Failed to load orders:", err);
      } finally {
        if (isMounted) {
          setLoading(false);
        }
      }
    };

    loadOrders();

    const intervalId = setInterval(loadOrders, 10000);
    const handleFocus = () => loadOrders();

    window.addEventListener("focus", handleFocus);

    return () => {
      isMounted = false;
      clearInterval(intervalId);
      window.removeEventListener("focus", handleFocus);
    };
  }, [navigate]);

  useEffect(() => {
    const loadNotifications = async () => {
      try {
        const session = getAuthSession();
        const token = session?.token;

        if (!token) {
          return;
        }

        const response = await fetch("/api/notifications", {
          headers: { Authorization: `Bearer ${token}` },
        });

        if (response.ok) {
          const data = await response.json();
          setNotifications(Array.isArray(data) ? data : []);
        }
      } catch (err) {
        console.error("Failed to load notifications:", err);
      } finally {
        setNoticesLoading(false);
      }
    };

    loadNotifications();
  }, []);

  const filteredOrders = orders.filter((order) => {
    // by order ID
    if (searchId && !order.id.includes(searchId)) {
      return false;
    }

    // by customer name
    if (searchName && !order.customerName.toLowerCase().includes(searchName.toLowerCase())) {
      return false;
    }

    // by status
    if (filterStatus !== "All" && order.status !== filterStatus) {
      return false;
    }

    // by date range
    const orderDate = new Date(order.createdAt);
    if (filterDateFrom) {
      const fromDate = new Date(filterDateFrom);
      if (orderDate < fromDate) return false;
    }
    if (filterDateTo) {
      const toDate = new Date(filterDateTo);
      toDate.setHours(23, 59, 59);
      if (orderDate > toDate) return false;
    }

    return true;
  });

  const pager = usePagination(
    filteredOrders,
    8,
    [searchId, searchName, filterStatus, filterDateFrom, filterDateTo].join("|")
  );

  if (loading) {
    return <div className="page-loading">Loading...</div>;
  }

  return (
    <div className="client-page history-page">
      <div className="client-shell">
        <header className="page-topbar">
          <div>
            <h1>Order History</h1>
            <p>Track past orders, filter by date or status.</p>
          </div>
          <div className="page-actions">
            <button className="client-btn ghost" onClick={() => navigate("/menu")}>← Back</button>
            <button className="client-btn ghost" onClick={() => navigate("/menu")}>Home</button>
          </div>
        </header>

        <div className="history-layout">
          <section className="panel-card filter-card">
            <div className="filter-header">
              <h3>Search & Filter</h3>
              <span>{filteredOrders.length} / {orders.length} orders</span>
            </div>

            <div className="filter-grid">
              <label>
                <span>Order ID</span>
                <input
                  type="text"
                  placeholder="Search by order ID"
                  value={searchId}
                  onChange={(e) => setSearchId(e.target.value)}
                />
              </label>
              <label>
                <span>Customer Name</span>
                <input
                  type="text"
                  placeholder="Search by customer name"
                  value={searchName}
                  onChange={(e) => setSearchName(e.target.value)}
                />
              </label>
              <label>
                <span>Status</span>
                <select value={filterStatus} onChange={(e) => setFilterStatus(e.target.value)}>
                  <option>All</option>
                  <option>Pending</option>
                  <option>Preparing</option>
                  <option>Ready</option>
                  <option>Delivered</option>
                  <option>Cancelled</option>
                </select>
              </label>
              <label>
                <span>From Date</span>
                <input
                  type="date"
                  value={filterDateFrom}
                  onChange={(e) => setFilterDateFrom(e.target.value)}
                />
              </label>
              <label>
                <span>To Date</span>
                <input
                  type="date"
                  value={filterDateTo}
                  onChange={(e) => setFilterDateTo(e.target.value)}
                />
              </label>
            </div>
          </section>

          <aside className="panel-card history-notifications">
            <h3>Status Notifications</h3>
            {noticesLoading ? (
              <p className="muted-text">Loading notifications...</p>
            ) : notifications.length === 0 ? (
              <p className="muted-text">No status updates yet.</p>
            ) : (
              <div className="notification-list">
                {notifications.slice(0, 5).map((notice) => (
                  <div key={notice.id} className={`notification-item ${notice.read ? "" : "unread"}`}>
                    <div>
                      <strong>{notice.title}</strong>
                      <p>{notice.message}</p>
                    </div>
                    <span>{new Date(notice.createdAt).toLocaleDateString()}</span>
                  </div>
                ))}
              </div>
            )}
          </aside>
        </div>

        {filteredOrders.length === 0 ? (
          <section className="panel-card empty-state">
            <h2>{orders.length === 0 ? "No orders yet" : "No orders match your filters"}</h2>
            <p>Try adjusting the filters or start a new order.</p>
            {orders.length === 0 && (
              <button className="client-btn primary" onClick={() => navigate("/menu")}>Start Ordering</button>
            )}
          </section>
        ) : (
          <>
          <section className="history-list">
            {pager.pageItems.map((order) => (
              <article
                key={order.id}
                className="panel-card history-card"
                role="link"
                tabIndex={0}
                aria-label={`Track order ${order.id}`}
                onClick={() => navigate(`/order-tracking/${order.id}`)}
                onKeyDown={(event) => {
                  if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    navigate(`/order-tracking/${order.id}`);
                  }
                }}
              >
                <div>
                  <h3>Order {order.id}</h3>
                  <p>{new Date(order.createdAt).toLocaleString()}</p>
                </div>
                <div className="history-meta">
                  <strong>{formatPrice(order.total)}</strong>
                  <span className={`status-pill ${order.status.toLowerCase()}`}>{order.status}</span>
                  <p>{order.items.length} items • {order.paymentMethod}</p>
                </div>
              </article>
            ))}
          </section>
          <Pagination pager={pager} buttonClass="client-btn" label="orders" />
          </>
        )}
      </div>
    </div>
  );
}
