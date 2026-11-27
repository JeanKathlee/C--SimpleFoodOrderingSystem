# FoodJS frontend

The React client for FoodJS. It includes the public landing page, customer ordering flow, live order tracking, account screens, and the role-protected admin dashboard.

Run `npm run dev` for local development, `npm run lint` for static checks, and `npm run build` for a production bundle. API requests under `/api` are proxied to the ASP.NET Core backend at `http://localhost:3002` during development.

## Styling: Dashboard migration pilot

- `styles/tokens.css` holds shared color/theme values and is imported once in
  `main.jsx`. Existing customer-page layout CSS remains in place for a later pass.
- `pages/Dashboard.module.css` owns the admin layout, navigation, analytics, and
  shared admin panel/table styles. Admin components import its scoped classes.
- Menu Management, Category Management, Customers, and Order Management have
  their own `.module.css` files for feature-specific styles and responsive rules.
- `components/ui/Button.jsx` and `Modal.jsx` are reusable native-button and
  accessible-dialog components with their own CSS Modules. Checkout and order
  tracking share the same Modal without loading Dashboard styles.
- Use CSS Module imports for new component layout. Do not add global admin
  selectors or rely on a different page's stylesheet being loaded first.

This is a styling refactor, not a redesign or a backend architecture change.
Tests cover scoped class mappings, active/status styles, reusable UI contracts,
customer-dialog composition, and existing pagination/upload behavior.

Add/Edit Menu Item uses the shared wide modal, with a scrollable form, focused
name input, keyboard focus containment, and focus restoration on close. Closing
a changed draft requires confirmation; saving/image-reading blocks dismissal.
Nested confirmations retain the page scroll lock until every dialog is closed.

## List pagination and food uploads

Product Details preselects the menu item's saved Included drink (the option with
zero add-on price). Customers can switch to another drink; its add-on updates
the displayed total and is submitted to the cart. Items without drink options
do not require a drink. Changing products resets quantity and selects that
product's own included drink, rather than carrying over the previous choice.

Add/Edit Item has an **Includes a drink** toggle, off for new items. Enable it
for an explicitly configured meal/bundle; disabling it saves `drinkOptions: []`.
The drink editor is hidden while off, and toggling off/on retains an unsaved
custom draft. Removing the last option switches the toggle off. Category names
do not force bundles: beverages, sides, desserts, and a la carte food may all be
standalone. Beverage-size variations are not implemented by this toggle.

- Lists use the same Pagination component with 10, 20, or 50 records per page.
  The shared usePagination hook clamps the page after removal and resets it when
  the page size or search/category/customer context changes.
- Admin menu and main order/history lists use existing server-paged endpoints.
  Categories, customers and their orders, coupons, customer menu,
  saved addresses, and notification lists page the records already loaded in React.
  Order/checkout item lists and longer status histories use the same controls.
- Active cart/bag items are not paginated. My Bag shows the complete list in a
  keyboard-focusable scroll region with its header/subtotal/Check Cart outside
  that region. The full Cart uses a continuous list and sticky desktop summary.
- Customer Order History hides order/notification page controls for empty or
  single-page results. Empty searches offer Clear filters, not Browse menu;
  unfiltered empty history shows a neutral message. Multi-page histories retain
  server-side pagination so older orders stay accessible.
- Totals, checkout submissions, clear-cart, and mark-all-read still operate on
  the complete collection, not just the displayed page. Fixed-size dashboard
  previews, dropdown options, category tabs, and transient toasts are not paged.
- Food image uploads allow up to 5 MB (5 × 1024 × 1024 bytes), checked before
  reading the file and again in the C# MenuService. Profile photos retain 2 MB.

Run `npm test` for paging edge cases, real React list-rendering regressions, and
upload-boundary tests. Mounted interaction tests use the development-only jsdom
dependency; no real database or external API is required.

## Browser QA ladder

From this folder, run `npm.cmd run test:e2e` (Windows, .NET 10 SDK, installed
Google Chrome, and restored NuGet packages required). The Playwright runner
uses a clean Chrome session, not your personal browser profile or the CRX
extension. It starts its own API on port 3004 and Vite on port 5174, refusing
to reuse unknown servers on those ports.

Each run creates a fresh SQLite database under the API's ignored `.qa-runtime`
folder. Only synthetic development accounts/catalog data are seeded. SMTP,
Google OAuth, and bootstrap credentials are explicitly disabled for that API;
the normal app, phone tunnel, and their databases are not used. Never expose
this QA API through a public tunnel.

The suite covers page smoke checks, mocked signup/verification and mobile bag
behavior, real API-backed menu editing/cart/checkout/history, authorization,
and a small local response-time sample. It does not prove real email delivery,
Google sign-in, production load capacity, or complete security coverage.

Results are written to ignored `playwright-report/index.html` and
`test-results/results.json`, with screenshots/traces for failures. Open the
HTML report using `npx.cmd playwright show-report`; review artifacts before
sharing, because real-API test traces can contain synthetic session tokens.
Run `npm.cmd test` and `npm.cmd run lint` for component/unit and code-quality
checks. Backend tests use the .NET test runner, not Pytest, because the API is C#.
