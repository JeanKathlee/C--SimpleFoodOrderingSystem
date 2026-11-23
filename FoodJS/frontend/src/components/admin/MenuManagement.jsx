import { useMemo, useState, useRef } from "react";
import { IMAGE_ACCEPT, checkImageFile } from "../../lib/imageUpload";

const EMPTY_FORM = {
  name: "",
  description: "",
  categoryId: "",
  price: "",
  prepTime: "",
  isAvailable: true,
  image: null,
  imagePreview: null,
};

function formatCurrency(value) {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    maximumFractionDigits: 0,
  }).format(value);
}

export default function MenuManagement({
  categories,
  menuItems,
  onAddMenuItem,
  onUpdateMenuItem,
  onDeleteMenuItem,
}) {
  const [form, setForm] = useState({
    ...EMPTY_FORM,
    categoryId: categories[0]?.id || "",
  });
  const [editId, setEditId] = useState(null);
  const [imageError, setImageError] = useState("");
  const fileInputRef = useRef(null);

  const groupedItems = useMemo(() => {
    return categories.map((category) => ({
      ...category,
      items: menuItems.filter((item) => item.categoryId === category.id),
    }));
  }, [categories, menuItems]);

  const handleInput = (field, value) => {
    setForm((current) => ({ ...current, [field]: value }));
  };

  const handleImageChange = (event) => {
    const file = event.target.files[0];
    if (!file) return;

    const problem = checkImageFile(file);
    setImageError(problem || "");
    if (problem) {
      event.target.value = "";
      return;
    }

    const reader = new FileReader();
    reader.onloadend = () => {
      setForm((current) => ({
        ...current,
        image: reader.result,
        imagePreview: reader.result,
      }));
    };
    reader.readAsDataURL(file);
  };

  const resetForm = () => {
    setEditId(null);
    setImageError("");
    setForm({
      ...EMPTY_FORM,
      categoryId: categories[0]?.id || "",
    });
    if (fileInputRef.current) {
      fileInputRef.current.value = "";
    }
  };

  const submitForm = (event) => {
    event.preventDefault();
    const payload = {
      name: form.name.trim(),
      description: form.description.trim(),
      categoryId: form.categoryId,
      price: Number(form.price),
      prepTime: Number(form.prepTime),
      isAvailable: form.isAvailable,
      image: form.image || undefined,
    };

    if (!payload.name || !payload.categoryId || payload.price <= 0 || payload.prepTime <= 0) {
      return;
    }

    if (editId) {
      onUpdateMenuItem(editId, payload);
    } else {
      onAddMenuItem(payload);
    }

    resetForm();
  };

  const beginEdit = (item) => {
    setEditId(item.id);
    setForm({
      name: item.name,
      description: item.description || "",
      categoryId: item.categoryId,
      price: String(item.price),
      prepTime: String(item.prepTime),
      isAvailable: item.isAvailable,
      image: item.image || null,
      imagePreview: item.image || null,
    });
  };

  return (
    <section className="admin-panel">
      <div className="admin-panel-header">
        <h2>Menu Management</h2>
      </div>

      <form className="admin-form-grid" onSubmit={submitForm}>
        <input
          type="text"
          placeholder="Menu item name"
          aria-label="Menu item name"
          value={form.name}
          onChange={(event) => handleInput("name", event.target.value)}
        />

        <textarea
          className="admin-form-textarea"
          placeholder="Item description"
          aria-label="Item description"
          value={form.description}
          onChange={(event) => handleInput("description", event.target.value)}
        />

        <select
          aria-label="Category"
          value={form.categoryId}
          onChange={(event) => handleInput("categoryId", event.target.value)}
        >
          {categories.map((category) => (
            <option value={category.id} key={category.id}>
              {category.name}
            </option>
          ))}
        </select>

        <input
          type="number"
          min="1"
          placeholder="Price"
          aria-label="Price"
          value={form.price}
          onChange={(event) => handleInput("price", event.target.value)}
        />

        <input
          type="number"
          min="1"
          placeholder="Prep time (min)"
          aria-label="Prep time in minutes"
          value={form.prepTime}
          onChange={(event) => handleInput("prepTime", event.target.value)}
        />

        <div className="admin-form-footer">
          <input
            className="admin-file-input"
            type="file"
            aria-label="Menu item image"
            aria-describedby="menu-image-error"
            accept={IMAGE_ACCEPT}
            onChange={handleImageChange}
            ref={fileInputRef}
          />

          {imageError && (
            <span id="menu-image-error" className="admin-field-error" role="alert">
              {imageError}
            </span>
          )}

          {form.imagePreview && (
            <img
              src={form.imagePreview}
              alt="Preview"
              style={{ width: "56px", height: "56px", objectFit: "cover", borderRadius: "8px" }}
            />
          )}

          <label className="admin-checkbox-row">
            <input
              type="checkbox"
              checked={form.isAvailable}
              onChange={(event) => handleInput("isAvailable", event.target.checked)}
            />
            <span>Available</span>
          </label>

          <div className="admin-form-actions">
            <button type="submit" className="admin-btn admin-btn-primary">
              {editId ? "Save Item" : "Add Item"}
            </button>
            {editId ? (
              <button
                type="button"
                className="admin-btn admin-btn-light"
                onClick={resetForm}
              >
                Cancel Edit
              </button>
            ) : null}
          </div>
        </div>
      </form>

      <div className="admin-grid-columns">
        {groupedItems.map((category) => (
          <article key={category.id} className="admin-subcard">
            <h3>{category.name}</h3>
            <p>{category.description}</p>
            <ul className="admin-item-list">
              {category.items.length === 0 ? (
                <li className="admin-empty-item">No items in this category yet.</li>
              ) : (
                category.items.map((item) => (
                  <li key={item.id}>
                    <div>
                      <strong>{item.name}</strong>
                      <span>
                        {formatCurrency(item.price)} | {item.prepTime} min |{" "}
                        {item.isAvailable ? "Available" : "Hidden"}
                      </span>
                    </div>
                    <div className="admin-item-actions">
                      <button
                        type="button"
                        className="admin-btn admin-btn-light"
                        onClick={() => beginEdit(item)}
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        className="admin-btn admin-btn-danger"
                        onClick={() => onDeleteMenuItem(item.id)}
                      >
                        Delete
                      </button>
                    </div>
                  </li>
                ))
              )}
            </ul>
          </article>
        ))}
      </div>
    </section>
  );
}
