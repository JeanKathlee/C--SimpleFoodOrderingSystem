import { useEffect, useId, useRef, useState } from "react";
import styles from "./Modal.module.css";
import Button from "./Button";

export default function Modal({ open, title, message, confirmLabel, cancelLabel = "Cancel",
  hideCancel = false, hideConfirm = false, size = "default", children, confirmVariant = "danger", onCancel, onConfirm }) {
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
    (dialogRef.current?.querySelector("[data-modal-cancel]:not(:disabled)") || dialogRef.current?.querySelector("button:not(:disabled)") || dialogRef.current)?.focus();
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
    const buttons = [...dialogRef.current.querySelectorAll("button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex='0']")];
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
    <div className={styles.backdrop} role="presentation" onClick={() => { if (!pending) onCancel(); }}>
      <div ref={dialogRef} className={[styles.dialog, size === "wide" ? styles.wide : ""].filter(Boolean).join(" ")} role="dialog" tabIndex={-1} aria-modal="true"
        aria-labelledby={titleId} aria-describedby={messageId} aria-busy={pending}
        onKeyDown={handleKeys} onClick={event => event.stopPropagation()}>
        <h3 id={titleId}>{title}</h3><p id={messageId}>{message}</p>
        {children}
        {error && <p className={styles.error} role="alert">{error}</p>}
        <div className={styles.actions}>
          {!hideCancel && <Button data-modal-cancel variant="light" disabled={pending} onClick={onCancel}>{cancelLabel}</Button>}
          {!hideConfirm && <Button variant={confirmVariant} disabled={pending} onClick={confirm}>
            {pending ? "Please wait…" : confirmLabel}</Button>}
        </div>
      </div>
    </div>
  );
}
