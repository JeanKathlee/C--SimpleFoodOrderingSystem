import adminStyles from "../../pages/Dashboard.module.css";
import styles from "./CategoryManagement.module.css";
import Button from "../ui/Button";
import { useEffect, useId, useRef, useState } from "react";

import Pagination from "../Pagination";
import usePagination from "../../hooks/usePagination";

const EMPTY_CATEGORY = {
  name: "",
  description: "",
};

function CategoryEditor({ category, onSave, onClose }) {
  const [form, setForm] = useState({ name: category.name, description: category.description });
  const [errors, setErrors] = useState({});
  const [saveError, setSaveError] = useState("");
  const [saving, setSaving] = useState(false);
  const pending = useRef(false);
  const dialogRef = useRef(null);
  const titleId = useId();
  const nameId = useId();
  const descriptionId = useId();

  useEffect(() => {
    const previousFocus = document.activeElement;
    const previousOverflow = document.body.style.overflow;
    const dialog = dialogRef.current;
    dialog.showModal();
    document.body.style.overflow = "hidden";
    dialog.querySelector("input")?.focus();
    return () => {
      dialog.close();
      document.body.style.overflow = previousOverflow;
      if (previousFocus?.isConnected) previousFocus.focus();
    };
  }, []);

  const close = () => { if (!pending.current) onClose(); };
  const update = (field, value) => {
    setForm(current => ({ ...current, [field]: value }));
    setErrors(current => ({ ...current, [field]: undefined }));
    setSaveError("");
  };
  const submitCategory = async event => {
    event.preventDefault();
    if (pending.current) return;
    const payload = { name: form.name.trim(), description: form.description.trim() };
    const nextErrors = {};
    if (!payload.name) nextErrors.name = "Enter a category name.";
    if (!payload.description) nextErrors.description = "Enter a description.";
    setErrors(nextErrors);
    if (Object.keys(nextErrors).length) {
      dialogRef.current.querySelector(`[name="${Object.keys(nextErrors)[0]}"]`)?.focus();
      return;
    }
    pending.current = true;
    setSaving(true);
    setSaveError("");
    try {
      const result = await onSave(payload);
      if (!result?.ok) throw new Error(result?.message || "Could not save this category. Please retry.");
      onClose();
    } catch (error) {
      setSaveError(error.message || "Could not save this category. Your changes are still here.");
    } finally {
      pending.current = false;
      setSaving(false);
    }
  };

  return (
    <dialog ref={dialogRef} className={styles["category-editor-dialog"]} aria-labelledby={titleId} aria-modal="true" aria-busy={saving}
      onCancel={event => { event.preventDefault(); close(); }}
      onClick={event => { if (event.target === event.currentTarget) close(); }}>
      <div className={styles["category-editor-content"]}>
        <div className={styles["category-editor-heading"]}>
          <h2 id={titleId}>{category.id ? "Edit Category" : "Add Category"}</h2>
          <Button variant="light" type="button" aria-label="Close category form" disabled={saving} onClick={close}>×</Button>
        </div>
        <p>Fields marked * are required.</p>
        <form onSubmit={submitCategory} noValidate>
          <fieldset disabled={saving} className={styles["category-editor-fields"]}>
            <div>
              <label htmlFor={nameId}>Category Name *</label>
              <input id={nameId} name="name" type="text" required autoComplete="off" placeholder="e.g. Drinks"
                value={form.name} onChange={event => update("name", event.target.value)}
                aria-invalid={!!errors.name} aria-describedby={errors.name ? `${nameId}-error` : undefined} />
              {errors.name && <small id={`${nameId}-error`} className={styles["category-editor-error"]}>{errors.name}</small>}
            </div>
            <div>
              <label htmlFor={descriptionId}>Description *</label>
              <textarea id={descriptionId} name="description" required rows={3} placeholder="Describe this category"
                value={form.description} onChange={event => update("description", event.target.value)}
                aria-invalid={!!errors.description} aria-describedby={errors.description ? `${descriptionId}-error` : undefined} />
              {errors.description && <small id={`${descriptionId}-error`} className={styles["category-editor-error"]}>{errors.description}</small>}
            </div>
          </fieldset>
          {saveError && <p className={styles["category-editor-error"]} role="alert">{saveError}</p>}
          <div className={styles["category-editor-actions"]}>
            <Button variant="light" type="button" disabled={saving} onClick={close}>Cancel</Button>
            <Button variant="primary" type="submit" disabled={saving}>{saving ? "Saving…" : "Save Category"}</Button>
          </div>
          <span className={styles["category-editor-status"]} role="status">{saving ? "Saving category. Please wait." : ""}</span>
        </form>
      </div>
    </dialog>
  );
}

export default function CategoryManagement({
  categories,
  menuItems,
  onAddCategory,
  onUpdateCategory,
  onDeleteCategory,
}) {
  const [editor, setEditor] = useState(null);
  const pager = usePagination(categories);

  return (
    <section className={adminStyles["admin-panel"]}>
      <div className={adminStyles["admin-panel-header"]}>
        <h2>Category Management</h2>
        <Button variant="primary" type="button" onClick={() => setEditor({ ...EMPTY_CATEGORY, id: null })}>Add Category</Button>
      </div>

      {editor && <CategoryEditor category={editor} onClose={() => setEditor(null)}
        onSave={payload => editor.id ? onUpdateCategory(editor.id, payload) : onAddCategory(payload)} />}

      <ul className={adminStyles["admin-category-list"]}>
        {pager.pageItems.map((category) => {
          const itemCount = menuItems.filter((item) => item.categoryId === category.id).length;

          return (
            <li key={category.id}>
              <div>
                <h3>{category.name}</h3>
                <p>{category.description}</p>
                <span>{itemCount} items</span>
              </div>
              <div className={adminStyles["admin-item-actions"]}>
                <Button variant="light"
                  type="button"

                  onClick={() => setEditor({ id: category.id, name: category.name, description: category.description || "" })}
                >
                  Edit
                </Button>
                <Button variant="danger"
                  type="button"

                  onClick={() => onDeleteCategory(category.id, itemCount)}
                  title={itemCount > 0 ? "Move or delete related items first" : "Delete category"}
                >
                  Delete
                </Button>
              </div>
            </li>
          );
        })}
      </ul>
      <Pagination {...pager} label="Categories" />
    </section>
  );
}
