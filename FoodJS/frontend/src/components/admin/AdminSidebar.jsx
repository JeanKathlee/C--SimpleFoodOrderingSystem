import styles from "../../pages/Dashboard.module.css";
const NAV_ITEMS = [
  { key: "dashboard", label: "Dashboard" },
  { key: "manage-orders", label: "Manage Orders" },
  { key: "customers", label: "Customers" },
  { key: "menu", label: "Menu" },
  { key: "categories", label: "Categories" },
  { key: "coupons", label: "Coupons" },
];

export default function AdminSidebar({ activeSection, onSectionChange, disabled = false }) {
  return (
    <aside className={styles["admin-sidebar"]}>
      <div className={styles["admin-brand"]}>
        <span className={styles["admin-brand-dot"]} aria-hidden="true" />
        <div>
          <p className={styles["admin-brand-title"]}>FoodJS</p>
          <p className={styles["admin-brand-subtitle"]}>Admin Features</p>
        </div>
      </div>

      <nav className={styles["admin-nav"]}>
        {NAV_ITEMS.map((item) => (
          <button
            key={item.key}
            type="button"
            aria-current={activeSection === item.key ? "page" : undefined}
            disabled={disabled}
            className={[styles["admin-nav-item"], activeSection === item.key ? styles.active : ""].filter(Boolean).join(" ")}
            onClick={() => onSectionChange(item.key)}
          >
            {item.label}
          </button>
        ))}
      </nav>
    </aside>
  );
}
