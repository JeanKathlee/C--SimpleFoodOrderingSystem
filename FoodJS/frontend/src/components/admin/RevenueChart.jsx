import styles from "../../pages/Dashboard.module.css";
function formatCompact(value) {
  return new Intl.NumberFormat("en", {
    notation: "compact",
    compactDisplay: "short",
  }).format(value);
}

function getDayLabel(date) {
  const days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
  return days[new Date(date).getDay()];
}

function getDateString(date) {
  const d = new Date(date);
  const year = d.getFullYear();
  const month = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function matchesDay(orderDate, dayDate) {
  const orderStr = getDateString(orderDate);
  const dayStr = getDateString(dayDate);
  return orderStr === dayStr;
}

function generateWeeklyRevenueFromOrders(orders) {
  // Get the last 7 days
  const today = new Date();
  const days = [];

  for (let i = 6; i >= 0; i--) {
    const d = new Date(today);
    d.setDate(d.getDate() - i);
    days.push(d);
  }

  return days.map((date) => {
    const revenue = orders
      .filter((order) => {
        if (order.status === "Cancelled") return false;
        if (!order.total || order.total <= 0) return false;
        return matchesDay(order.createdAt, date);
      })
      .reduce((sum, order) => sum + (order.total || 0), 0);

    return {
      day: getDayLabel(date),
      date: getDateString(date),
      amount: revenue,
    };
  });
}

export default function RevenueChart({ weeklyRevenue, orders = [], dateRange = {} }) {
  const chartData = (() => {
    const hasValidOrders = orders && orders.length > 0 && Array.isArray(orders);
    const hasValidDates = dateRange && dateRange.fromDate && dateRange.toDate;

    if (hasValidOrders && hasValidDates) {
      try {
        const start = new Date(dateRange.fromDate);
        const end = new Date(dateRange.toDate);

        const dateArray = [];
        const current = new Date(start);
        while (current <= end) {
          dateArray.push(new Date(current));
          current.setDate(current.getDate() + 1);
        }

        return dateArray.map((date) => {
          const revenue = orders
            .filter((order) => {
              if (order.status === "Cancelled") return false;
              if (!order.total || order.total <= 0) return false;
              return matchesDay(order.createdAt, date);
            })
            .reduce((sum, order) => sum + (order.total || 0), 0);

          return {
            day: getDayLabel(date),
            date: getDateString(date),
            amount: revenue,
          };
        });
      } catch (error) {
        console.error("Error calculating filtered revenue:", error);
        return generateWeeklyRevenueFromOrders(orders);
      }
    }

    if (hasValidOrders) {
      return generateWeeklyRevenueFromOrders(orders);
    }

    return weeklyRevenue;
  })();

  const maxValue = Math.max(...chartData.map((entry) => entry.amount), 1);

  return (
    <section className={styles["admin-panel"]}>
      <div className={styles["admin-panel-header"]}>
        <h2>Revenue Chart</h2>
        {orders && orders.length > 0 && (
          <span style={{ fontSize: "12px", color: "#666" }}>
            ({orders.length} orders)
          </span>
        )}
      </div>
      <div className={styles["admin-revenue-chart"]}>
        {chartData.map((entry, index) => {
          const height = `${Math.round((entry.amount / maxValue) * 100)}%`;
          return (
            <div className={styles["admin-chart-col"]} key={entry.date || entry.day}>
              <div className={styles["admin-chart-track"]}>
                <div
                  className={styles["admin-chart-bar"]}
                  style={{ height, animationDelay: `${index * 80}ms` }}
                  title={`${entry.day} (${entry.date}): ${entry.amount}`}
                />
              </div>
              <span>{entry.day}</span>
              <strong>{formatCompact(entry.amount)}</strong>
            </div>
          );
        })}
      </div>
    </section>
  );
}
