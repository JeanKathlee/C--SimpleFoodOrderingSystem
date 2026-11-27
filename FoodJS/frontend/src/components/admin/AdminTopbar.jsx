import styles from "../../pages/Dashboard.module.css";
import Button from "../ui/Button";
export default function AdminTopbar({ onLogout, adminName, syncLabel, disabled = false, title = "Admin Dashboard", subtitle = "Track orders, menu, and categories in one workspace." }) {
  return (
    <header className={styles["admin-topbar"]}>
      <div>
        <h1>{title}</h1>
        <p>{subtitle}</p>
      </div>

      <div className={styles["admin-topbar-actions"]}>
        {syncLabel && <span className={styles["sync-pill"]}>{syncLabel}</span>}
        <span className={styles["admin-avatar"]}>{adminName.slice(0, 1).toUpperCase()}</span>
        <Button variant="dark" type="button" disabled={disabled} onClick={onLogout}>
          Logout
        </Button>
      </div>
    </header>
  );
}
