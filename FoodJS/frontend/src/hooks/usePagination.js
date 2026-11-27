import { useState } from "react";
import { paginateItems } from "../lib/pagination";

// Splits a list into pages. Goes back to page 1 when resetKey changes (e.g. the filters).
// When the list shrinks (an item was deleted), the page is clamped instead of reset.
export default function usePagination(items, initialPageSize = 10, resetKey = "") {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(initialPageSize);
  const [lastKey, setLastKey] = useState(resetKey);

  if (resetKey !== lastKey) {
    setLastKey(resetKey);
    setPage(1);
  }

  const result = paginateItems(items, resetKey !== lastKey ? 1 : page, pageSize);
  if (page !== result.page) setPage(result.page);

  return {
    ...result,
    onPageChange: setPage,
    onPageSizeChange: value => { setPageSize(value); setPage(1); },
  };
}
