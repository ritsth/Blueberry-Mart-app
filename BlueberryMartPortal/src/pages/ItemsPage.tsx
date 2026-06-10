import { FormEvent, useCallback, useEffect, useState } from 'react';
import {
  adjustStock, Branch, createItem, getBranches, getItemHistory, InventoryItem,
  listManagedItems, Page, setItemActive, StockAdjustment, updateItem,
} from '../api';
import { getBranchId, getRole, isAdmin } from '../auth';
import Modal from '../components/Modal';

const PAGE_SIZE = 25;
const LOW_STOCK = 5;

type ModalState =
  | { kind: 'create' }
  | { kind: 'edit'; item: InventoryItem }
  | { kind: 'adjust'; item: InventoryItem }
  | { kind: 'history'; item: InventoryItem }
  | null;

export default function ItemsPage() {
  const admin = isAdmin();
  const role = getRole();
  const canDeactivate = role === 'manager' || role === 'admin';
  const myBranchId = getBranchId();

  const [search, setSearch] = useState('');
  const [lowStock, setLowStock] = useState(false);
  const [includeInactive, setIncludeInactive] = useState(false);
  const [branchFilter, setBranchFilter] = useState(''); // admin only
  const [page, setPage] = useState(1);

  const [data, setData] = useState<Page<InventoryItem> | null>(null);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [modal, setModal] = useState<ModalState>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const res = await listManagedItems({
        branchId: admin ? (branchFilter || undefined) : undefined,
        search: search || undefined,
        lowStock,
        includeInactive,
        page,
        pageSize: PAGE_SIZE,
      });
      setData(res);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load items.');
    } finally {
      setLoading(false);
    }
  }, [admin, branchFilter, search, lowStock, includeInactive, page]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { getBranches().then(setBranches).catch(() => setBranches([])); }, []);

  const myBranchName = branches.find((b) => b.id === myBranchId)?.name ?? null;

  async function onToggleActive(item: InventoryItem) {
    const verb = item.isActive ? 'Deactivate' : 'Restore';
    if (!window.confirm(`${verb} "${item.itemName}"?`)) return;
    try {
      await setItemActive(item.id, !item.isActive);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : `${verb} failed.`);
    }
  }

  function closeAndReload() {
    setModal(null);
    load();
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <section>
      <header className="page-head">
        <h1>Items</h1>
        {data && <span className="count">{data.total} total</span>}
        <button className="btn primary push-right" onClick={() => setModal({ kind: 'create' })}>+ New item</button>
      </header>

      {!admin && myBranchName && <p className="muted">Branch: <strong>{myBranchName}</strong></p>}

      <div className="filters">
        <input
          placeholder="Search items…"
          value={search}
          onChange={(e) => { setPage(1); setSearch(e.target.value); }}
        />
        {admin && (
          <select value={branchFilter} onChange={(e) => { setPage(1); setBranchFilter(e.target.value); }}>
            <option value="">All branches</option>
            {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </select>
        )}
        <label className="check"><input type="checkbox" checked={lowStock} onChange={(e) => { setPage(1); setLowStock(e.target.checked); }} /> Low stock</label>
        <label className="check"><input type="checkbox" checked={includeInactive} onChange={(e) => { setPage(1); setIncludeInactive(e.target.checked); }} /> Show inactive</label>
      </div>

      {error && <p className="error">{error}</p>}

      <table className="grid">
        <thead>
          <tr>
            <th>Item</th>{admin && <th>Branch</th>}<th>Price</th><th>Stock</th>
            <th>Type</th><th>Status</th><th></th>
          </tr>
        </thead>
        <tbody>
          {data?.items.map((it) => (
            <tr key={it.id} className={it.isActive ? '' : 'banned-row'}>
              <td>{it.itemName}</td>
              {admin && <td>{it.branchName}</td>}
              <td>Rs {it.price.toFixed(2)}</td>
              <td className={it.stockQuantity <= LOW_STOCK ? 'stock-low' : ''}>{it.stockQuantity}</td>
              <td>{it.isBulkOnly ? 'Bulk' : 'Retail'}</td>
              <td>
                {it.isActive
                  ? <span className="pill active">Active</span>
                  : <span className="pill banned">Inactive</span>}
              </td>
              <td className="actions">
                <button className="btn small" onClick={() => setModal({ kind: 'edit', item: it })}>Edit</button>
                <button className="btn small" onClick={() => setModal({ kind: 'adjust', item: it })}>Stock</button>
                <button className="btn small" onClick={() => setModal({ kind: 'history', item: it })}>History</button>
                {canDeactivate && (
                  <button className={`btn small ${it.isActive ? 'danger' : ''}`} onClick={() => onToggleActive(it)}>
                    {it.isActive ? 'Deactivate' : 'Restore'}
                  </button>
                )}
              </td>
            </tr>
          ))}
          {!loading && data?.items.length === 0 && (
            <tr><td colSpan={admin ? 7 : 6} className="empty">No items match.</td></tr>
          )}
        </tbody>
      </table>

      <div className="pager">
        <button className="btn small" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>Prev</button>
        <span>Page {page} / {totalPages}</span>
        <button className="btn small" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>Next</button>
      </div>

      {modal?.kind === 'create' && (
        <Modal title="New item" onClose={() => setModal(null)}>
          <CreateForm
            admin={admin}
            branches={branches}
            myBranchId={myBranchId}
            myBranchName={myBranchName}
            onDone={closeAndReload}
          />
        </Modal>
      )}
      {modal?.kind === 'edit' && (
        <Modal title={`Edit · ${modal.item.itemName}`} onClose={() => setModal(null)}>
          <EditForm item={modal.item} onDone={closeAndReload} />
        </Modal>
      )}
      {modal?.kind === 'adjust' && (
        <Modal title={`Adjust stock · ${modal.item.itemName}`} onClose={() => setModal(null)}>
          <AdjustForm item={modal.item} onDone={closeAndReload} />
        </Modal>
      )}
      {modal?.kind === 'history' && (
        <Modal title={`History · ${modal.item.itemName}`} onClose={() => setModal(null)}>
          <HistoryView itemId={modal.item.id} />
        </Modal>
      )}
    </section>
  );
}

function HistoryView({ itemId }: { itemId: string }) {
  const [rows, setRows] = useState<StockAdjustment[] | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    getItemHistory(itemId)
      .then(setRows)
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load history.'));
  }, [itemId]);

  if (error) return <p className="error">{error}</p>;
  if (!rows) return <p className="muted">Loading…</p>;
  if (rows.length === 0) return <p className="muted">No adjustments recorded yet.</p>;

  return (
    <table className="od-items">
      <tbody>
        {rows.map((r, i) => (
          <tr key={i}>
            <td>
              <div>{new Date(r.createdAt).toLocaleString()}</div>
              <div className="muted small">{r.userEmail}</div>
            </td>
            <td className={r.delta >= 0 ? 'delta-pos' : 'delta-neg'}>{r.delta >= 0 ? `+${r.delta}` : r.delta}</td>
            <td className="muted">→ {r.newQuantity}</td>
            <td className="muted">{r.reason}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function CreateForm({
  admin, branches, myBranchId, myBranchName, onDone,
}: {
  admin: boolean; branches: Branch[]; myBranchId: string | null; myBranchName: string | null;
  onDone: () => void;
}) {
  const [branchId, setBranchId] = useState(admin ? '' : (myBranchId ?? ''));
  const [itemName, setItemName] = useState('');
  const [price, setPrice] = useState('');
  const [stock, setStock] = useState('0');
  const [bulk, setBulk] = useState(false);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError('');
    if (!branchId) { setError('Choose a branch.'); return; }
    setBusy(true);
    try {
      await createItem({
        branchId,
        itemName: itemName.trim(),
        price: Number(price),
        stockQuantity: Number(stock),
        isBulkOnly: bulk,
      });
      onDone();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Create failed.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="modal-form" onSubmit={submit}>
      {admin ? (
        <label>Branch
          <select value={branchId} onChange={(e) => setBranchId(e.target.value)} required>
            <option value="">Select…</option>
            {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </select>
        </label>
      ) : (
        <label>Branch<input type="text" value={myBranchName ?? '—'} disabled /></label>
      )}
      <label>Name<input value={itemName} onChange={(e) => setItemName(e.target.value)} autoFocus required /></label>
      <label>Price (Rs)<input type="number" min="0" step="0.01" value={price} onChange={(e) => setPrice(e.target.value)} required /></label>
      <label>Initial stock<input type="number" min="0" value={stock} onChange={(e) => setStock(e.target.value)} required /></label>
      <label className="check"><input type="checkbox" checked={bulk} onChange={(e) => setBulk(e.target.checked)} /> Bulk-only (members)</label>
      {error && <p className="error">{error}</p>}
      <div className="modal-actions">
        <button type="submit" className="btn primary" disabled={busy}>{busy ? 'Saving…' : 'Create item'}</button>
      </div>
    </form>
  );
}

function EditForm({ item, onDone }: { item: InventoryItem; onDone: () => void }) {
  const [itemName, setItemName] = useState(item.itemName);
  const [price, setPrice] = useState(String(item.price));
  const [bulk, setBulk] = useState(item.isBulkOnly);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError('');
    setBusy(true);
    try {
      await updateItem(item.id, { itemName: itemName.trim(), price: Number(price), isBulkOnly: bulk });
      onDone();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Update failed.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="modal-form" onSubmit={submit}>
      <label>Name<input value={itemName} onChange={(e) => setItemName(e.target.value)} autoFocus required /></label>
      <label>Price (Rs)<input type="number" min="0" step="0.01" value={price} onChange={(e) => setPrice(e.target.value)} required /></label>
      <label className="check"><input type="checkbox" checked={bulk} onChange={(e) => setBulk(e.target.checked)} /> Bulk-only (members)</label>
      {error && <p className="error">{error}</p>}
      <div className="modal-actions">
        <button type="submit" className="btn primary" disabled={busy}>{busy ? 'Saving…' : 'Save changes'}</button>
      </div>
    </form>
  );
}

function AdjustForm({ item, onDone }: { item: InventoryItem; onDone: () => void }) {
  const [mode, setMode] = useState<'add' | 'remove'>('add');
  const [qty, setQty] = useState('');
  const [reason, setReason] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const n = Number(qty);
  const delta = mode === 'add' ? n : -n;
  const resulting = item.stockQuantity + delta;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError('');
    if (!n || n <= 0) { setError('Enter a quantity greater than zero.'); return; }
    if (resulting < 0) { setError(`Can't remove more than the current stock (${item.stockQuantity}).`); return; }
    setBusy(true);
    try {
      await adjustStock(item.id, delta, reason.trim());
      onDone();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Adjustment failed.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="modal-form" onSubmit={submit}>
      <p className="muted">Current stock: <strong>{item.stockQuantity}</strong></p>
      <div className="seg">
        <button type="button" className={mode === 'add' ? 'on' : ''} onClick={() => setMode('add')}>Add</button>
        <button type="button" className={mode === 'remove' ? 'on' : ''} onClick={() => setMode('remove')}>Remove</button>
      </div>
      <label>Quantity<input type="number" min="1" value={qty} onChange={(e) => setQty(e.target.value)} autoFocus required /></label>
      <label>Reason (optional)<input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="e.g. delivery, damage, correction" /></label>
      {n > 0 && resulting >= 0 && (
        <p className="muted">New stock will be <strong>{resulting}</strong>.</p>
      )}
      {error && <p className="error">{error}</p>}
      <div className="modal-actions">
        <button type="submit" className="btn primary" disabled={busy}>{busy ? 'Saving…' : 'Apply'}</button>
      </div>
    </form>
  );
}
