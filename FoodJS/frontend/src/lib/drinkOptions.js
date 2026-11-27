// Starter rows for new admin drafts. Customers default to the item's saved zero-add-on option.
export function newDrinkOptions() {
  return [
    { label: "Go Large Iced Tea", addOn: "50" },
    { label: "Go Medium Iced Tea", addOn: "45" },
    { label: "Go Regular Iced Tea", addOn: "0" },
  ];
}

export function drinkOptionsError(options) {
  if (options.length > 10) return "Add at most 10 drink options.";
  const labels = new Set();
  for (const option of options) {
    const label = option.label.trim();
    if (!label || label.length > 80) return "Each drink needs a name of 1 to 80 characters.";
    if (labels.has(label.toLowerCase())) return "Drink names must be unique.";
    labels.add(label.toLowerCase());
    if (!/^\d+(\.\d{1,2})?$/.test(String(option.addOn)) || Number(option.addOn) > 10000)
      return "Enter an add-on price from 0 to 10000, with up to two decimal places.";
  }
  if (options.length && options.filter(option => Number(option.addOn) === 0).length !== 1)
    return "Choose exactly one included drink. Other drinks must have a positive add-on price.";
  return null;
}

export function includeDrink(options, index) {
  return options.map((option, i) => ({ ...option, addOn: i === index ? "0" : Number(option.addOn) === 0 ? "" : option.addOn }));
}

export function drinkOptionsPayload(options) {
  return options.map(option => ({ label: option.label.trim(), addOn: Number(option.addOn) }));
}
