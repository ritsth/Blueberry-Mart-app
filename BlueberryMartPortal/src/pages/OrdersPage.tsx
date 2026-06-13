import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  advanceOrderStatus, Branch, cancelOrder, createInStoreSale, getBranches, getOrder,
  InventoryItem, listManagedItems, listOrders, ManagedOrder, ManagedOrderDetail,
  NEXT_STATUS, Page, recordPayment,
} from '../api';
import { getBranchId, getRole, isAdmin } from '../auth';
import Modal from '../components/Modal';

const PAGE_SIZE = 25;
const STATUSES = ['pending', 'confirmed', 'processing', 'ready', 'completed', 'cancelled'];

export default function OrdersPage() {
  const admin = isAdmin();
  const role = getRole();
  const canCancel = role === 'manager' || role === 'admin';

  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [branchFilter, setBranchFilter] = useState('');
  const [page, setPage] = useState(1);

  const [data, setData] = useState<Page<ManagedOrder> | null>(null);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);
  const [newSale, setNewSale] = useState(false);
  const [flash, setFlash] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const res = await listOrders({
        branchId: admin ? (branchFilter || undefined) : undefined,
        status: status || undefined,
        search: search || undefined,
        page,
        pageSize: PAGE_SIZE,
      });
      setData(res);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load orders.');
    } finally {
      setLoading(false);
    }
  }, [admin, branchFilter, status, search, page]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { if (admin) getBranches().then(setBranches).catch(() => setBranches([])); }, [admin]);

  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <section>
      <header className="page-head">
        <h1>Orders</h1>
        {data && <span className="count">{data.total} total</span>}
        <button className="btn primary" style={{ marginLeft: 'auto' }} onClick={() => setNewSale(true)}>
          + New in-store sale
        </button>
      </header>

      {flash && <p className="success">{flash}</p>}

      <div className="filters">
        <input
          placeholder="Order # …"
          value={search}
          onChange={(e) => { setPage(1); setSearch(e.target.value); }}
        />
        <select value={status} onChange={(e) => { setPage(1); setStatus(e.target.value); }}>
          <option value="">All statuses</option>
          {STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
        {admin && (
          <select value={branchFilter} onChange={(e) => { setPage(1); setBranchFilter(e.target.value); }}>
            <option value="">All branches</option>
            {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </select>
        )}
      </div>

      {error && <p className="error">{error}</p>}

      <table className="grid">
        <thead>
          <tr>
            <th>Order</th><th>Customer</th>{admin && <th>Branch</th>}<th>Type</th>
            <th>Total</th><th>Status</th><th>Payment</th><th>Placed</th><th></th>
          </tr>
        </thead>
        <tbody>
          {data?.items.map((o) => (
            <tr key={o.id}>
              <td>#{o.orderNumber}</td>
              <td>{o.customerEmail}</td>
              {admin && <td>{o.branchName}</td>}
              <td>{o.orderType}</td>
              <td>Rs {o.totalAmount.toFixed(2)}</td>
              <td><span className={`pill status-${o.status}`}>{o.status}</span></td>
              <td><span className={`pill pay-${o.paymentStatus}`}>{o.paymentStatus}</span></td>
              <td>{new Date(o.createdAt).toLocaleDateString()}</td>
              <td className="actions">
                <button className="btn small" onClick={() => setOpenId(o.id)}>View</button>
              </td>
            </tr>
          ))}
          {!loading && data?.items.length === 0 && (
            <tr><td colSpan={admin ? 9 : 8} className="empty">No orders match.</td></tr>
          )}
        </tbody>
      </table>

      <div className="pager">
        <button className="btn small" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>Prev</button>
        <span>Page {page} / {totalPages}</span>
        <button className="btn small" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>Next</button>
      </div>

      {openId && (
        <OrderDetail
          id={openId}
          canCancel={canCancel}
          onClose={() => setOpenId(null)}
          onChanged={() => { setOpenId(null); load(); }}
        />
      )}

      {newSale && (
        <NewSaleModal
          admin={admin}
          branches={branches}
          onClose={() => setNewSale(false)}
          onDone={(orderNumber) => {
            setNewSale(false);
            setFlash(`In-store sale recorded — order #${orderNumber}.`);
            setPage(1);
            load();
          }}
        />
      )}
    </section>
  );
}

// Ring up a walk-in sale: pick the branch's in-stock items, choose a payment method, and the
// backend creates a paid + completed in-store order. Attaching a specific customer (for loyalty)
// is API-supported but not yet surfaced here — these are anonymous walk-in sales.
function NewSaleModal({
  admin, branches, onClose, onDone,
}: { admin: boolean; branches: Branch[]; onClose: () => void; onDone: (orderNumber: number) => void }) {
  // Staff/managers sell at their own branch (from the token); admins must choose one.
  const ownBranch = getBranchId();
  const [branchId, setBranchId] = useState<string>(admin ? '' : (ownBranch ?? ''));
  const [items, setItems] = useState<InventoryItem[]>([]);
  const [search, setSearch] = useState('');
  const [cart, setCart] = useState<Record<string, number>>({});   // itemId → qty
  const [method, setMethod] = useState('cash');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  // Load the branch's active in-stock catalogue once a branch is known.
  useEffect(() => {
    if (!branchId) { setItems([]); return; }
    listManagedItems({ branchId, pageSize: 200 })
      .then((p) => setItems(p.items.filter((i) => i.isActive && i.stockQuantity > 0)))
      .catch(() => setError('Failed to load items.'));
  }, [branchId]);

  const byId = useMemo(() => Object.fromEntries(items.map((i) => [i.id, i])), [items]);
  const visible = items.filter((i) => i.itemName.toLowerCase().includes(search.trim().toLowerCase()));
  const lines = Object.entries(cart).filter(([, q]) => q > 0);
  const total = lines.reduce((sum, [id, q]) => sum + (byId[id]?.price ?? 0) * q, 0);

  function setQty(id: string, qty: number) {
    const max = byId[id]?.stockQuantity ?? 0;
    const clamped = Math.max(0, Math.min(qty, max));
    setCart((c) => ({ ...c, [id]: clamped }));
  }

  async function submit() {
    setBusy(true);
    setError('');
    try {
      const res = await createInStoreSale({
        branchId: admin ? branchId : undefined,
        items: lines.map(([itemId, quantity]) => ({ itemId, quantity })),
        paymentMethod: method,
      });
      onDone(res.orderNumber);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not record the sale.');
      setBusy(false);
    }
  }

  return (
    <Modal title="New in-store sale" onClose={onClose}>
      <div className="order-detail">
        {admin && (
          <div className="od-row">
            <span>Branch</span>
            <select value={branchId} onChange={(e) => { setBranchId(e.target.value); setCart({}); }}>
              <option value="">Select a branch…</option>
              {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
            </select>
          </div>
        )}

        {branchId && (
          <>
            <input
              placeholder="Search items…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              style={{ width: '100%', marginBottom: 8 }}
            />
            <table className="od-items">
              <tbody>
                {visible.map((it) => (
                  <tr key={it.id}>
                    <td>{it.itemName}<div className="muted">Rs {it.price.toFixed(2)} · {it.stockQuantity} in stock</div></td>
                    <td className="od-price">
                      <div className="qty-steppers">
                        <button className="btn small" disabled={!cart[it.id]} onClick={() => setQty(it.id, (cart[it.id] ?? 0) - 1)}>−</button>
                        <span style={{ minWidth: 24, textAlign: 'center', display: 'inline-block' }}>{cart[it.id] ?? 0}</span>
                        <button className="btn small" disabled={(cart[it.id] ?? 0) >= it.stockQuantity} onClick={() => setQty(it.id, (cart[it.id] ?? 0) + 1)}>+</button>
                      </div>
                    </td>
                  </tr>
                ))}
                {visible.length === 0 && <tr><td colSpan={2} className="empty">No items.</td></tr>}
              </tbody>
            </table>

            <div className="od-row total"><span>Total ({lines.length} item{lines.length === 1 ? '' : 's'})</span><strong>Rs {total.toFixed(2)}</strong></div>

            {error && <p className="error">{error}</p>}

            <div className="pay-row">
              <select value={method} onChange={(e) => setMethod(e.target.value)}>
                <option value="cash">Cash</option>
                <option value="card">Card</option>
                <option value="esewa">eSewa</option>
              </select>
              <button className="btn primary" disabled={busy || lines.length === 0} onClick={submit}>
                Complete sale
              </button>
            </div>
          </>
        )}
      </div>
    </Modal>
  );
}

function OrderDetail({
  id, canCancel, onClose, onChanged,
}: { id: string; canCancel: boolean; onClose: () => void; onChanged: () => void }) {
  const [order, setOrder] = useState<ManagedOrderDetail | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [method, setMethod] = useState('cash');

  useEffect(() => {
    getOrder(id).then(setOrder).catch((err) => setError(err instanceof Error ? err.message : 'Failed to load order.'));
  }, [id]);

  async function act(fn: () => Promise<unknown>) {
    setBusy(true);
    setError('');
    try {
      await fn();
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Action failed.');
      setBusy(false);
    }
  }

  const next = order ? NEXT_STATUS[order.status] : undefined;
  const isUnpaid = order?.paymentStatus !== 'completed';
  const isTerminal = order?.status === 'completed' || order?.status === 'cancelled';

  return (
    <Modal title={order ? `Order #${order.orderNumber}` : 'Order'} onClose={onClose}>
      {!order ? (
        <p className="muted">{error || 'Loading…'}</p>
      ) : (
        <div className="order-detail">
          <div className="od-row"><span>Customer</span><strong>{order.customerEmail}</strong></div>
          <div className="od-row"><span>Type</span><strong>{order.orderType}</strong></div>
          <div className="od-row"><span>Status</span><span className={`pill status-${order.status}`}>{order.status}</span></div>
          <div className="od-row"><span>Payment</span><span className={`pill pay-${order.paymentStatus}`}>{order.paymentStatus}</span></div>
          {order.deliveryAddress && <div className="od-row"><span>Deliver to</span><strong>{order.deliveryAddress}</strong></div>}

          <table className="od-items">
            <tbody>
              {order.items.map((it, i) => (
                <tr key={i}>
                  <td>{it.itemName}</td>
                  <td className="muted">×{it.quantity}</td>
                  <td className="od-price">Rs {(it.unitPrice * it.quantity).toFixed(2)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="od-row"><span>Discount</span><span>Rs {order.discountAmount.toFixed(2)}</span></div>
          <div className="od-row"><span>Delivery</span><span>Rs {order.deliveryFee.toFixed(2)}</span></div>
          <div className="od-row total"><span>Total</span><strong>Rs {order.totalAmount.toFixed(2)}</strong></div>

          {error && <p className="error">{error}</p>}

          <div className="od-actions">
            {order.status === 'pending' && isUnpaid && (
              <div className="pay-row">
                <select value={method} onChange={(e) => setMethod(e.target.value)}>
                  <option value="cash">Cash</option>
                  <option value="card">Card</option>
                </select>
                <button className="btn primary" disabled={busy} onClick={() => act(() => recordPayment(order.id, method))}>
                  Record payment
                </button>
              </div>
            )}
            {next && (
              <button className="btn primary" disabled={busy} onClick={() => act(() => advanceOrderStatus(order.id, next))}>
                Advance to {next}
              </button>
            )}
            {canCancel && !isTerminal && (
              <button className="btn danger" disabled={busy} onClick={() => {
                if (window.confirm('Cancel this order and restock its items?')) act(() => cancelOrder(order.id));
              }}>
                Cancel order
              </button>
            )}
          </div>
        </div>
      )}
    </Modal>
  );
}
