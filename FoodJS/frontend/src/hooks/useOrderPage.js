import { useEffect, useState } from "react";
import { orderRequest } from "../lib/orders";

const DEFAULT_FILTERS = { search: "", status: "All", fromDate: "", toDate: "", page: 1, pageSize: 10 };
const EMPTY = { items: [], totalCount: 0, page: 1, pageSize: 10, totalPages: 1 };
export default function useOrderPage(endpoint, pollMs = 15000) {
  const [filters, setFilters] = useState(DEFAULT_FILTERS);
  const [search, setSearch] = useState("");
  const [data, setData] = useState(EMPTY);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [hasLoaded, setHasLoaded] = useState(false);
  const [revision, setRevision] = useState(0);
  const { page, pageSize, status, fromDate, toDate } = filters;
  const rangeError = fromDate && toDate && fromDate > toDate ? "From date must be on or before To date." : "";
  useEffect(() => { const timer = setTimeout(() => setSearch(filters.search.trim()), 300); return () => clearTimeout(timer); }, [filters.search]);
  useEffect(() => {
    if (rangeError) { setLoading(false); return; }
    let controller;
    let disposed = false;
    const load = async () => {
      controller?.abort(); controller = new AbortController(); const signal = controller.signal;
      setLoading(true);
      const params = new URLSearchParams({ page, pageSize });
      if (search) params.set("search", search);
      if (status !== "All") params.set("status", status);
      if (fromDate) params.set("fromDate", fromDate);
      if (toDate) params.set("toDate", toDate);
      try {
        const body = await orderRequest(`${endpoint}?${params}`, { signal });
        if (!Array.isArray(body.items) || !Number.isInteger(body.totalCount)) throw new Error("The orders response was incomplete. Please retry.");
        if (!disposed && !signal.aborted) { setData(body); setError(""); setHasLoaded(true); }
      } catch (failure) { if (!disposed && !signal.aborted) setError(failure.message); }
      finally { if (!disposed && !signal.aborted) setLoading(false); }
    };
    load(); const timer = setInterval(load, pollMs);
    window.addEventListener("focus", load);
    return () => { disposed = true; controller?.abort(); clearInterval(timer); window.removeEventListener("focus", load); };
  }, [endpoint, page, pageSize, status, fromDate, toDate, search, revision, rangeError, pollMs]);
  return { data, filters, loading, error: rangeError || error, hasLoaded,
    setFilter: (field, value) => setFilters(current => ({ ...current, [field]: value, page: field === "page" ? value : 1 })),
    clearFilters: () => setFilters(current => ({ ...DEFAULT_FILTERS, pageSize: current.pageSize })),
    refresh: () => setRevision(value => value + 1) };
}
