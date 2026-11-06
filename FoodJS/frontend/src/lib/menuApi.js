const burgerImg = "https://images.unsplash.com/photo-1568901346375-23c9450c58cd?auto=format&fit=crop&w=500&q=60";
const sidesImg = "https://images.unsplash.com/photo-1680016791602-3a160533b0a8?q=80&w=1170&auto=format&fit=crop&w=500&q=60";
const drinksImg = "https://images.unsplash.com/photo-1500217052183-bc01eee1a74e?q=80&w=688&auto=format&fit=crop&w=500&q=60";
const dessertsImg = "https://images.unsplash.com/photo-1495147466023-ac5c588e2e94?q=80&w=687&auto=format&fit=crop&w=500&q=60";

// Fetch menu items gikan backend API. search and category are done by the C# backend
export async function fetchMenuFromBackend({ search = "", category = "" } = {}) {
  try {
    const params = new URLSearchParams();
    if (search.trim()) params.set("search", search.trim());
    if (category && category !== "All") params.set("category", category);
    const response = await fetch(`/api/menu${params.size ? `?${params}` : ""}`);
    if (!response.ok) {
      throw new Error(`Menu API error: ${response.status}`);
    }
    const data = await response.json();
    return Array.isArray(data) ? data : [];
  } catch (error) {
    console.error("Failed to fetch menu:", error);
    return [];
  }
}


// Get categories gikan menu items

export function getCategoriesFromMenu(menuItems) {
  const categories = new Set();
  categories.add("All");
  menuItems.forEach((item) => {
    if (item.category) {
      categories.add(item.category);
    }
  });
  return Array.from(categories);
}

// Filter menu items by category

// one food item from the C# backend, null if not found
export async function fetchMenuItem(id) {
  try {
    const response = await fetch(`/api/menu/${encodeURIComponent(id)}`);
    return response.ok ? await response.json() : null;
  } catch (error) {
    console.error("Failed to fetch menu item:", error);
    return null;
  }
}

// Find menu item gamit ID

export function getMenuItemById(menuItems, id) {
  return menuItems.find((item) => item.id === id) || null;
}

export function formatPrice(value) {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    minimumFractionDigits: 2,
  }).format(Number(value) || 0);
}

// Add image URL sa menu item pende sa category

function getImageForCategory(category) {
  switch (category) {
    case "Burgers":
      return burgerImg;
    case "Sides":
      return sidesImg;
    case "Drinks":
      return drinksImg;
    case "Desserts":
      return dessertsImg;
    default:
      return burgerImg;
  }
}

export function enrichMenuWithImages(menuItems) {
  return menuItems.map((item) => ({
    ...item,
    image: item.image || getImageForCategory(item.category),
  }));
}
