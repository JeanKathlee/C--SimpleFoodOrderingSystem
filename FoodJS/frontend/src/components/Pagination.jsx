import { useId } from "react";

export default function Pagination({ page, pageSize, totalCount, totalPages, onPageChange, onPageSizeChange, disabled = false, label = "Items" }) {
  const sizeId = useId();
  const first = totalCount ? (page - 1) * pageSize + 1 : 0;
  const last = Math.min(page * pageSize, totalCount);
  const start = Math.max(1, Math.min(page - 2, totalPages - 4));
  const pages = Array.from({ length: Math.min(5, totalPages) }, (_, i) => start + i);
  return (
    <nav className="list-pagination" aria-label={`${label} pagination`}>
      <p aria-live="polite">{first}–{last} of {totalCount} {label.toLowerCase()}</p>
      <div className="pagination-size"><label htmlFor={sizeId}>Per page</label>
        <select id={sizeId} value={pageSize} disabled={disabled} onChange={e => onPageSizeChange(Number(e.target.value))}>
          {[10, 20, 50].map(size => <option key={size} value={size}>{size}</option>)}
        </select>
      </div>
      <div className="pagination-buttons">
        <button type="button" disabled={disabled || page === 1} onClick={() => onPageChange(1)} aria-label="First page">«</button>
        <button type="button" disabled={disabled || page === 1} onClick={() => onPageChange(page - 1)} aria-label="Previous page">Previous</button>
        {pages.map(number => <button type="button" key={number} aria-label={`Page ${number}`} aria-current={number === page ? "page" : undefined}
          disabled={disabled} onClick={() => onPageChange(number)}>{number}</button>)}
        <button type="button" disabled={disabled || page >= totalPages} onClick={() => onPageChange(page + 1)} aria-label="Next page">Next</button>
        <button type="button" disabled={disabled || page >= totalPages} onClick={() => onPageChange(totalPages)} aria-label="Last page">»</button>
      </div>
    </nav>
  );
}
