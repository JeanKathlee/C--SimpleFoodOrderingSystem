# FoodJS ASP.NET Core Backend

FoodJS uses React for the View and ASP.NET Core API controllers for HTTP requests.
Every feature follows this dependency direction:

    React View -> Controller -> Service -> Repository -> EF Core / SQLite

API controllers return JSON for React. There are no Razor views.

## Layer responsibilities

- Controllers bind input, read authenticated identity/roles, call services, and
  return HTTP responses. They do not validate business rules or access data.
- Services validate inputs, make business decisions, calculate prices/discounts,
  enforce ownership and status rules, and map entities to response contracts.
- Repositories execute queries, track additions/updates, load related records,
  and persist changes. They return materialized records, never IQueryable.
- IUnitOfWorkRepository owns SaveChanges, migrations, and database transactions.
  All repositories share one scoped DbContext. Services select the workflow's
  transaction boundary through this abstraction.
- Models represent persisted entities; contracts represent request/response data.
  Services assign business values to entities. EF tracking and persistence
  happen in repositories.

## Layer folders and feature locations

The API root is backend-csharp/FoodJS.Api/.

- Controllers/AuthController.cs -> Services/AuthService.cs -> Repositories/UserRepository.cs
- Controllers/UsersController.cs -> Services/UsersService.cs -> Repositories/UserRepository.cs
- Controllers/MenuController.cs -> Services/MenuService.cs -> Repositories/MenuRepository.cs
- Controllers/CategoriesController.cs -> Services/CategoryService.cs -> Repositories/CategoryRepository.cs
- Controllers/CartController.cs -> Services/CartService.cs -> Repositories/CartRepository.cs
- Controllers/OrdersController.cs -> Services/OrderService.cs -> Repositories/OrderRepository.cs
- Controllers/CouponController.cs -> Services/CouponService.cs -> Repositories/CouponRepository.cs
- Controllers/NotificationsController.cs -> Services/NotificationService.cs -> Repositories/NotificationRepository.cs
- Controllers/AdminInsightsController.cs -> Services/AdminInsightsService.cs -> Repositories/AdminInsightsRepository.cs
- Controllers/HealthController.cs -> Services/HealthService.cs

Models/ holds database entities, Contracts/ holds request and response DTOs,
Data/ holds the EF context and independent JSON seed files, and Migrations/
holds database schema history. Service interfaces live alongside their service
implementations under Services/. React remains in ../frontend/src/; a Razor
Views/ folder is not needed for the accepted React View layer.

Health has no repository because it does not access the database.
ReferenceDataRepository supplies shared category/user/coupon/address/status data.
Pure pricing and status rules (ItemChoiceRules, CouponRules, CartOptionPricing,
and OrderStatusRules) live under Services/. Program.cs registers dependencies,
configures HTTP/authentication, and calls startup services/repository infrastructure.

## Shopping cart example for the defense

1. ProductDetails.jsx posts a menu ID, quantity, and selected drink to /api/cart/items.
2. CartController.Add forwards the request and authenticated user ID to ICartService.
3. CartService validates quantity (1-99), requires a recognized drink, requests
   menu/cart records, and calculates the authoritative unit price.
4. CartRepository loads matching records and stages new cart/items. CartService
   decides whether to merge quantities or keep different drink choices separate.
5. CartService asks IUnitOfWorkRepository to save. The controller returns HTTP 201.
6. React refreshes the bag. Browser-supplied prices never determine server totals.

List, update, remove, and clear use these same layers and enforce ownership.

## PR #4 integration

Reviewed commit: 51c64eb5894e596f8e4604444214e1a813ac3b43.
Its Menu/Order/ReferenceData/UnitOfWork repository design and retained-record
behavior were integrated locally by adapting the source changes.
This is source integration, not a GitHub merge or push. The older PR code was
not copied over the newer checkout, contact snapshots, choice pricing,
pagination, idempotency, or password-recovery implementations.

EfMenuService was consolidated into MenuService. MenuController now uses one
IMenuService for reads and mutations. Repositories were added for all remaining
features, auth development seeds, and coupon/status seeds. The former Features/
layout has been removed; namespaces, imports, registrations, and test references
use the separate layer folders.

## Soft deletion for every application record

Every persisted application entity implements Models/ISoftDeletable.cs and has
an IsDeleted column defaulting to false. Deleting a record sets that flag to true;
it never removes the row. This applies to users, categories, coupons, menu items,
orders, cart lines (including Clear), and removal of a saved profile address.
The remaining related and lookup entities also carry the flag for consistency.

- Services decide when deletion is allowed and set IsDeleted. Repositories own
  persistence and expose no physical Remove/RemoveRange or bulk DELETE methods.
- Normal reads/updates exclude deleted records through EF query filters. Deleted
  parent categories also hide their menu items and related cart lines.
- Data/FoodJSDbContext.cs converts accidental EF Deleted states into flag-only
  updates for synchronous and asynchronous saves. EF cascade/orphan deletion is
  deferred, and all foreign keys use Restrict to protect retained relationships.
- Deleted accounts cannot log in, reset passwords, or use existing JWTs. Account
  deletion retains all linked records; administrators can still review past orders.
- Historical order details load retained addresses/statuses explicitly without
  joining the live user, menu, or coupon. Saved delivery, prices, names, choices,
  and discounts remain readable after related records are deleted.
- Cart re-add creates a new active row rather than reviving an old line. Clearing
  a bag flags only that customer's lines, including hidden items.
- Deleted emails and coupon codes remain reserved. Demo seeding checks retained
  records so startup cannot silently recreate deleted accounts, menu, or coupons.
- Checkout submission ledgers are consulted even when flagged, so safe retries
  cannot create duplicate orders. Deleted-order replay still returns HTTP 409.
- Categories with active menu items still return HTTP 409. Retained deleted menu
  items do not prevent an otherwise-empty category from being soft-deleted.
- IsAvailable, IsActive, Archived, and Cancelled remain separate business states.

SoftDeleteAllRecords adds the remaining flags and restrictive foreign keys on top
of RetainMenuAndOrders and ProtectRetainedRelationships. Existing records retain
their data and receive false flags. No public restore/purge endpoint is added;
explicitly restoring a permitted record means setting its flag back to false.

Migrations are tested on isolated SQLite databases, including upgrades and
rollback/reapply of legacy schemas. Startup applies pending migrations on the
next normal run. Stop old servers and back up important data before upgrading.

## Run locally

From FoodJS/, start these commands in separate terminals:

    npm run dev:backend
    npm run dev:frontend

React: http://localhost:5173
API: http://localhost:3002
Health: http://localhost:3002/api/health

The backend command explicitly selects the http launch profile.
To run directly:

    dotnet watch --project backend-csharp/FoodJS.Api/FoodJS.Api.csproj --launch-profile http

Development accounts: admin@foodjs.demo / admin123 and
customer@foodjs.demo / customer123. The old Express backend, development migration
adapter, and migration launch profile have been retired. Their files (including
local Express JSON data) are preserved at the repository root in the ignored
.legacy-backups/express-20261125-1fb1078e/ directory. The active C# backend and
its Data/Seed files run independently of that backup. The four tests for the
retired adapter were removed; the active backend regression tests remain.

## Preserved behavior

Existing /api/users, /api/auth, /api/menu, /api/categories, /api/cart,
/api/orders, /api/coupons, /api/notifications, /api/admin-insights, and
/api/health routes keep React-compatible contracts.

Menu paging supports search, category, availability, page, and pageSize (1-50).
Public/customer paging cannot expose unavailable items; admins can filter them.
Order paging supports owner/admin scope, search, status, and Philippine calendar
dates. Repositories apply parameterized SQL and LIMIT/OFFSET before materializing
pages. Services validate filters, clamp pages, and produce response DTOs.

Checkout uses server prices, ten-minute quote review tokens, and UUID submission
keys for safe retries. Order, items, payment, history, notification, address,
coupon usage, and submission ledger commit together. Drink upgrades are regular
+PHP 0, medium +PHP 45, and large +PHP 50. Cart additions require a drink; legacy
order requests without a choice remain compatible.

Google OAuth requires configured credentials and the allowed redirect URI.
Password reset uses hashed one-time tokens. Development/Testing returns tokens
for local testing; production email delivery still needs integration.
These external configuration requirements are unchanged by this refactor.

## Verification

From FoodJS/:

    dotnet test backend-csharp/FoodJS.Backend.slnx -c Release
    npm run build
    npm run lint --prefix frontend

ArchitectureBoundaryTests checks service-only controller dependencies, no EF
references or database queries in service/controller source files,
and no deferred query/EF types exposed by repository contracts.

AssignedRepositoryFlowTests adapts the PR tests and verifies retained data,
ownership, pricing, transaction rollback after a save failure, restrictive
foreign keys, seed behavior, and real schema upgrades. Other tests cover auth,
password recovery, cart variants, review tokens, contact snapshots, pagination,
and concurrent/replayed checkout submissions.

SoftDeletePersistenceTests verifies all application entity flags and foreign keys,
sync/async delete safeguards, restoration, and real migration of retained data.
SoftDeleteApiTests uses real JWT authentication to verify delete paths, revoked
account access, historical orders, reserved identities, and retained cart rows.

Tests do not mutate your normal application database.
