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

The API listens on `http://localhost:3002`. Verify it at:

```text
GET http://localhost:3002/api/health
```

## Local development bridge

Run Express with `npm run dev:backend`, C# with `npm run dev:backend:csharp`,
and React with `npm run dev:frontend` in separate terminals from `FoodJS/`.
The frontend sends `/api/menu` and `/api/orders` requests to C# on port 3002;
the remaining APIs use Express on port 3001. Checkout submission uses C# orders.

The `migration` launch profile enables a Development-only dependency adapter
using a separate `foodjs.migration.db`. It imports the initial menu once, maps
reference categories to database IDs, refreshes coupon dependencies, and obtains
the current user's identity/role from Express `GET /api/users/me`. It never
grants an admin role from the browser's saved user object. Existing Express
orders are not imported. The adapter is temporary; production authentication and
the teammate-owned APIs still require integration.

Menu reads are public; adding/editing/deleting requires a valid admin session.
Express demo mode maps invalid or expired supplied tokens to its demo customer.
The browser may still display a cached admin dashboard while C# correctly
returns HTTP 403 for that customer. Sign out and sign in with the admin account
to refresh the session. Permission failures return a JSON message; the menu UI
also handles empty/non-JSON API errors without misreporting a connection failure.

## Project organization

- `Controllers/` contains small cross-cutting controllers such as health checks.
- `Features/<Feature>/` owns a complete business feature: controller, request and
  response contracts, interface, and implementation.
- `Program.cs` contains dependency registration and the HTTP middleware pipeline.
- `appsettings*.json` contains safe, non-secret configuration.

The Menu feature uses Entity Framework Core with SQLite and preserves the
existing menu response contracts.

## Menu Management pagination

The admin Menu Management screen uses `GET /api/menu/paged`. It accepts `page`
(default 1), `pageSize` (default 10, maximum 50), and optional `search`, `categoryId`,
and `isAvailable` filters. Search matches item names and descriptions. The response
contains `items`, `totalCount`, `page`, `pageSize`, and `totalPages`. Filtering,
counting, and page selection happen in the database; items sort by name then ID.
An out-of-range page is clamped to the last page, including after deletion.

The existing `GET /api/menu` array response remains available for existing callers.
The new admin screen requires this C# endpoint; the Express reference is unchanged.
Category loading still depends on the category API owned by the assigned teammate.

## Order Management pagination and details

- `GET /api/orders/paged` lists only the authenticated customer's orders.
- `GET /api/orders/admin/paged` lists all customers' orders for admins only.
- `GET /api/orders/admin/{id}` provides complete saved details for admins;
  the existing customer detail route keeps its ownership check.

Paged routes accept `page` (default 1), `pageSize` (10, maximum 50), `search`
(order ID or saved customer name, up to 120 characters), `status`, `fromDate`,
and `toDate`. Calendar dates use Philippine time (UTC+8), with an exclusive
next-day upper bound. Invalid ranges return HTTP 400. Results contain `items`,
`totalCount`, `page`, `pageSize`, and `totalPages`; summary items include order ID,
customer name, status, total, creation time, and the sum of item quantities.

SQLite parameterized queries compare/order creation instants with `julianday`
and apply `LIMIT/OFFSET` before materializing a page. Summary responses do not
load every order's item details or history. Sorting is newest first with ID as
the tie breaker; pages clamp after deletions. Existing array routes remain for
compatibility with other callers, including the separate dashboard analytics.

Admin UI and customer history use these paged routes. Admin details are fetched
on demand. Mutation controls lock during requests, cancellation requires
confirmation, and failed confirmations keep their error message and retry path.
Tracking shows saved delivery details, totals, item choices, and status history.
Notifications still depend on the teammate-owned API and the development bridge
does not migrate or synchronize that API's records.

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

## Verification of ARATIA's assigned features

Run the regression suite from `FoodJS/`:

```powershell
dotnet test backend-csharp/FoodJS.Backend.slnx
```

`AssignedApiFlowTests` exercises the real application startup, migrations,
controllers, JSON contracts, authorization policies, and services over an
in-process HTTP test server with isolated SQLite storage.

| Assignment | Automated coverage |
| --- | --- |
| Menu Management, WBS 21-25 | List, create, edit, availability, delete, description/image preservation, invalid inputs, and admin-only mutations. |
| Checkout, WBS 38-42 | Order submission, server pricing, coupons, saved delivery addresses, and rejection of unavailable items. Existing SQLite tests also verify choice and contact snapshots. |
| Order Management, WBS 44-50 | Customer/admin lists, details/tracking history, creation, status transitions, cancellation eligibility, ownership, and deletion with related records. |

Test authentication is defined only inside the test project; users, roles,
categories, and coupons are seeded as dependency data. This does not implement or
verify teammate-owned authentication/profile/cart/category/coupon/notification APIs.
Browser checkout summary/map behavior and full frontend-to-C# integration remain
team acceptance checks once those APIs are available. The suite currently has 107
passing cases; PR approval and merging remain with the project lead.

## Reviewed checkout and safe retries

The updated customer checkout uses authenticated `POST /api/orders/quote` with
`items` (menu ID, quantity, selected choice) and optional `couponCode`. It returns
current menu prices, complete item quantities, subtotal, discount, total, applied
coupon, and a protected `quoteToken` with a ten-minute expiry. Quoting does not
create an order, consume a coupon, save an address, or clear a cart.

Checkout sends that token and a UUID `idempotencyKey` with `POST /api/orders`.
The server recalculates pricing and rejects changed, expired, tampered, or
other-user reviews with HTTP 409 before saving. Reviewed checkout requires a
recipient, Philippine mobile number, delivery address, and cash payment; card
payments have no gateway and are disabled in the updated UI. Existing clients
without a quote retain the earlier request contract.

`GuardCheckoutSubmissions` adds a submission ledger keyed by user and UUID.
The ledger, order, items, payment, history, notification, and coupon usage commit
in the same SQLite transaction. A retry with the same details returns the saved
order even if menu prices/coupons later change. Different details with the same
key return 409. Ledger entries remain after order deletion so an old attempt
cannot recreate a deleted order. Data Protection protects quote tokens; deployed
instances must share a persisted key ring when serving the same database.

The browser stores the exact pending submission in session storage before
posting. Lost responses/timeouts can be retried with that same key after reload.
Order acceptance navigates directly to saved confirmation details. Bag cleanup
is a separate, retryable operation; failure never resubmits the order. Cleanup
keeps a changed bag and lets the customer explicitly retain it for later.

Checkout/review/confirmation and shared order details paginate item types at
10/20/50 rows while totals include all quantities. This bounds rendered rows;
the current teammate-owned cart API still returns the complete cart. Delivery
address search returns at most five results, and the optional map remains mounted
through unrelated form edits. No cart, profile, authentication, category, coupon
management, or notification endpoint is implemented by these changes.

Checkout HTTP tests cover quotes, stale/expired/other-user/tampered tokens,
contact validation, replay conflicts, deleted orders, and simultaneous retries
on separate SQLite connections. Browser QA uses isolated mock dependencies;
the running local bridge/proxy was also checked for successful quote responses.

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
