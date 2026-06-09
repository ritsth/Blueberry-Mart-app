import { useCallback, useEffect, useState } from 'react';
import {
  advanceOrderStatus, Branch, cancelOrder, getBranches, getOrder, listOrders,
  ManagedOrder, ManagedOrderDetail, NEXT_STATUS, Page, recordPayment,
} from '../api';
import { getRole, isAdmin } from '../auth';
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
      </header>

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
    </section>
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
