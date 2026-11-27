import Button from "../ui/Button";
import styles from "./MenuManagement.module.css";
import { includeDrink } from "../../lib/drinkOptions";

export default function DrinkOptionsEditor({ options, onChange, error }) {
  const update = (index, field, value) => onChange(options.map((option, i) => i === index ? { ...option, [field]: value } : option));
  return (
    <fieldset className={[styles["menu-drink-options"], styles["menu-field-wide"]].join(" ")}
      id="menu-drinkOptions" name="drinkOptions" tabIndex={-1} aria-invalid={!!error}
      aria-describedby={error ? "menu-drinkOptions-error" : "menu-drinkOptions-hint"}>
      <legend>Drink options</legend>
      <p id="menu-drinkOptions-hint" className={styles["menu-hint"]}>
        Choose one included drink (no extra cost). It is selected by default for customers, who can choose another drink.
        Remove all options for an item without a drink.
      </p>
      {options.map((option, index) => (
        <div key={index} className={styles["menu-drink-row"]}>
          <div className={styles["menu-field"]}><label htmlFor={`drink-label-${index}`}>Drink name *</label>
            <input id={`drink-label-${index}`} required maxLength={80} value={option.label}
              onChange={e => update(index, "label", e.target.value)} /></div>
          <div className={styles["menu-field"]}><label htmlFor={`drink-price-${index}`}>Add-on price (PHP) *</label>
            <input id={`drink-price-${index}`} type="number" min="0" max="10000" step="0.01" required value={option.addOn}
              onChange={e => update(index, "addOn", e.target.value)} /></div>
          <label className={styles["menu-checkbox"]}>
            <input type="radio" name="included-drink" aria-label={`Include drink ${index + 1} in base price`}
              checked={option.addOn !== "" && Number(option.addOn) === 0}
              onChange={() => onChange(includeDrink(options, index))} />Included</label>
          <Button variant="light" type="button" aria-label={`Remove drink ${index + 1}`}
            onClick={() => onChange(options.filter((_, i) => i !== index))}>Remove</Button>
        </div>
      ))}
      {!options.length && <p className={styles["menu-hint"]}>No drink selection required for this item.</p>}
      {error && <p className={styles["menu-field-error"]} id="menu-drinkOptions-error" role="alert">{error}</p>}
      <Button variant="light" type="button" disabled={options.length >= 10}
        onClick={() => onChange([...options, { label: "", addOn: options.length ? "" : "0" }])}>Add drink option</Button>
    </fieldset>
  );
}
