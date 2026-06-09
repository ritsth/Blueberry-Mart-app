import { useCallback, useEffect, useState } from 'react';
import {
  AdminUser, ASSIGNABLE_ROLES, assignRole, banUser, Branch, getBranches,
  listUsers, Page, Role, unbanUser,
} from '../api';

const PAGE_SIZE = 25;
const FIELD_ROLES = ['staff', 'manager'];

export default function UsersPage() {
  const [search, setSearch] = useState('');
  const [role, setRole] = useState('');
  const [banned, setBanned] = useState<'' | 'true' | 'false'>('');
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Page<AdminUser> | null>(null);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const res = await listUsers({
        search: search || undefined,
        role: role || undefined,
        banned: banned === '' ? undefined : banned === 'true',
        page,
        pageSize: PAGE_SIZE,
      });
      setData(res);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load users.');
    } finally {
      setLoading(false);
    }
  }, [search, role, banned, page]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { getBranches().then(setBranches).catch(() => setBranches([])); }, []);

  async function onBan(u: AdminUser) {
    const reason = window.prompt(`Ban ${u.email}?\nOptional reason:`, '');
    if (reason === null) return; // cancelled
    try {
      await banUser(u.id, reason);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Ban failed.');
    }
  }

  async function onUnban(u: AdminUser) {
    if (!window.confirm(`Unban ${u.email}?`)) return;
    try {
      await unbanUser(u.id);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Unban failed.');
    }
  }

  async function onRoleChange(u: AdminUser, newRole: Role) {
    if (newRole === u.role) return;
    // Staff/manager need a branch — default to their current one or the first branch.
    let branchId: string | undefined;
    if (FIELD_ROLES.includes(newRole)) {
      if (branches.length === 0) {
        alert('No branches exist yet — create a branch before assigning staff or manager.');
        await load();
        return;
      }
      branchId = u.branchId ?? branches[0].id;
    }
    if (!window.confirm(`Change ${u.email} from "${u.role}" to "${newRole}"?`)) {
      await load(); // revert the select to the real value
      return;
    }
    try {
      await assignRole(u.id, newRole, branchId);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Role change failed.');
      await load();
    }
  }

  async function onBranchChange(u: AdminUser, branchId: string) {
    if (branchId === u.branchId) return;
    try {
      await assignRole(u.id, u.role as Role, branchId);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Branch change failed.');
      await load();
    }
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <section>
      <header className="page-head">
        <h1>Users</h1>
        {data && <span className="count">{data.total} total</span>}
      </header>

      <div className="filters">
        <input
          placeholder="Search email…"
          value={search}
          onChange={(e) => { setPage(1); setSearch(e.target.value); }}
        />
        <select value={role} onChange={(e) => { setPage(1); setRole(e.target.value); }}>
          <option value="">All roles</option>
          <option value="customer">Customer</option>
          <option value="shareholder">Shareholder</option>
          <option value="staff">Staff</option>
          <option value="manager">Manager</option>
          <option value="admin">Admin</option>
        </select>
        <select value={banned} onChange={(e) => { setPage(1); setBanned(e.target.value as '' | 'true' | 'false'); }}>
          <option value="">Any status</option>
          <option value="false">Active</option>
          <option value="true">Banned</option>
        </select>
      </div>

      {error && <p className="error">{error}</p>}

      <table className="grid">
        <thead>
          <tr>
            <th>Email</th><th>Role</th><th>Branch</th><th>Member</th><th>Points</th>
            <th>Status</th><th>Joined</th><th></th>
          </tr>
        </thead>
        <tbody>
          {data?.items.map((u) => (
            <tr key={u.id} className={u.isBanned ? 'banned-row' : ''}>
              <td>{u.email}</td>
              <td>
                <select
                  className={`role-select role-${u.role}`}
                  value={u.role}
                  onChange={(e) => onRoleChange(u, e.target.value as Role)}
                >
                  {ASSIGNABLE_ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                </select>
              </td>
              <td>
                {FIELD_ROLES.includes(u.role) ? (
                  <select
                    className="branch-select"
                    value={u.branchId ?? ''}
                    onChange={(e) => onBranchChange(u, e.target.value)}
                  >
                    {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
                  </select>
                ) : (
                  <span className="muted">—</span>
                )}
              </td>
              <td>{u.isMember ? '✓' : '—'}</td>
              <td>{u.loyaltyPoints}</td>
              <td>
                {u.isBanned
                  ? <span className="pill banned" title={u.banReason ?? ''}>Banned</span>
                  : <span className="pill active">Active</span>}
              </td>
              <td>{new Date(u.createdAt).toLocaleDateString()}</td>
              <td className="actions">
                {u.role === 'admin'
                  ? <span className="muted">—</span>
                  : u.isBanned
                    ? <button className="btn small" onClick={() => onUnban(u)}>Unban</button>
                    : <button className="btn small danger" onClick={() => onBan(u)}>Ban</button>}
              </td>
            </tr>
          ))}
          {!loading && data?.items.length === 0 && (
            <tr><td colSpan={8} className="empty">No users match.</td></tr>
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
