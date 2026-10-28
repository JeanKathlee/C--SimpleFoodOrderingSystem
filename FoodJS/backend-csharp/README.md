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
