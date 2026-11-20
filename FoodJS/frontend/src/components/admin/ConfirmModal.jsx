import { useEffect, useId, useRef, useState } from "react";
import "./ConfirmModal.css";

export default function ConfirmModal({ open, title, message, confirmLabel, cancelLabel = "Cancel",
  hideCancel = false, hideConfirm = false, dialogClassName = "", children, confirmVariant = "danger", onCancel, onConfirm }) {
  const dialogRef = useRef(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  const titleId = useId();
  const messageId = useId();

  useEffect(() => {
    if (!open) return;
    setError("");
    const previous = document.activeElement;
    const overflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    (dialogRef.current?.querySelector("button") || dialogRef.current)?.focus();
    return () => {
      document.body.style.overflow = overflow;
      if (previous?.isConnected) previous.focus();
    };
  }, [open]);

  useEffect(() => {
    if (!open) return;
    if (pending) dialogRef.current?.focus();
    else if (document.activeElement === dialogRef.current)
      dialogRef.current?.querySelector("button:not(:disabled)")?.focus();
  }, [open, pending]);

  if (!open) return null;
  const handleKeys = event => {
    if (event.key === "Escape" && !pending) { event.preventDefault(); event.stopPropagation(); onCancel(); }
    if (event.key !== "Tab") return;
    const buttons = [...dialogRef.current.querySelectorAll("button:not(:disabled), [tabindex='0']")];
    if (!buttons.length) { event.preventDefault(); return; }
    const first = buttons[0];
    const last = buttons[buttons.length - 1];
    if (event.shiftKey && (document.activeElement === first || document.activeElement === dialogRef.current)) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  };
  const confirm = async () => {
    if (pending) return;
    setPending(true); setError("");
    try { await onConfirm(); }
    catch (failure) { setError(failure.message || "Could not complete the action. Please retry."); }
    finally { setPending(false); }
  };
  return (
    <div className="admin-modal-backdrop" role="presentation" onClick={() => { if (!pending) onCancel(); }}>
      <div ref={dialogRef} className={`admin-modal ${dialogClassName}`} role="dialog" tabIndex={-1} aria-modal="true"
        aria-labelledby={titleId} aria-describedby={messageId} aria-busy={pending}
        onKeyDown={handleKeys} onClick={event => event.stopPropagation()}>
        <h3 id={titleId}>{title}</h3><p id={messageId}>{message}</p>
        {children}
        {error && <p className="menu-error" role="alert">{error}</p>}
        <div className="admin-modal-actions">
          {!hideCancel && <button type="button" className="admin-btn admin-btn-light" disabled={pending} onClick={onCancel}>{cancelLabel}</button>}
          {!hideConfirm && <button type="button" className={`admin-btn admin-btn-${confirmVariant}`} disabled={pending} onClick={confirm}>
            {pending ? "Please wait…" : confirmLabel}</button>}
        </div>
      </div>
    </div>
  );
}
