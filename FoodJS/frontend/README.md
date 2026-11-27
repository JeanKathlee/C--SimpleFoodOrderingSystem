# FoodJS frontend

The React client for FoodJS. It includes the public landing page, customer ordering flow, live order tracking, account screens, and the role-protected admin dashboard.

Run `npm run dev` for local development, `npm run lint` for static checks, and `npm run build` for a production bundle. API requests under `/api` are proxied to the ASP.NET Core backend at `http://localhost:3002` during development.

## List pagination and food uploads

- Lists use the same Pagination component with 10, 20, or 50 records per page.
  The shared usePagination hook clamps the page after removal and resets it when
  the page size or search/category/customer context changes.
- Admin menu and main order/history lists use existing server-paged endpoints.
  Categories, customers and their orders, coupons, customer menu, cart/bag,
  saved addresses, and notification lists page the records already loaded in React.
  Order/checkout item lists and longer status histories use the same controls.
- Totals, checkout submissions, clear-cart, and mark-all-read still operate on
  the complete collection, not just the displayed page. Fixed-size dashboard
  previews, dropdown options, category tabs, and transient toasts are not paged.
- Food image uploads allow up to 5 MB (5 × 1024 × 1024 bytes), checked before
  reading the file and again in the C# MenuService. Profile photos retain 2 MB.

Run `npm test` for paging edge cases, real React list-rendering regressions, and
upload-boundary tests. No extra test dependencies or real database are required.
