import { useState } from "react";

const EMPTY_CATEGORY = {
  name: "",
  description: "",
};

export default function CategoryManagement({
  categories,
  menuItems,
  onAddCategory,
  onUpdateCategory,
  onDeleteCategory,
}) {
  const [form, setForm] = useState(EMPTY_CATEGORY);
  const [editId, setEditId] = useState(null);

  const reset = () => {
    setForm(EMPTY_CATEGORY);
    setEditId(null);
  };

  const submitCategory = (event) => {
    event.preventDefault();
    const payload = {
      name: form.name.trim(),
      description: form.description.trim(),
    };

    if (!payload.name || !payload.description) {
      return;
    }

    if (editId) {
      onUpdateCategory(editId, payload);
    } else {
      onAddCategory(payload);
    }

    reset();
  };

  const beginEdit = (category) => {
    setEditId(category.id);
    setForm({ name: category.name, description: category.description });
  };

  return (
    <section className="admin-panel">
      <div className="admin-panel-header">
        <h2>Category Management</h2>
      </div>

      <form className="admin-form-grid admin-category-form" onSubmit={submitCategory}>
        <input
          type="text"
          placeholder="Category name"
          aria-label="Category name"
          value={form.name}
          onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
        />

        <input
          type="text"
          placeholder="Description"
          aria-label="Category description"
          value={form.description}
          onChange={(event) =>
            setForm((current) => ({ ...current, description: event.target.value }))
          }
        />

        <div className="admin-form-actions">
          <button type="submit" className="admin-btn admin-btn-primary">
            {editId ? "Save Category" : "Add Category"}
          </button>
          {editId ? (
            <button type="button" className="admin-btn admin-btn-light" onClick={reset}>
              Cancel Edit
            </button>
          ) : null}
        </div>
      </form>

      <ul className="admin-category-list">
        {categories.map((category) => {
          const itemCount = menuItems.filter((item) => item.categoryId === category.id).length;

          return (
            <li key={category.id}>
              <div>
                <h3>{category.name}</h3>
                <p>{category.description}</p>
                <span>{itemCount} items</span>
              </div>
              <div className="admin-item-actions">
                <button
                  type="button"
                  className="admin-btn admin-btn-light"
                  onClick={() => beginEdit(category)}
                >
                  Edit
                </button>
                <button
                  type="button"
                  className="admin-btn admin-btn-danger"
                  onClick={() => onDeleteCategory(category.id, itemCount)}
                  title={itemCount > 0 ? "Move or delete related items first" : "Delete category"}
                >
                  Delete
                </button>
              </div>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
