import styles from "./CustomersPanel.module.css";
import modalStyles from "../ui/Modal.module.css";
import adminStyles from "../../pages/Dashboard.module.css";
import Button from "../ui/Button";
import { useEffect, useState } from "react";
import Pagination from "../Pagination";
import usePagination from "../../hooks/usePagination";
function formatDate(value) {
  if (!value) {
    return "Not available";
  }

  return new Date(value).toLocaleDateString("en-PH", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

function formatCurrency(value) {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    maximumFractionDigits: 0,
  }).format(value);
}

export default function CustomersPanel({
  customers,
  orders,
  selectedCustomer,
  onViewCustomer,
  onClose,
  onUpdateCustomer,
}) {
  const [editing, setEditing] = useState(false);
  const [form, setForm] = useState(null);

  const selectedOrders = selectedCustomer
    ? orders.filter((order) => String(order.userId) === String(selectedCustomer.id))
    : [];
  const totalSpent = selectedOrders.reduce((total, order) => total + order.total, 0);
  const customerPager = usePagination(customers, 10);
  const orderPager = usePagination(selectedOrders, 10, selectedCustomer?.id ?? "");

  // Esc closes the profile, like clicking the backdrop
  useEffect(() => {
    if (!selectedCustomer) return undefined;
    const handleKey = (event) => event.key === "Escape" && onClose();
    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, [selectedCustomer, onClose]);

  useEffect(() => {
    setEditing(false);
    setForm(
      selectedCustomer
        ? {
            firstName: selectedCustomer.firstName || "",
            lastName: selectedCustomer.lastName || "",
            email: selectedCustomer.email || "",
            mobileNumber: selectedCustomer.mobileNumber || "",
            address: selectedCustomer.address || "",
          }
        : null
    );
  }, [selectedCustomer]);

  const handleFormChange = (event) => {
    const { name, value } = event.target;
    setForm((current) => ({ ...current, [name]: value }));
  };

  const handleSubmit = async (event) => {
    event.preventDefault();
    const updated = await onUpdateCustomer(selectedCustomer.id, form);
    if (updated) {
      setEditing(false);
    }
  };

  return (
    <>
      <section className={adminStyles["admin-panel"]}>
        <div className={adminStyles["admin-panel-header"]}>
          <div>
            <h2>Customers</h2>
            <p className={adminStyles["admin-panel-subtitle"]}>View customer account details and order history.</p>
          </div>
        </div>

        <div className={adminStyles["admin-table-wrap"]}>
          <table className={adminStyles["admin-table"]}>
            <thead>
              <tr>
                <th>Customer</th>
                <th>Email</th>
                <th>Mobile Number</th>
                <th>Joined</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              {customers.length === 0 ? (
                <tr>
                  <td colSpan="5" className={adminStyles["admin-empty-cell"]}>No customers found.</td>
                </tr>
              ) : (
                customerPager.pageItems.map((customer) => (
                  <tr key={customer.id}>
                    <td>{customer.name || `${customer.firstName} ${customer.lastName}`}</td>
                    <td>{customer.email}</td>
                    <td>{customer.mobileNumber || "Not provided"}</td>
                    <td>{formatDate(customer.createdAt)}</td>
                    <td>
                      <Button variant="primary"
                        type="button"
                        className={adminStyles["admin-table-action"]}
                        onClick={() => onViewCustomer(customer)}
                        aria-label={`View profile of ${customer.name || `${customer.firstName} ${customer.lastName}`}`}
                      >
                        View Profile
                      </Button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        <Pagination {...customerPager} label="Customers" />
      </section>

      {selectedCustomer && (
        <div className={modalStyles.backdrop} role="presentation" onClick={onClose}>
          <div
            className={styles["customer-profile-modal"]}
            role="dialog"
            aria-modal="true"
            aria-label={`Profile for ${selectedCustomer.name}`}
            onClick={(event) => event.stopPropagation()}
          >
            <div className={adminStyles["admin-panel-header"]}>
              <div>
                <h3>{selectedCustomer.name}</h3>
                <p className={adminStyles["admin-panel-subtitle"]}>Customer profile</p>
              </div>
              <div className={adminStyles["admin-item-actions"]}>
                <Button variant="primary"
                  type="button"

                  onClick={() => setEditing((current) => !current)}
                >
                  {editing ? "Cancel Edit" : "Edit Profile"}
                </Button>
                <Button variant="light" type="button" onClick={onClose}>
                  Close
                </Button>
              </div>
            </div>

            {editing ? (
              <form className={styles["customer-profile-form"]} onSubmit={handleSubmit}>
                <label>
                  <span>First name</span>
                  <input name="firstName" value={form?.firstName || ""} onChange={handleFormChange} required />
                </label>
                <label>
                  <span>Last name</span>
                  <input name="lastName" value={form?.lastName || ""} onChange={handleFormChange} required />
                </label>
                <label>
                  <span>Email</span>
                  <input type="email" name="email" value={form?.email || ""} onChange={handleFormChange} required />
                </label>
                <label>
                  <span>Mobile number</span>
                  <input name="mobileNumber" value={form?.mobileNumber || ""} onChange={handleFormChange} />
                </label>
                <label>
                  <span>Address</span>
                  <textarea name="address" value={form?.address || ""} onChange={handleFormChange} />
                </label>
                <div className={modalStyles.actions}>
                  <Button variant="primary" type="submit" >Save Changes</Button>
                </div>
              </form>
            ) : (
              <dl className={styles["customer-profile-details"]}>
                <div><dt>Email</dt><dd>{selectedCustomer.email || "Not provided"}</dd></div>
                <div><dt>Mobile number</dt><dd>{selectedCustomer.mobileNumber || "Not provided"}</dd></div>
                <div><dt>Address</dt><dd>{selectedCustomer.address || "Not provided"}</dd></div>
                <div><dt>Joined</dt><dd>{formatDate(selectedCustomer.createdAt)}</dd></div>
                <div><dt>Total orders</dt><dd>{selectedOrders.length}</dd></div>
                <div><dt>Total spent</dt><dd>{formatCurrency(totalSpent)}</dd></div>
              </dl>
            )}

            {!editing && (
              <>
                <h4 className={styles["customer-profile-section-title"]}>Recent orders</h4>
                {selectedOrders.length === 0 ? (
                  <p className={adminStyles["admin-panel-subtitle"]}>This customer has no orders.</p>
                ) : (
                  <div className={adminStyles["admin-table-wrap"]}>
                    <table className={adminStyles["admin-table"]}>
                      <thead>
                        <tr><th>Order ID</th><th>Status</th><th>Total</th><th>Created</th></tr>
                      </thead>
                      <tbody>
                        {orderPager.pageItems.map((order) => (
                          <tr key={order.id}>
                            <td>{order.id}</td>
                            <td><span className={[adminStyles["admin-status"], adminStyles[order.status.toLowerCase()]].filter(Boolean).join(" ")}>{order.status}</span></td>
                            <td>{formatCurrency(order.total)}</td>
                            <td>{formatDate(order.createdAt)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                    <Pagination {...orderPager} label="Customer orders" />
                  </div>
                )}
              </>
            )}
          </div>
        </div>
      )}
    </>
  );
}
