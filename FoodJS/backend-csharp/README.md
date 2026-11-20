# FoodJS C# Backend Team Template

This folder is the shared starting point for migrating the existing Express API
to ASP.NET Core. The React frontend should not need to change: keep the same
URLs, HTTP methods, status codes, JSON property names, and response shapes.

The existing Node backend remains in `../backend` as the working reference while
the migration is in progress.

## Run the template

From `FoodJS`:

```powershell
npm run dev:backend:csharp
```

Or run the API directly:

```powershell
dotnet watch --project backend-csharp/FoodJS.Api/FoodJS.Api.csproj
```

The API listens on `http://localhost:3001`. Verify it at:

```text
GET http://localhost:3001/api/health
```

## Project organization

- `Controllers/` contains small cross-cutting controllers such as health checks.
- `Features/<Feature>/` owns a complete business feature: controller, request and
  response contracts, interface, and implementation.
- `Program.cs` contains dependency registration and the HTTP middleware pipeline.
- `appsettings*.json` contains safe, non-secret configuration.

The Menu feature is the example to copy. It is intentionally in-memory so the
template builds without database packages. Replace it with Entity Framework Core
and PostgreSQL after the API contracts have been migrated.

## Checkout item choices

Order requests accept an optional `selectedChoice` on each item. The server uses
the current menu price plus the recognized upgrade price: `Go Regular Iced Tea`
adds PHP 0, `Go Medium Iced Tea` adds PHP 45, and `Go Large Iced Tea` adds PHP 50.
Missing/blank choices keep the base menu price; unknown choices return HTTP 400.
Client-supplied item prices and add-on amounts do not determine the order total.
Keep these values aligned with `frontend/src/pages/ProductDetails.jsx`.

Items merge only when both the menu ID and selected choice match. The persisted
order line stores the choice and the complete unit price, including the upgrade.
Order responses return `selectedChoice` and that saved unit price in `price`.
The `PreserveItemChoices` migration adds nullable choice columns to order and cart
items, preserving existing rows without a choice.

The C# cart feature must preserve `selectedChoice` and distinguish variants when
adding, updating, or removing lines. Variant-specific cart operations are deferred
to that implementation and frontend integration. The existing frontend cart
mutation routes are unchanged. Keep `../backend/` as the migration reference;
implement new backend behavior in this C# project.

Run the SQLite order/pricing regression tests with:

```powershell
dotnet test backend-csharp/FoodJS.Backend.slnx
```

## Saved order checkout details

Orders store the submitted customer name and mobile number, plus the validated
coupon code. The existing `customerName`, `mobileNumber`, and `couponCode` response
fields read those saved values for details, tracking, and customer/admin lists.
Later profile edits or coupon renaming/deletion do not change historical orders.
An omitted mobile number is stored as an empty string; no applied coupon is null.

The `PreserveOrderCheckoutDetails` migration adds nullable columns to `Orders`.
For legacy rows it copies the current profile name and mobile number once, since
their original checkout values were never stored. Unrecoverable coupon codes
remain null. New orders save their actual checkout values when created.

## Suggested team ownership

- Lead: shared architecture, code review, configuration, deployment, and database
  migrations.
- Member 1: authentication and users.
- Member 2: menu and categories.
- Member 3: cart and orders.
- Member 4: coupons, notifications, and admin insights.

Adjust the assignments to match the size of the team. One feature should have
one primary owner to avoid conflicting edits.

## Existing API contract to preserve

- `/api/auth`
- `/api/users`
- `/api/menu`
- `/api/categories`
- `/api/cart`
- `/api/orders`
- `/api/coupons`
- `/api/notifications`
- `/api/admin-insights`
- `/api/health`

Before implementing an endpoint, read its matching file in `../backend/routes`.
Do not rename JSON fields unless the frontend is updated in the same pull request.

## Team rules

1. Branch from the C# migration branch using `feature/csharp-<feature>`.
2. Keep controllers thin; business rules belong in a service.
3. Use request and response DTOs instead of exposing database entities.
4. Use asynchronous methods for database and network operations.
5. Store secrets in environment variables, never in `appsettings.json`.
6. Return the same status codes and `{ "message": "..." }` errors as the Node API.
7. Build the solution before opening a pull request:

   ```powershell
   dotnet build backend-csharp/FoodJS.Backend.slnx
   ```

8. A migrated feature is complete only when its frontend flow works against the
   C# API and its automated tests pass.

## Recommended migration order

1. Database entities and Entity Framework Core setup.
2. JWT authentication, Google OAuth, and role authorization.
3. Users, menu, and categories.
4. Cart, coupons, and orders.
5. Notifications, email, and admin insights.
6. Deployment configuration and removal of the Node backend after acceptance.
