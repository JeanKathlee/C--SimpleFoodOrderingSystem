namespace FoodJS.Api.Features.Menu;

public interface IMenuService
{
    IReadOnlyCollection<MenuItemResponse> GetAll();
    MenuItemResponse Create(SaveMenuItemRequest request);
    MenuItemResponse? Update(string id, SaveMenuItemRequest request);
    bool Delete(string id);
}
