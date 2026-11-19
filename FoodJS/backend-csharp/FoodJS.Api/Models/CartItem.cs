namespace FoodJS.Api.Models;

public class CartItem
{
    public int Id { get; set; }

    public int ShoppingCartId { get; set; }

    public ShoppingCart ShoppingCart { get; set; } = null!;

    public int MenuItemId { get; set; }

    public MenuItem MenuItem { get; set; } = null!;

    public int Quantity { get; set; } = 1;

    // the meal option picked on the product page, e.g. "Go Large Iced Tea"
    public string? SelectedChoice { get; set; }
}