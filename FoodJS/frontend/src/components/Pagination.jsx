// buttonClass reuses the page's own buttons: "admin-btn admin-btn-light" or "client-btn"
export default function Pagination({ pager, buttonClass, label = "items" }) {
  if (pager.totalPages <= 1) {
    return null;
  }

  return (
    <nav className="pager" aria-label={`${label} pages`}>
      <span className="pager-status" aria-live="polite">
        Showing {pager.from}–{pager.to} of {pager.total} {label}
      </span>
      <div className="pager-controls">
        <button
          type="button"
          className={buttonClass}
          onClick={() => pager.setPage(pager.page - 1)}
          disabled={pager.page === 1}
        >
          Previous
        </button>
        <span className="pager-page">
          Page {pager.page} of {pager.totalPages}
        </span>
        <button
          type="button"
          className={buttonClass}
          onClick={() => pager.setPage(pager.page + 1)}
          disabled={pager.page === pager.totalPages}
        >
          Next
        </button>
      </div>
    </nav>
  );
}
