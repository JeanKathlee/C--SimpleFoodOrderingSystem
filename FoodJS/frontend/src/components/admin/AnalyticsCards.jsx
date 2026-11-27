import styles from "../../pages/Dashboard.module.css";
function formatCurrency(value) {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    maximumFractionDigits: 0,
  }).format(value);
}

export default function AnalyticsCards({ stats, topItems }) {
  return (
    <section className={styles["admin-analytics-grid"]}>
      <article className={styles["admin-stat-card"]}>
        <p>Total Orders (Filtered)</p>
        <h3>{stats.totalOrdersFiltered}</h3>
        <span>{stats.preparingOrders} preparing</span>
      </article>

      <article className={styles["admin-stat-card"]}>
        <p>Revenue (Filtered)</p>
        <h3>{formatCurrency(stats.revenueFiltered)}</h3>
        <span>{stats.cancelledOrders} cancelled</span>
      </article>

      <article className={styles["admin-stat-card"]}>
        <p>Completed Orders</p>
        <h3>{stats.completedOrders}</h3>
        <span>{stats.pendingOrders} pending</span>
      </article>

      <article className={styles["admin-stat-card"]}>
        <p>Popular Item</p>
        <h3>{topItems[0]?.name || "-"}</h3>
        <span>{topItems[0]?.soldCount || 0} sold</span>
      </article>
    </section>
  );
}
