import { useEffect, useId, useRef, useState } from "react";
import styles from "./Modal.module.css";
import Button from "./Button";

// Nested confirmations must not unlock scrolling while another dialog is open.
let openDialogs = 0;
let previousBodyOverflow = "";

export default function Modal({ open, title, message, confirmLabel, cancelLabel = "Cancel",
  hideCancel = false, hideConfirm = false, size = "default", children, confirmVariant = "danger", onCancel, onConfirm,
  busy = false, initialFocusRef, showClose = false, closeLabel = "Close dialog" }) {
  const dialogRef = useRef(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  const titleId = useId();
  const messageId = useId();
  const blocked = pending || busy;

  useEffect(() => {
    if (!open) return;
    setError("");
    const previous = document.activeElement;
    if (openDialogs === 0) previousBodyOverflow = document.body.style.overflow;
    openDialogs += 1;
    document.body.style.overflow = "hidden";
    (initialFocusRef?.current || dialogRef.current?.querySelector("[data-modal-cancel]:not(:disabled)") || dialogRef.current?.querySelector("button:not(:disabled)") || dialogRef.current)?.focus();
    return () => {
      openDialogs -= 1;
      if (openDialogs === 0) document.body.style.overflow = previousBodyOverflow;
      if (previous?.isConnected) previous.focus();
    };
  }, [open, initialFocusRef]);

  useEffect(() => {
    if (!open) return;
    if (blocked) dialogRef.current?.focus();
    else if (document.activeElement === dialogRef.current)
      (initialFocusRef?.current || dialogRef.current?.querySelector("button:not(:disabled)"))?.focus();
  }, [open, blocked, initialFocusRef]);

  if (!open) return null;
  const handleKeys = event => {
    if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); if (!blocked) onCancel(); }
    if (event.key !== "Tab") return;
    const buttons = [...dialogRef.current.querySelectorAll("*")].filter(element =>
      element.matches("button, a[href], input, select, textarea, [tabindex]") && element.tabIndex >= 0 &&
      !element.matches(":disabled") && element.getAttribute("type") !== "hidden" && !element.closest("[hidden], [inert]"));
    if (!buttons.length) { event.preventDefault(); return; }
    const first = buttons[0];
    const last = buttons[buttons.length - 1];
    if (event.shiftKey && (document.activeElement === first || document.activeElement === dialogRef.current)) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  };
  const confirm = async () => {
    if (blocked) return;
    setPending(true); setError("");
    try { await onConfirm(); }
    catch (failure) { setError(failure.message || "Could not complete the action. Please retry."); }
    finally { setPending(false); }
  };
  return (
    <div className={styles.backdrop} role="presentation" onClick={() => { if (!blocked) onCancel(); }}>
      <div ref={dialogRef} className={[styles.dialog, size === "wide" ? styles.wide : ""].filter(Boolean).join(" ")} role="dialog" tabIndex={-1} aria-modal="true"
        aria-labelledby={titleId} aria-describedby={message ? messageId : undefined} aria-busy={blocked}
        onKeyDown={handleKeys} onClick={event => event.stopPropagation()}>
        <div className={styles.heading}><h3 id={titleId}>{title}</h3>
          {showClose && <Button variant="light" type="button" aria-label={closeLabel} disabled={blocked} onClick={onCancel}>×</Button>}
        </div>
        {message && <p id={messageId}>{message}</p>}
        {children}
        {error && <p className={styles.error} role="alert">{error}</p>}
        {(!hideCancel || !hideConfirm) && <div className={styles.actions}>
          {!hideCancel && <Button data-modal-cancel variant="light" disabled={blocked} onClick={onCancel}>{cancelLabel}</Button>}
          {!hideConfirm && <Button variant={confirmVariant} disabled={blocked} onClick={confirm}>
            {pending ? "Please wait…" : confirmLabel}</Button>}
        </div>}
      </div>
    </div>
  );
}
