// Shared client-side paging. Never use pageItems to calculate cart/order totals.
export function paginateItems(items, requestedPage = 1, requestedSize = 10) {
  const pageSize = Number.isInteger(requestedSize) && requestedSize > 0 ? requestedSize : 10;
  const totalCount = items.length;
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const page = Math.max(1, Math.min(Number.isInteger(requestedPage) ? requestedPage : 1, totalPages));
  const offset = (page - 1) * pageSize;
  return { page, pageSize, totalCount, totalPages, pageItems: items.slice(offset, offset + pageSize) };
}
