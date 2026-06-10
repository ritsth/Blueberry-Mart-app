import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getBranchId, getEmail, getRole } from '../auth';
import { Branch, DashboardSummary, getBranches, getDashboardSummary } from '../api';

export default function Dashboard() {
  const email = getEmail();
  const role = getRole();
  const branchId = getBranchId();
  const [branchName, setBranchName] = useState<string | null>(null);
  const [summary, setSummary] = useState<DashboardSummary | null>(null);

  useEffect(() => {
    if (!branchId) return;
    getBranches()
      .then((bs: Branch[]) => setBranchName(bs.find((b) => b.id === branchId)?.name ?? null))
      .catch(() => setBranchName(null));
  }, [branchId]);

  useEffect(() => {
    getDashboardSummary().then(setSummary).catch(() => setSummary(null));
  }, []);

  return (
    <section>
      <header className="page-head">
        <h1>Dashboard</h1>
      </header>

      <div className="card">
        <p>Signed in as <strong>{email}</strong></p>
        <p>Role <span className={`pill role-${role}`}>{role}</span></p>
        {branchId && (
          <p>Branch <strong>{branchName ?? '…'}</strong></p>
        )}
      </div>

      <div className="stats">
        <Link to="/items" className="stat">
          <span className={`stat-num ${summary && summary.lowStockItems > 0 ? 'warn' : ''}`}>
            {summary ? summary.lowStockItems : '–'}
          </span>
          <span className="stat-label">Low-stock items</span>
        </Link>
        <Link to="/orders" className="stat">
          <span className={`stat-num ${summary && summary.pendingOrders > 0 ? 'warn' : ''}`}>
            {summary ? summary.pendingOrders : '–'}
          </span>
          <span className="stat-label">Orders awaiting payment</span>
        </Link>
        <Link to="/orders" className="stat">
          <span className="stat-num">{summary ? summary.activeOrders : '–'}</span>
          <span className="stat-label">Orders in fulfillment</span>
        </Link>
      </div>

      {role === 'admin' && (
        <p className="muted">As an admin you can also manage users, reviews, and store settings. Counts above are across all branches.</p>
      )}
    </section>
  );
}
