import styles from "./Button.module.css";

// Native button semantics, including React 19 refs and data/ARIA attributes.
export default function Button({ variant = "light", type = "button", className = "", ...props }) {
  return <button {...props} type={type} data-ui-button="" data-variant={variant}
    className={[styles.button, styles[variant] || styles.light, className].filter(Boolean).join(" ")} />;
}
