const NAV_ITEMS = [
  { key: "dashboard", label: "Dashboard" },
  { key: "manage-orders", label: "Manage Orders" },
  { key: "customers", label: "Customers" },
  { key: "menu", label: "Menu" },
  { key: "categories", label: "Categories" },
  { key: "coupons", label: "Coupons" },
];

export default function AdminSidebar({ activeSection, onSectionChange }) {
  return (
    <aside className="admin-sidebar">
      <div className="admin-brand">
        <span className="admin-brand-dot" aria-hidden="true" />
        <div>
          <p className="admin-brand-title">FoodOps</p>
          <p className="admin-brand-subtitle">Admin Features</p>
        </div>
      </div>

      <nav className="admin-nav">
        {NAV_ITEMS.map((item) => (
          <button
            key={item.key}
            type="button"
            className={`admin-nav-item ${
              activeSection === item.key ? "active" : ""
            }`}
            onClick={() => onSectionChange(item.key)}
          >
            {item.label}
          </button>
        ))}
      </nav>
    </aside>
  );
}
