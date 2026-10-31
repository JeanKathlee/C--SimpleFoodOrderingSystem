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

export default function CustomersPanel({ customers, orders, selectedCustomer, onViewCustomer, onClose }) {
  const selectedOrders = selectedCustomer
    ? orders.filter((order) => String(order.userId) === String(selectedCustomer.id))
    : [];
  const totalSpent = selectedOrders.reduce((total, order) => total + order.total, 0);

  return (
    <>
      <section className="admin-panel">
        <div className="admin-panel-header">
          <div>
            <h2>Customers</h2>
            <p className="admin-panel-subtitle">View customer account details and order history.</p>
          </div>
        </div>

        <div className="admin-table-wrap">
          <table className="admin-table">
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
                  <td colSpan="5" className="admin-empty-cell">No customers found.</td>
                </tr>
              ) : (
                customers.map((customer) => (
                  <tr key={customer.id}>
                    <td>{customer.name || `${customer.firstName} ${customer.lastName}`}</td>
                    <td>{customer.email}</td>
                    <td>{customer.mobileNumber || "Not provided"}</td>
                    <td>{formatDate(customer.createdAt)}</td>
                    <td>
                      <button
                        type="button"
                        className="admin-btn admin-btn-primary admin-table-action"
                        onClick={() => onViewCustomer(customer)}
                      >
                        View Profile
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </section>

      {selectedCustomer && (
        <div className="admin-modal-backdrop" role="presentation" onClick={onClose}>
          <div
            className="admin-modal customer-profile-modal"
            role="dialog"
            aria-modal="true"
            aria-label={`Profile for ${selectedCustomer.name}`}
            onClick={(event) => event.stopPropagation()}
          >
            <div className="admin-panel-header">
              <div>
                <h3>{selectedCustomer.name}</h3>
                <p className="admin-panel-subtitle">Customer profile</p>
              </div>
              <button type="button" className="admin-btn admin-btn-light" onClick={onClose}>
                Close
              </button>
            </div>

            <dl className="customer-profile-details">
              <div><dt>Email</dt><dd>{selectedCustomer.email || "Not provided"}</dd></div>
              <div><dt>Mobile number</dt><dd>{selectedCustomer.mobileNumber || "Not provided"}</dd></div>
              <div><dt>Address</dt><dd>{selectedCustomer.address || "Not provided"}</dd></div>
              <div><dt>Joined</dt><dd>{formatDate(selectedCustomer.createdAt)}</dd></div>
              <div><dt>Total orders</dt><dd>{selectedOrders.length}</dd></div>
              <div><dt>Total spent</dt><dd>{formatCurrency(totalSpent)}</dd></div>
            </dl>

            <h4 className="customer-profile-section-title">Recent orders</h4>
            {selectedOrders.length === 0 ? (
              <p className="admin-panel-subtitle">This customer has no orders.</p>
            ) : (
              <div className="admin-table-wrap">
                <table className="admin-table">
                  <thead>
                    <tr><th>Order ID</th><th>Status</th><th>Total</th><th>Created</th></tr>
                  </thead>
                  <tbody>
                    {selectedOrders.map((order) => (
                      <tr key={order.id}>
                        <td>{order.id}</td>
                        <td><span className={`admin-status ${order.status.toLowerCase()}`}>{order.status}</span></td>
                        <td>{formatCurrency(order.total)}</td>
                        <td>{formatDate(order.createdAt)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      )}
    </>
  );
}
