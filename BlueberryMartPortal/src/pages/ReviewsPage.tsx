import { useCallback, useEffect, useState } from 'react';
import { AdminReview, apiBase, deleteReview, listReviews, Page } from '../api';

const PAGE_SIZE = 25;

// Image paths are absolute URLs when stored in GCS (production) but relative
// (/images/reviews/…) when served by the API locally. Only prefix the API base
// for the relative case, otherwise the two URLs get concatenated into garbage.
function photoUrl(imagePath: string): string {
  return /^https?:\/\//i.test(imagePath) ? imagePath : `${apiBase}${imagePath}`;
}

export default function ReviewsPage() {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Page<AdminReview> | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      setData(await listReviews(page, PAGE_SIZE));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load reviews.');
    } finally {
      setLoading(false);
    }
  }, [page]);

  useEffect(() => { load(); }, [load]);

  async function onDelete(r: AdminReview) {
    if (!window.confirm(`Delete this review by ${r.userEmail}? This cannot be undone.`)) return;
    try {
      await deleteReview(r.id);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Delete failed.');
    }
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <section>
      <header className="page-head">
        <h1>Reviews</h1>
        {data && <span className="count">{data.total} total</span>}
      </header>

      {error && <p className="error">{error}</p>}

      <table className="grid">
        <thead>
          <tr>
            <th>Item</th><th>By</th><th>Rating</th><th>Comment</th><th>Photo</th><th>Date</th><th></th>
          </tr>
        </thead>
        <tbody>
          {data?.items.map((r) => (
            <tr key={r.id}>
              <td>{r.itemName}</td>
              <td>{r.userEmail}</td>
              <td>{'★'.repeat(r.rating)}{'☆'.repeat(Math.max(0, 5 - r.rating))}</td>
              <td className="comment">{r.comment}</td>
              <td>
                {r.imagePath
                  ? <a href={photoUrl(r.imagePath)} target="_blank" rel="noreferrer">view</a>
                  : '—'}
              </td>
              <td>{new Date(r.createdAt).toLocaleDateString()}</td>
              <td className="actions">
                <button className="btn small danger" onClick={() => onDelete(r)}>Delete</button>
              </td>
            </tr>
          ))}
          {!loading && data?.items.length === 0 && (
            <tr><td colSpan={7} className="empty">No reviews.</td></tr>
          )}
        </tbody>
      </table>

      <div className="pager">
        <button className="btn small" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>Prev</button>
        <span>Page {page} / {totalPages}</span>
        <button className="btn small" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>Next</button>
      </div>
    </section>
  );
}
