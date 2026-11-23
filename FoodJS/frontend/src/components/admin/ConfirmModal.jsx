import { useEffect } from "react";

export default function ConfirmModal({
  open,
  title,
  message,
  confirmLabel,
  cancelLabel = "Cancel",
  hideCancel = false,
  confirmVariant = "danger",
  onCancel,
  onConfirm,
}) {
  // Esc works like Cancel
  useEffect(() => {
    if (!open) return undefined;
    const handleKey = (event) => event.key === "Escape" && onCancel?.();
    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, [open, onCancel]);

  if (!open) {
    return null;
  }

  return (
    <div className="admin-modal-backdrop" role="presentation" onClick={onCancel}>
      <div
        className="admin-modal"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(event) => event.stopPropagation()}
      >
        <h3>{title}</h3>
        <p>{message}</p>

        <div className="admin-modal-actions">
          {!hideCancel && (
            <button type="button" className="admin-btn admin-btn-light" onClick={onCancel}>
              {cancelLabel}
            </button>
          )}
          <button
            type="button"
            className={`admin-btn admin-btn-${confirmVariant}`}
            onClick={onConfirm}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
