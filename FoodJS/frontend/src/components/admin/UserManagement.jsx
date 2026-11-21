import { useState } from "react";

const EMPTY_USER = {
  firstName: "",
  lastName: "",
  email: "",
  password: "",
  mobileNumber: "",
  address: "",
  role: "customer",
};

export default function UserManagement({
  users,
  currentUserId,
  onAddUser,
  onUpdateUser,
  onDeleteUser,
}) {
  const [form, setForm] = useState(EMPTY_USER);
  const [editId, setEditId] = useState(null);

  const handleInput = (field, value) => {
    setForm((current) => ({ ...current, [field]: value }));
  };

  const reset = () => {
    setForm(EMPTY_USER);
    setEditId(null);
  };

  const submitUser = async (event) => {
    event.preventDefault();

    const payload = {
      firstName: form.firstName.trim(),
      lastName: form.lastName.trim(),
      email: form.email.trim(),
      mobileNumber: form.mobileNumber.trim(),
      address: form.address.trim(),
      role: form.role,
    };

    if (!payload.firstName || !payload.lastName || !payload.email) {
      return;
    }

    // Password is required when adding, optional when editing (blank = keep current)
    if (!editId && form.password.length < 6) {
      return;
    }
    if (form.password) {
      payload.password = form.password;
    }

    const ok = editId ? await onUpdateUser(editId, payload) : await onAddUser(payload);
    if (ok) {
      reset();
    }
  };

  const beginEdit = (user) => {
    setEditId(user.id);
    setForm({
      firstName: user.firstName || "",
      lastName: user.lastName || "",
      email: user.email || "",
      password: "",
      mobileNumber: user.mobileNumber || "",
      address: user.address || "",
      role: user.role || "customer",
    });
  };

  return (
    <section className="admin-panel">
      <div className="admin-panel-header">
        <h2>User Management</h2>
      </div>

      <form className="admin-form-grid" onSubmit={submitUser}>
        <input
          type="text"
          placeholder="First name"
          value={form.firstName}
          onChange={(event) => handleInput("firstName", event.target.value)}
        />
        <input
          type="text"
          placeholder="Last name"
          value={form.lastName}
          onChange={(event) => handleInput("lastName", event.target.value)}
        />
        <input
          type="email"
          placeholder="Email"
          value={form.email}
          onChange={(event) => handleInput("email", event.target.value)}
        />
        <input
          type="password"
          placeholder={editId ? "New password (optional)" : "Password (min 6 chars)"}
          value={form.password}
          onChange={(event) => handleInput("password", event.target.value)}
        />
        <select value={form.role} onChange={(event) => handleInput("role", event.target.value)}>
          <option value="customer">Customer</option>
          <option value="admin">Admin</option>
        </select>
        <input
          type="text"
          placeholder="Mobile number"
          value={form.mobileNumber}
          onChange={(event) => handleInput("mobileNumber", event.target.value)}
        />
        <input
          type="text"
          placeholder="Address"
          value={form.address}
          onChange={(event) => handleInput("address", event.target.value)}
        />

        <div className="admin-form-actions">
          <button type="submit" className="admin-btn admin-btn-primary">
            {editId ? "Save User" : "Add User"}
          </button>
          {editId ? (
            <button type="button" className="admin-btn admin-btn-light" onClick={reset}>
              Cancel Edit
            </button>
          ) : null}
        </div>
      </form>

      <div className="admin-table-wrap">
        <table className="admin-table">
          <thead>
            <tr>
              <th>ID</th>
              <th>Name</th>
              <th>Email</th>
              <th>Role</th>
              <th>Mobile</th>
              <th>Registered</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {users.length === 0 ? (
              <tr>
                <td colSpan={7} className="admin-empty-cell">
                  No registered users found.
                </td>
              </tr>
            ) : (
              users.map((user) => (
                <tr key={user.id}>
                  <td>{user.id}</td>
                  <td>{user.name}</td>
                  <td>{user.email}</td>
                  <td>
                    <span className={`admin-status ${user.role === "admin" ? "preparing" : "ready"}`}>
                      {user.role}
                    </span>
                  </td>
                  <td>{user.mobileNumber || "-"}</td>
                  <td>{user.createdAt ? new Date(user.createdAt).toLocaleDateString("en-PH") : "-"}</td>
                  <td>
                    <div className="admin-item-actions">
                      <button
                        type="button"
                        className="admin-btn admin-btn-light admin-table-action"
                        onClick={() => beginEdit(user)}
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        className="admin-btn admin-btn-danger admin-table-action"
                        onClick={() => onDeleteUser(user.id)}
                        disabled={user.id === currentUserId}
                        title={user.id === currentUserId ? "You cannot delete your own account" : "Delete user"}
                      >
                        Delete
                      </button>
                    </div>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </section>
  );
}
