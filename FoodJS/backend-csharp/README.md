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
- `Repositories/` contains assigned-feature database queries, persistence, and
  transaction infrastructure. Services use its interfaces rather than DbContext.
- `Program.cs` contains dependency registration and the HTTP middleware pipeline.
- `appsettings*.json` contains safe, non-secret configuration.

The Menu feature is the example to copy. The menu list (`GET /api/menu`) now comes
from the database (`Features/Menu/MenuService.cs`). Customers only see available
items; a logged-in admin sees hidden ones too. Search and category filter are done
here too: `GET /api/menu?search=fries&category=Sides`. One item: `GET /api/menu/{id}`. On the first run the old Node menu in
`Data/Seed/*.json` is copied into the database. Add, edit, change availability and
delete (admin only) are in `Features/Menu/MenuService.cs`. Menu ids look like `M-1`
and category ids like `cat-1` (`Common/PublicId.cs`).

Other features already in C#: `Features/Coupons` (`/api/coupons`), `Features/Orders`
(`/api/orders`), and `Features/Users` (including admin `PATCH /api/users/{id}` to edit a
customer). Tests: `dotnet test FoodJS.Backend.slnx`.

## Menu, Checkout, and Order repository boundaries

These assigned features follow **Controller = Request, Service = Logic,
Repository = Database**:

- `MenuController` uses one `IMenuService` for reads and mutations. The former
  `EfMenuService` was consolidated into `MenuService`. Controllers bind requests,
  obtain identity/role context, and return HTTP responses.
- `MenuService`, `OrderService`, and `OrderStatusSeeder` have no DbContext or EF
  queries. Validation, image rules, visibility decisions, pricing, coupon rules,
  ownership, order transitions, entity changes, and response calculations stay
  in services/rule classes.
- `IMenuRepository`, `IOrderRepository`, and `IReferenceDataRepository` expose
  materialized entities/record groups and persistence operations, never
  `IQueryable`. Repositories translate requested filters into queries and load
  related records; they do not decide eligibility, prices, or allowed transitions.
- `IUnitOfWorkRepository` owns SaveChanges, schema migration, and EF transaction
  infrastructure. All repositories are scoped and share one DbContext. Checkout
  starts its transaction before dependency reads and commits the complete order,
  items, payment, history, notification, address, and coupon usage together.
- Menu/status initialization now uses repository interfaces. Menu seed existence
  checks deliberately include deleted records, so an emptied menu is not reseeded.

### Soft deletion and migration

`20261123040403_RetainMenuAndOrders` adds `IsDeleted = false` to existing menu and
order rows and replaces destructive foreign-key cascades with restrictive
relationships protecting retained records. Startup applies the migration through
the unit-of-work repository. SQLite rebuilds affected tables to change foreign
keys; back up persistent databases and stop other writers before upgrading.

Menu/order DELETE routes now set `IsDeleted = true` and update the timestamp.
Normal reads, details, updates, cart menu lookups, checkout, and order aggregates
exclude deleted records through EF query filters. `IsAvailable`, `Archived`, and
Cancelled keep their separate meanings. There is no new restore/purge endpoint.

Deleting an ordered menu item is now allowed: the menu row remains, and order
details use saved item names/prices without joining the deleted live menu item.
Deleting an order retains its items, payment, status history, address, and linked
notifications. Child read filters follow order deletion, not menu deletion.
Explicit administrative database inspection can use `IgnoreQueryFilters` to see
retained rows; it is not exposed as an unrestricted HTTP option.

Category/user physical deletion cannot cascade through retained menu/orders.
Foreign-key conflicts return HTTP 409 with a message explaining that retained
data still references the parent. These owners must migrate their own deletion
and repository implementations separately. Cart, auth/users, categories,
coupons, notifications, insights, and auth development initialization were not
refactored into repositories by this assigned-feature change. Shared query
filters and foreign-key protection affect their reads/deletes automatically;
this is not a claim of application-wide architecture compliance.

### Verification

`AssignedRepositoryFlowTests` uses isolated SQLite with real migrations and
repositories. It covers menu validation/visibility/edit preservation, deletion
retention and access rejection, history after menu deletion, checkout totals,
ownership/status/cancellation rules, rollback after an injected second-save
failure, parent foreign-key protection, seed behavior, and upgrading a complete
legacy order graph. The suite has 52 passing cases (15 added by this refactor).

Real HTTP smoke checks also ran against the actual application with JWT admin
and customer sessions on a separate QA database. They verified status codes,
checkout, soft deletes, preserved item snapshots, and translated parent-delete
conflicts. No normal application data was mutated by those checks.

## Suggested team ownership

- Lead: shared architecture, code review, configuration, deployment, and database
  migrations.
- Member 1: authentication and users.
- Member 2: menu and categories.
- Member 3: cart and orders.
- Member 4: coupons, notifications, and admin insights.

Adjust the assignments to match the size of the team. One feature should have
one primary owner to avoid conflicting edits.

## Authentication and authorization

Normal login is `POST /api/users/login` and Google login is `POST /api/auth/google/callback`.
Both send back `{ message, token, user }`. The React app saves the token and sends it with
every request. Logout happens only in the React app: it deletes the saved token. A token
stops working after 1 hour.

To lock your endpoints (same as `verifyToken` and `requireAdmin` in the old Node code):

```csharp
[Authorize]                                  // must be logged in
[Authorize(Roles = AuthService.AdminRole)]   // must be an admin
var userId = User.GetUserId();               // id of whoever is logged in
```

User account endpoints (`Features/Users`):

- `GET /api/users`: list all users (admin only)
- `POST /api/users/register`: make a new customer account and log them in
- `GET /api/users/me` and `PATCH /api/users/me`: see or edit your own account
- `DELETE /api/users/{id}`: delete a customer (admin only). Admins can't be deleted, and you
  can't delete yourself. Deleting a user also deletes their orders, cart, and addresses.

When saving a new password (like on register), use
`AuthService.Hasher.HashPassword(user, password)`, or login won't work for that user.

When you run the API on your computer, it makes the tables and adds 2 demo users:

- admin: `admin@foodjs.demo` / `admin123`
- customer: `customer@foodjs.demo` / `customer123`

Don't put secrets in `appsettings.json`. Set them like this:

```powershell
dotnet user-secrets set "Google:ClientSecret" "<secret>" --project backend-csharp/FoodJS.Api
dotnet user-secrets set "Jwt:Secret" "<32+ random characters>" --project backend-csharp/FoodJS.Api
```

On your computer, `Jwt:Secret` has a test value already, so you can skip it. On a real
server you have to set it.

To test login, start the API and run `pwsh backend-csharp/auth-check.ps1`.

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
