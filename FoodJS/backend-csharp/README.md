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
coupon usage, and submission ledger commit together. Drink choices are configurable
per menu item, with exactly one included (zero add-on) drink when options exist.
An empty or unconfigured list means no drink is included or required. New items
default to standalone; updates that omit drink options retain saved options.
The `NormalizeUnconfiguredDrinks` data migration changes only null legacy lists
to `[]`, leaving explicit bundles and completed order snapshots untouched.
Restart the API to apply it. Old active-cart choices removed by this correction
must be removed/re-added before checkout; completed orders remain historical.
Seed data explicitly includes drinks for the sample burgers and uses empty
lists for cola, shakes, fries, and cake. This is per-item configuration, not a
category-name rule. The admin **Includes a drink** toggle saves the same list;
the service validates it and the repository persists it. Beverage-size variants
are a separate feature and are not added by this change.

Google OAuth requires configured credentials and the allowed redirect URI.
Password reset uses SMTP-delivered, hashed one-time tokens. No environment
returns a raw recovery token in an API response. Both verification and recovery
use the same configured sender and trusted `Email:FrontendUrl`.

## Email verification

Public password signup creates a pending customer account, NOT a login session.
The response contains `verificationRequired`, `emailSent`, `email`, and `message`,
never a JWT or verification token. React opens `/verify-email`. The HTML email
shows a **Confirm email** button (with a plain-text fallback). The button's link
opens the same page, which automatically POSTs the one-time token after mounting.
Success shows **Email verified successfully!**; **OK** returns to `/login`.
Verification does not sign in automatically. Invalid/expired links offer resend.
Opening the route without a token does not verify or send anything automatically.
Security trade-off: a link scanner that runs JavaScript could consume a token;
the API still verifies via POST, never via GET, and tokens remain single-use.

During local testing, open the email on the computer running FoodJS. `localhost`
on a phone points to that phone, not the development computer. Changing the link
to a button cannot fix that network boundary. Phone access requires a reachable
HTTPS frontend origin configured as `Email:FrontendUrl`; no public tunnel or
firewall rule is enabled automatically. Loopback email links include this warning.

- `POST /api/auth/verify-email`: `{ email, token }`.
- `POST /api/auth/resend-verification`: `{ email }`; the public response is generic.
- Tokens use 32 random bytes, a SHA-256 hash at rest, a 30-minute expiry, and
  single-use consumption. Resend invalidates previous links and enforces a
  60-second per-account cooldown. An IP limit also covers registration/verification.
- Links are built from configured `Email:FrontendUrl`, not request headers. The
  token is in the URL fragment (not HTTP access logs); React removes it from the
  address/history immediately and never stores it in localStorage.
- Unverified password accounts cannot sign in or use old JWTs/password-reset
  tokens. Changing an email revokes access until the new email is verified and
  retires old verification/password-reset tokens. Soft-deleted accounts stay blocked.
- Versioned JWTs keep prior sessions revoked even after an email change is verified.
  Existing pre-migration sessions must sign in again; password reset also revokes
  previously issued sessions.
- Google-verified new accounts do not need a second email. Google login cannot
  activate an existing unverified password account; verify that account first.
- Existing administrators are retained as verified by migration. Existing real
  customers must use the resend flow. Development demo accounts are deliberately
  pre-verified; production public registrations never have that exemption.

### Configure real delivery (local development)

Google OAuth `Google:ClientSecret` is NOT an SMTP password. Configure an actual
sender's SMTP credentials privately, outside source control. From `FoodJS/`:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Configure-Email.ps1

The helper defaults to Gmail SMTP on port 587 and prompts for the sender email
and SMTP app password using hidden input. For another provider, pass `-SmtpHost`
and `-SmtpPort`; if the login differs from the From address, set the corresponding
user-secret separately. This command bypasses execution policy for this invocation
only; it does not change the machine's persistent policy.

Settings: `Email:FromAddress`, `Email:Smtp:Host`, `Email:Smtp:Port`,
`Email:Smtp:Username`, `Email:Smtp:Password`, and `Email:FrontendUrl`.
The transport requires STARTTLS (normally port 587, not implicit-TLS port 465).
For production, use deployment secrets and an HTTPS frontend origin. .NET user
secrets are local developer storage, not an encrypted production secret vault.

Missing configuration returns HTTP 503 before signup creates an account. A
delivery failure after creation leaves the account pending, explicitly reports
`emailSent: false`, and supports recovery via resend. Never pretend that delivery
succeeded, log email links/passwords, or commit credentials. Restart the backend
after setup; startup applies `AddEmailVerification` and `AddAccountSessionVersion`
automatically.

The automated tests capture emails in memory, not a real inbox. Real SMTP/inbox
delivery must be tested with your sender after configuration.

### Forgot password

`POST /api/auth/forgot-password` sends an HTML **Reset password** button (with a
plain-text link fallback) to eligible, verified password accounts. Missing,
unverified, Google-only, and throttled accounts receive the same public message.
Missing SMTP/trusted-origin configuration returns 503 for every email; delivery
failures are logged without recipients or links and invalidate the failed token.

Links use `/reset-password#email=...&token=...`; the fragment is not included in
HTTP requests, and React removes it from browser history immediately. The page
does not reset anything until the user submits matching new passwords. Tokens
are random, hashed with their email binding in SQLite, expire after 30 minutes,
and are single-use. Requesting another link after the 60-second cooldown retires
the previous one. Both recovery endpoints allow 10 requests per IP/path per
15 minutes (reverse-proxy client IP handling should be reviewed for deployment).

`POST /api/auth/reset-password` atomically changes the password, retires outstanding
reset links, and increments `AuthVersion` to revoke existing JWT sessions. A
password-changed notification is sent separately; notification failure does not
undo a successful reset. The View displays success and **OK** returns to login,
without automatically signing the user in. No records are permanently deleted.
Old development-only query-string links must be replaced by requesting a new email.

Controller → PasswordResetService → IUserRepository/IUnitOfWorkRepository is the
required flow. Existing SMTP user-secrets are reused; never commit credentials.
For a phone demo, `Email:FrontendUrl` must match the active public HTTPS tunnel,
and a new link is required if that tunnel changes. Restart the backend/demo API
after updating its code; no additional database migration is needed for recovery.

Recovery design reference: [OWASP Forgot Password Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html).

## Verification

### Temporary phone demo

Use a Quick Tunnel only with test data. It publishes a built React UI and a
separate Production-mode API on loopback port 3003, NOT the Vite development
server. No firewall/router changes are needed. Normal localhost settings and
data are not modified; a new `.demo-runtime/phone-demo-<unique-id>.db` is created
on each start. Existing users, orders, and verification tokens are NEVER copied.
Only sample catalog/categories/coupons are seeded; no admin/demo accounts.
Use the SAME public URL on both the computer and phone during the demo.

From `FoodJS/`, build first:

    $env:VITE_DISABLE_GOOGLE_AUTH = "true"
    try { npm.cmd run build:frontend } finally { Remove-Item Env:\VITE_DISABLE_GOOGLE_AUTH -ErrorAction SilentlyContinue }
    dotnet build backend-csharp/FoodJS.Api/FoodJS.Api.csproj -c Release

Start `cloudflared tunnel --url http://127.0.0.1:3003 --protocol http2 --no-autoupdate`
and keep it running. In another terminal, pass its exact HTTPS URL:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-PhoneDemo.ps1 -PublicUrl "https://YOUR-ASSIGNED-HOST.trycloudflare.com"

With the sender owner's permission, the helper privately imports only the
project's SMTP user-secrets, supplies the public verification origin only to
this child process, and creates
a fresh random demo JWT key. It does not overwrite user-secrets or weaken Vite's
allowed-host rules. No development reset tokens are returned from this API.
Previously sent localhost verification links must be replaced using Resend at
the public demo URL. Register a NEW account in this demo first: localhost
accounts are not present here. Changes affect only the demo DB. Anyone with
the public URL can register and trigger verification emails (rate limited).

Email/password sign-in and verification work without Google Console changes.
This helper intentionally disables Google sign-in and does not import OAuth
secrets. Enabling it requires a separate setup and the exact public
`/auth/callback` URL authorized on the Google OAuth web client. Each Quick Tunnel has
a different URL; old links/callback entries are not automatically updated.

The URL stops working when the tunnel or API stops. Keep the computer awake and
connected. If started with `-TunnelProcessId`, `Stop-PhoneDemo.ps1` can stop both
processes after verifying their identities; otherwise stop the tunnel terminal
with Ctrl+C as well. This is a temporary public demo, not production deployment.

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
