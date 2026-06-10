import { useCallback, useEffect, useState } from 'react';
import { Branch, getBranches, getSalesReport, SalesReport } from '../api';
import { getBranchId, isAdmin } from '../auth';

function isoDaysAgo(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() - days);
  return d.toISOString().slice(0, 10);
}

function csvCell(v: string | number): string {
  const s = String(v);
  return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

function buildCsv(report: SalesReport, branchLabel: string): string {
  const from = report.from.slice(0, 10);
  const to = report.to.slice(0, 10);
  const rows: (string | number)[][] = [
    ['Blueberry Mart — Sales report'],
    ['Branch', branchLabel],
    ['From', from, 'To', to],
    [],
    ['Total revenue', report.totalRevenue],
    ['Paid orders', report.orderCount],
    ['Average order value', report.averageOrderValue],
    [],
    ['Orders by status'],
    ['Status', 'Count'],
    ...report.byStatus.map((s) => [s.status, s.count]),
    [],
    ['Top items'],
    ['Item', 'Quantity sold', 'Revenue'],
    ...report.topItems.map((t) => [t.itemName, t.quantitySold, t.revenue]),
  ];
  return rows.map((r) => r.map(csvCell).join(',')).join('\n');
}

function downloadCsv(filename: string, content: string): void {
  const blob = new Blob([content], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

export default function ReportsPage() {
  const admin = isAdmin();
  const [from, setFrom] = useState(isoDaysAgo(30));
  const [to, setTo] = useState(isoDaysAgo(0));
  const [branchFilter, setBranchFilter] = useState('');
  const [branches, setBranches] = useState<Branch[]>([]);
  const [report, setReport] = useState<SalesReport | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const res = await getSalesReport({
        from,
        to,
        branchId: admin ? (branchFilter || undefined) : undefined,
      });
      setReport(res);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load report.');
    } finally {
      setLoading(false);
    }
  }, [from, to, branchFilter, admin]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { getBranches().then(setBranches).catch(() => setBranches([])); }, []);

  const branchLabel = admin
    ? (branchFilter ? (branches.find((b) => b.id === branchFilter)?.name ?? 'Selected branch') : 'All branches')
    : (branches.find((b) => b.id === getBranchId())?.name ?? 'Your branch');

  function onExport() {
    if (!report) return;
    downloadCsv(`sales-report_${report.from.slice(0, 10)}_${report.to.slice(0, 10)}.csv`, buildCsv(report, branchLabel));
  }

  return (
    <section>
      <header className="page-head">
        <h1>Reports</h1>
        <button className="btn push-right" disabled={!report} onClick={onExport}>Export CSV</button>
      </header>

      <div className="filters">
        <label className="check">From <input type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} /></label>
        <label className="check">To <input type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} /></label>
        {admin && (
          <select value={branchFilter} onChange={(e) => setBranchFilter(e.target.value)}>
            <option value="">All branches</option>
            {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </select>
        )}
      </div>

      {error && <p className="error">{error}</p>}
      {loading && !report && <p className="muted">Loading…</p>}

      {report && (
        <>
          <div className="stats">
            <div className="stat"><span className="stat-num">Rs {report.totalRevenue.toFixed(2)}</span><span className="stat-label">Revenue (paid orders)</span></div>
            <div className="stat"><span className="stat-num">{report.orderCount}</span><span className="stat-label">Paid orders</span></div>
            <div className="stat"><span className="stat-num">Rs {report.averageOrderValue.toFixed(2)}</span><span className="stat-label">Avg order value</span></div>
          </div>

          <div className="report-grid">
            <div className="card report-card">
              <h3>Orders by status</h3>
              {report.byStatus.length === 0 ? <p className="muted">No orders in range.</p> : (
                <table className="od-items">
                  <tbody>
                    {report.byStatus.map((s) => (
                      <tr key={s.status}>
                        <td><span className={`pill status-${s.status}`}>{s.status}</span></td>
                        <td className="od-price">{s.count}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>

            <div className="card report-card">
              <h3>Top items</h3>
              {report.topItems.length === 0 ? <p className="muted">No sales in range.</p> : (
                <table className="od-items">
                  <tbody>
                    {report.topItems.map((t, i) => (
                      <tr key={i}>
                        <td>{t.itemName}</td>
                        <td className="muted">×{t.quantitySold}</td>
                        <td className="od-price">Rs {t.revenue.toFixed(2)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </div>
        </>
      )}
    </section>
  );
}
