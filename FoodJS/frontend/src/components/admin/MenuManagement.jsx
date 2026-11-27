import adminStyles from "../../pages/Dashboard.module.css";
import styles from "./MenuManagement.module.css";
import Button from "../ui/Button";
import { useEffect, useRef, useState } from "react";
import Pagination from "../Pagination";
import ConfirmModal from "../ui/Modal";
import { formatPrice } from "../../lib/menuApi";
import { checkImageFile, FOOD_IMAGE_MAX_BYTES } from "../../lib/imageUpload";
import DrinkOptionsEditor from "./DrinkOptionsEditor";
import { newDrinkOptions, drinkOptionsError, drinkOptionsPayload } from "../../lib/drinkOptions";

const EMPTY_FORM = { name: "", description: "", categoryId: "", price: "", prepTime: "", isAvailable: true, image: null };
const EMPTY_PAGE = { items: [], totalCount: 0, page: 1, pageSize: 10, totalPages: 1 };

export default function MenuManagement({ categories, categoryLoading, categoryError, onRetryCategories,
  revision, onAddMenuItem, onUpdateMenuItem, onDeleteMenuItem, onSetAvailability, onDraftChange }) {
  const [form, setForm] = useState({ ...EMPTY_FORM, includesDrink: false, drinkOptions: [] });
  const [editor, setEditor] = useState(null);
  const [errors, setErrors] = useState({});
  const [saveError, setSaveError] = useState("");
  const [saving, setSaving] = useState(false);
  const [readingImage, setReadingImage] = useState(false);
  const [imageChanged, setImageChanged] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [discardAction, setDiscardAction] = useState(null);
  const [search, setSearch] = useState("");
  const [query, setQuery] = useState({ page: 1, pageSize: 10, search: "", categoryId: "", availability: "" });
  const [data, setData] = useState(EMPTY_PAGE);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState("");
  const [hasLoaded, setHasLoaded] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const [busyItems, setBusyItems] = useState([]);
  const [actionError, setActionError] = useState("");
  const nameRef = useRef(null);
  const editorRef = useRef(null);
  const addRef = useRef(null);
  const fileRef = useRef(null);
  const imageReadId = useRef(0);

  useEffect(() => { onDraftChange?.({ dirty, busy: saving || readingImage }); }, [dirty, saving, readingImage, onDraftChange]);
  useEffect(() => () => { onDraftChange?.({ dirty: false, busy: false }); }, [onDraftChange]);

  useEffect(() => {
    const timer = setTimeout(() => setQuery(current => current.search === search.trim() ? current : { ...current, search: search.trim(), page: 1 }), 300);
    return () => clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    let controller;
    let disposed = false;
    const load = async () => {
      controller?.abort();
      controller = new AbortController();
      const signal = controller.signal;
      setLoading(true);
      const params = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) });
      if (query.search) params.set("search", query.search);
      if (query.categoryId) params.set("categoryId", query.categoryId);
      if (query.availability) params.set("isAvailable", query.availability);
      try {
        const response = await fetch(`/api/menu/paged?${params}`, { signal });
        const body = await response.json();
        if (!response.ok) throw new Error(body.message || "Could not load menu items.");
        if (!Array.isArray(body.items) || !Number.isInteger(body.totalCount)) throw new Error("The menu response was incomplete. Please retry.");
        if (signal.aborted || disposed) return;
        setData(body); setHasLoaded(true); setLoadError("");
      } catch (error) {
        if (!signal.aborted && !disposed) setLoadError(error.message || "Could not connect. Please retry.");
      } finally {
        if (!signal.aborted && !disposed) setLoading(false);
      }
    };
    load();
    const timer = setInterval(load, 15000);
    return () => { disposed = true; controller?.abort(); clearInterval(timer); };
  }, [query, revision, refresh]);

  const resetEditor = () => {
    imageReadId.current += 1;
    setEditor(null); setDirty(false); setErrors({}); setSaveError(""); setReadingImage(false);
    setImageChanged(false); setForm({ ...EMPTY_FORM, includesDrink: false, drinkOptions: [] });
  };
  const guardDraft = action => {
    if (dirty) setDiscardAction(() => action);
    else action();
  };
  const closeEditor = () => { if (!saving && !readingImage) guardDraft(resetEditor); };
  const openEditor = item => guardDraft(() => {
    imageReadId.current += 1;
    setForm(item ? { name: item.name, description: item.description || "", categoryId: item.categoryId,
      price: String(item.price), prepTime: String(item.prepTime), isAvailable: item.isAvailable, image: item.image || null,
      includesDrink: !!item.drinkOptions?.length,
      drinkOptions: (item.drinkOptions ?? []).map(option => ({ ...option, addOn: String(option.addOn) })) }
      : { ...EMPTY_FORM, categoryId: categories[0]?.id || "", includesDrink: false, drinkOptions: [] });
    setEditor(item ? { id: item.id, name: item.name } : { id: null });
    setErrors({}); setSaveError(""); setImageChanged(false); setDirty(false); setReadingImage(false);
    if (fileRef.current) fileRef.current.value = "";
  });
  const update = (field, value) => {
    setForm(current => ({ ...current, [field]: value })); setDirty(true);
    setErrors(current => ({ ...current, [field]: undefined })); setSaveError("");
  };
  const toggleDrink = enabled => {
    setForm(current => ({ ...current, includesDrink: enabled,
      drinkOptions: enabled && !current.drinkOptions.length ? newDrinkOptions() : current.drinkOptions }));
    setDirty(true); setSaveError("");
    setErrors(current => ({ ...current, drinkOptions: undefined }));
  };
  const readImage = event => {
    const file = event.target.files[0];
    if (!file) return;
    const fail = message => { setErrors(current => ({ ...current, image: message })); event.target.value = ""; };
    if (!["image/jpeg", "image/png", "image/webp"].includes(file.type)) return fail("Choose a JPG, PNG, or WebP image.");
    const imageError = checkImageFile(file, FOOD_IMAGE_MAX_BYTES);
    if (imageError) return fail(imageError);
    const id = ++imageReadId.current;
    setReadingImage(true);
    const reader = new FileReader();
    reader.onload = () => {
      if (id !== imageReadId.current) return;
      update("image", reader.result); setImageChanged(true); setReadingImage(false);
    };
    reader.onerror = () => {
      if (id !== imageReadId.current) return;
      setErrors(current => ({ ...current, image: "Could not read this image. Choose another file." })); setReadingImage(false);
    };
    reader.readAsDataURL(file);
  };
  const submit = async event => {
    event.preventDefault();
    if (saving || readingImage) return;
    const nextErrors = {};
    if (!form.name.trim()) nextErrors.name = "Enter an item name.";
    if (!categories.some(category => category.id === form.categoryId)) nextErrors.categoryId = "Choose a category.";
    if (!Number.isFinite(Number(form.price)) || Number(form.price) <= 0 || !/^\d+(\.\d{1,2})?$/.test(form.price)) nextErrors.price = "Enter a price greater than zero, with up to two decimal places.";
    if (!Number.isInteger(Number(form.prepTime)) || Number(form.prepTime) <= 0) nextErrors.prepTime = "Enter preparation time in whole minutes, greater than zero.";
    if (errors.image) nextErrors.image = errors.image;
    const optionsError = form.includesDrink
      ? (!form.drinkOptions.length ? "Add at least one included drink, or turn off Includes a drink." : drinkOptionsError(form.drinkOptions))
      : null;
    if (optionsError) nextErrors.drinkOptions = optionsError;
    setErrors(nextErrors);
    if (Object.keys(nextErrors).length) {
      editorRef.current?.querySelector(`[name="${Object.keys(nextErrors)[0]}"]`)?.focus(); return;
    }
    setSaving(true); setSaveError("");
    const payload = { name: form.name.trim(), description: form.description.trim(), categoryId: form.categoryId,
      price: Number(form.price), prepTime: Number(form.prepTime), isAvailable: form.isAvailable,
      drinkOptions: form.includesDrink ? drinkOptionsPayload(form.drinkOptions) : [],
      ...(imageChanged ? { image: form.image || "" } : {}) };
    try {
      const result = editor.id ? await onUpdateMenuItem(editor.id, payload) : await onAddMenuItem(payload);
      if (!result?.ok) throw new Error(result?.message || "Could not save this item. Your changes are still here.");
      resetEditor();
    } catch (error) { setSaveError(error.message || "Could not save. Your changes are still here."); }
    finally { setSaving(false); }
  };
  const toggleAvailability = async item => {
    setBusyItems(current => [...current, item.id]); setActionError("");
    try {
      const result = await onSetAvailability(item.id, !item.isAvailable);
      if (!result?.ok) throw new Error(result?.message || "Could not change availability.");
    } catch (error) { setActionError(error.message); }
    finally { setBusyItems(current => current.filter(id => id !== item.id)); }
  };
  const setFilter = (field, value) => setQuery(current => ({ ...current, [field]: value, page: 1 }));
  const filtered = query.search || query.categoryId || query.availability;
  const errorFor = field => errors[field] ? <small className={styles["menu-field-error"]} id={`menu-${field}-error`}>{errors[field]}</small> : null;
  const fieldProps = field => ({ id: `menu-${field}`, name: field, "aria-invalid": !!errors[field], "aria-describedby": errors[field] ? `menu-${field}-error` : undefined });

  return (
    <section className={[adminStyles["admin-panel"], styles["menu-management"]].join(" ")} aria-labelledby="menu-list-title">
      <div className={styles["menu-heading"]}>
        <div><h2 id="menu-list-title">Menu items</h2><p>Keep your menu accurate and ready for customers.</p></div>
        <Button variant="primary" ref={addRef} type="button" disabled={saving || readingImage || categoryLoading || !categories.length}
          onClick={() => openEditor(null)}>Add menu item</Button>
      </div>
      {(categoryError || (!categoryLoading && !categories.length)) && <div className={styles["menu-notice"]} role="alert">
        <p>{categoryError || "No categories are available yet. A category is required before adding an item."}</p>
        {categoryError && <Button variant="light" type="button" onClick={onRetryCategories}>Retry categories</Button>}
      </div>}
      {categoryLoading && <p className={styles["menu-hint"]} role="status">Loading categories…</p>}
      {editor && <ConfirmModal open title={editor.id ? `Edit ${editor.name}` : "Add a menu item"} message="Fields marked * are required."
        size="wide" hideCancel hideConfirm showClose closeLabel="Close menu item form" initialFocusRef={nameRef}
        busy={saving || readingImage} onCancel={closeEditor}>
      <form ref={editorRef} className={styles["menu-editor"]} onSubmit={submit} noValidate aria-label={editor.id ? "Edit menu item" : "Add menu item"} aria-busy={saving || readingImage}>
        <fieldset disabled={saving || readingImage} className={styles["menu-fields"]}>
          <div className={styles["menu-field"]}><label htmlFor="menu-name">Item name *</label>
            <input {...fieldProps("name")} ref={nameRef} required maxLength={120} autoComplete="off" placeholder="e.g. Classic cheeseburger" value={form.name} onChange={e => update("name", e.target.value)} />{errorFor("name")}</div>
          <div className={styles["menu-field"]}><label htmlFor="menu-categoryId">Category *</label>
            <select {...fieldProps("categoryId")} required value={form.categoryId} onChange={e => update("categoryId", e.target.value)}><option value="">Choose a category</option>
              {categories.map(category => <option key={category.id} value={category.id}>{category.name}</option>)}</select>{errorFor("categoryId")}</div>
          <div className={styles["menu-field"]}><label htmlFor="menu-price">Price (PHP) *</label>
            <input {...fieldProps("price")} type="number" inputMode="decimal" min="0.01" step="0.01" required placeholder="0.00" value={form.price} onChange={e => update("price", e.target.value)} />{errorFor("price")}</div>
          <div className={styles["menu-field"]}><label htmlFor="menu-prepTime">Preparation time (minutes) *</label>
            <input {...fieldProps("prepTime")} type="number" inputMode="numeric" min="1" step="1" required placeholder="e.g. 15" value={form.prepTime} onChange={e => update("prepTime", e.target.value)} />{errorFor("prepTime")}</div>
          <div className={[styles["menu-field"], styles["menu-field-wide"]].join(" ")}><label htmlFor="menu-description">Description <span>(optional)</span></label>
            <textarea {...fieldProps("description")} rows={3} maxLength={2000} placeholder="Describe the ingredients or what makes this item special." value={form.description} onChange={e => update("description", e.target.value)} />
            <small className={styles["menu-hint"]}>{form.description.length}/2000 characters</small></div>
          <div className={[styles["menu-field"], styles["menu-field-wide"]].join(" ")}><label htmlFor="menu-image">Item image <span>(optional)</span></label>
            <div className={styles["menu-image-control"]}>{form.image && <img className={styles["menu-image-preview"]} src={form.image} alt={`Preview of ${form.name || "menu item"}`} />}
              <div><input {...fieldProps("image")} ref={fileRef} type="file" accept="image/jpeg,image/png,image/webp" onChange={readImage} />
                <p className={styles["menu-hint"]}>JPG, PNG, or WebP · Up to 5 MB{readingImage ? " · Reading image…" : ""}</p>
                {form.image && <button type="button" className={styles["menu-text-button"]} onClick={() => { update("image", null); setImageChanged(true); if (fileRef.current) fileRef.current.value = ""; }}>Remove image</button>}
              </div></div>{errorFor("image")}</div>
          <div className={styles["menu-field-wide"]}>
            <label className={styles["menu-checkbox"]}><input id="menu-includesDrink" type="checkbox" checked={form.includesDrink}
              aria-describedby="menu-includesDrink-hint" onChange={e => toggleDrink(e.target.checked)} />Includes a drink</label>
            <p id="menu-includesDrink-hint" className={styles["menu-hint"]}>Enable for a meal or bundle with a drink. Leave off for standalone beverages, food, sides, or desserts.</p>
          </div>
          {form.includesDrink && <DrinkOptionsEditor options={form.drinkOptions} onChange={options => {
            update("drinkOptions", options);
            if (!options.length) setForm(current => ({ ...current, includesDrink: false }));
          }} error={errors.drinkOptions} />}
          <label className={[styles["menu-checkbox"], styles["menu-field-wide"]].join(" ")}><input type="checkbox" checked={form.isAvailable} onChange={e => update("isAvailable", e.target.checked)} />Available for ordering</label>
        </fieldset>
        {saveError && <p className={styles["menu-error"]} role="alert">{saveError}</p>}
        <div className={styles["menu-editor-actions"]}><Button variant="primary" type="submit" disabled={saving || readingImage || categoryLoading || !categories.length}>
          {saving ? "Saving…" : editor.id ? "Save changes" : "Add item"}</Button>
          <Button variant="light" type="button" disabled={saving || readingImage} onClick={closeEditor}>Cancel</Button>
          <span className={styles["menu-hint"]} aria-live="polite">{saving ? "Please wait. Your draft is being saved." : "Changes are saved only when you submit."}</span></div>
      </form>
      </ConfirmModal>}
      <div className={styles["menu-toolbar"]}>
        <div className={styles["menu-field"]}><label htmlFor="menu-search">Search menu</label><input id="menu-search" type="search" maxLength={120} placeholder="Search name or description" value={search} onChange={e => setSearch(e.target.value)} /></div>
        <div className={styles["menu-field"]}><label htmlFor="menu-filter-category">Category</label><select id="menu-filter-category" value={query.categoryId} onChange={e => setFilter("categoryId", e.target.value)}>
          <option value="">All categories</option>{categories.map(category => <option key={category.id} value={category.id}>{category.name}</option>)}</select></div>
        <div className={styles["menu-field"]}><label htmlFor="menu-filter-availability">Availability</label><select id="menu-filter-availability" value={query.availability} onChange={e => setFilter("availability", e.target.value)}>
          <option value="">All items</option><option value="true">Available</option><option value="false">Unavailable</option></select></div>
        {filtered && <button type="button" className={styles["menu-text-button"]} onClick={() => { setSearch(""); setQuery(current => ({ ...current, search: "", categoryId: "", availability: "", page: 1 })); }}>Clear filters</button>}
      </div>
      {loadError && <div className={styles["menu-notice"]} role="alert"><p>{loadError}{hasLoaded ? " Previously loaded items are shown below." : ""}</p>
        <Button variant="light" type="button" disabled={loading} onClick={() => setRefresh(value => value + 1)}>Retry menu</Button></div>}
      {actionError && <p role="alert" className={styles["menu-error"]}>{actionError}</p>}
      <p className={styles["menu-list-status"]} role="status">{loading ? "Loading menu items…" : loadError ? "Menu is not up to date." : `${data.totalCount} ${data.totalCount === 1 ? "item" : "items"}${filtered ? data.totalCount === 1 ? " matches your filters" : " match your filters" : " in your menu"}`}</p>
      <ul className={styles["menu-catalog"]} aria-label="Menu items" aria-busy={loading}>
        {data.items.map(item => <li key={item.id} className={styles["menu-catalog-item"]}>
          {item.image ? <img src={item.image} alt="" className={styles["menu-item-thumbnail"]} loading="lazy" /> : <span className={styles["menu-item-placeholder"]} aria-hidden="true">No image</span>}
          <div className={styles["menu-item-details"]}><h3>{item.name}</h3><p className={styles["menu-item-category"]}>{item.category}</p>
            {item.description && <p className={styles["menu-item-description"]}>{item.description}</p>}<span>{item.prepTime} min prep</span></div>
          <strong className={styles["menu-item-price"]}>{formatPrice(item.price)}</strong>
          <button type="button" role="switch" aria-checked={item.isAvailable} aria-label={`Availability for ${item.name}`} className={[styles["menu-availability"], item.isAvailable ? styles["is-available"] : ""].filter(Boolean).join(" ")}
            disabled={busyItems.includes(item.id) || saving || editor?.id === item.id} title={editor?.id === item.id ? "Change availability in the open editor." : undefined} onClick={() => toggleAvailability(item)}>{busyItems.includes(item.id) ? "Updating…" : item.isAvailable ? "Available" : "Unavailable"}</button>
          <div className={styles["menu-item-actions"]}><Button variant="light" type="button" disabled={saving || readingImage || busyItems.includes(item.id)} aria-label={`Edit ${item.name}`} onClick={() => openEditor(item)}>Edit</Button>
            <Button variant="danger" type="button" disabled={saving || busyItems.includes(item.id)} aria-label={`Delete ${item.name}`} onClick={() => onDeleteMenuItem(item, () => { if (editor?.id === item.id) resetEditor(); })}>Delete</Button></div>
        </li>)}
      </ul>
      {!loading && !loadError && !data.items.length && <div className={styles["menu-empty"]}><h3>{filtered ? "No matching items" : "Your menu is empty"}</h3><p>{filtered ? "Try a different search or clear your filters." : "Add your first item when categories are available."}</p></div>}
      <Pagination {...data} label="Menu items" disabled={loading} onPageChange={page => setQuery(current => ({ ...current, page }))} onPageSizeChange={pageSize => setFilter("pageSize", pageSize)} />
      <ConfirmModal open={!!discardAction} title="Discard unsaved changes?" message="Your current draft has not been saved. Discard it to continue?" confirmLabel="Discard changes"
        onCancel={() => setDiscardAction(null)} onConfirm={() => { const action = discardAction; setDiscardAction(null); action(); }} />
    </section>
  );
}
