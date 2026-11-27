import styles from "../../pages/Dashboard.module.css";
function formatCurrency(value) {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    maximumFractionDigits: 0,
  }).format(value);
}

export default function PopularItems({ topItems }) {
  return (
    <section className={styles["admin-panel"]}>
      <div className={styles["admin-panel-header"]}>
        <h2>Top Selling Menu Items</h2>
      </div>
      <ul className={styles["admin-popular-list"]}>
        {topItems.map((item, index) => (
          <li key={item.id}>
            <div>
              <strong>{index + 1}. {item.name}</strong>
              <span>{item.soldCount} sold</span>
            </div>
            <span>{formatCurrency(item.price)}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}
