import { useEffect, useState } from 'react';
import { getBranchId, getEmail, getRole } from '../auth';
import { Branch, getBranches } from '../api';

export default function Dashboard() {
  const email = getEmail();
  const role = getRole();
  const branchId = getBranchId();
  const [branchName, setBranchName] = useState<string | null>(null);

  useEffect(() => {
    if (!branchId) return;
    getBranches()
      .then((bs: Branch[]) => setBranchName(bs.find((b) => b.id === branchId)?.name ?? null))
      .catch(() => setBranchName(null));
  }, [branchId]);

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

      <p className="muted">Use <strong>Items</strong> in the sidebar to manage your catalogue and stock.</p>
      {role === 'admin' && (
        <p className="muted">As an admin you can also manage users, reviews, and store settings.</p>
      )}
    </section>
  );
}
