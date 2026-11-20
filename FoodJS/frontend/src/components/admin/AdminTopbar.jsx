export default function AdminTopbar({ onLogout, adminName, syncLabel, disabled = false, title = "Admin Dashboard", subtitle = "Track orders, menu, and categories in one workspace." }) {
  return (
    <header className="admin-topbar">
      <div>
        <h1>{title}</h1>
        <p>{subtitle}</p>
      </div>

      <div className="admin-topbar-actions">
        {syncLabel && <span className="sync-pill">{syncLabel}</span>}
        <span className="admin-avatar">{adminName.slice(0, 1).toUpperCase()}</span>
        <button type="button" className="admin-btn admin-btn-dark" disabled={disabled} onClick={onLogout}>
          Logout
        </button>
      </div>
    </header>
  );
}
