import { useState } from "react";

// Splits a list into pages. Goes back to page 1 when resetKey changes (e.g. the filters).
// When the list shrinks (an item was deleted), the page is clamped instead of reset.
export default function usePagination(items, pageSize, resetKey = "") {
  const [page, setPage] = useState(1);
  const [lastKey, setLastKey] = useState(resetKey);

  if (resetKey !== lastKey) {
    setLastKey(resetKey);
    setPage(1);
  }

  const totalPages = Math.max(1, Math.ceil(items.length / pageSize));
  const current = Math.min(page, totalPages);
  const start = (current - 1) * pageSize;

  return {
    page: current,
    totalPages,
    total: items.length,
    from: items.length === 0 ? 0 : start + 1,
    to: Math.min(start + pageSize, items.length),
    pageItems: items.slice(start, start + pageSize),
    setPage,
  };
}
