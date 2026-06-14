import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Branch, createInStoreSale, CustomerLite, getBranches, InventoryItem,
  listManagedItems, searchCustomers,
} from '../api';
import { getBranchId, isAdmin } from '../auth';

const MEMBER_RATE = 0.05;   // display-only; the backend applies the configured rate at checkout

/**
 * Point-of-sale screen for store staff: search the branch's catalogue, build a ticket with a
 * running total, optionally attach a customer (for loyalty), take payment, and complete. Each sale
 * posts to /api/orders/manage/in-store-sale, which creates a paid + completed `in_store` order and
 * deducts stock. With no customer attached the sale is an anonymous walk-in.
 *
 * `embedded` trims the page chrome so it can sit inside the staff Dashboard.
 */
export default function SellPage({ embedded = false }: { embedded?: boolean }) {
  const admin = isAdmin();
  const ownBranch = getBranchId();

  const [branches, setBranches] = useState<Branch[]>([]);
  const [branchId, setBranchId] = useState<string>(admin ? '' : (ownBranch ?? ''));
  const [items, setItems] = useState<InventoryItem[]>([]);
  const [search, setSearch] = useState('');
  const [cart, setCart] = useState<Record<string, number>>({});   // itemId → qty
  const [customer, setCustomer] = useState<CustomerLite | null>(null);
  const [method, setMethod] = useState('cash');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [receipt, setReceipt] = useState<{ orderNumber: number; total: number } | null>(null);

  // Admins choose a branch; everyone else sells at their assigned branch.
  useEffect(() => { if (admin) getBranches().then(setBranches).catch(() => setBranches([])); }, [admin]);

  // (Re)load the branch's active, in-stock catalogue.
  const reloadItems = () => {
    if (!branchId) { setItems([]); return; }
    listManagedItems({ branchId, pageSize: 200 })
      // Retail only — bulk is members-only wholesale, not sold at the walk-in till.
      .then((p) => setItems(p.items.filter((i) => i.isActive && i.stockQuantity > 0 && !i.isBulkOnly)))
      .catch(() => setError('Failed to load items.'));
  };
  useEffect(reloadItems, [branchId]);

  const byId = useMemo(() => Object.fromEntries(items.map((i) => [i.id, i])), [items]);
  const visible = items.filter((i) => i.itemName.toLowerCase().includes(search.trim().toLowerCase()));
  const lines = Object.entries(cart).filter(([, q]) => q > 0);
  const subtotal = lines.reduce((sum, [id, q]) => sum + (byId[id]?.price ?? 0) * q, 0);
  const count = lines.reduce((n, [, q]) => n + q, 0);
  const discount = customer?.isMember ? subtotal * MEMBER_RATE : 0;
  const total = subtotal - discount;

  function setQty(id: string, qty: number) {
    const max = byId[id]?.stockQuantity ?? 0;
    setCart((c) => ({ ...c, [id]: Math.max(0, Math.min(qty, max)) }));
  }

  async function completeSale() {
    setBusy(true);
    setError('');
    try {
      const res = await createInStoreSale({
        branchId: admin ? branchId : undefined,
        items: lines.map(([itemId, quantity]) => ({ itemId, quantity })),
        paymentMethod: method,
        customerId: customer?.id,
      });
      setReceipt({ orderNumber: res.orderNumber, total: res.totalAmount });
      setCart({});
      setSearch('');
      setCustomer(null);
      reloadItems();   // stock dropped
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not complete the sale.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section>
      {!embedded && (
        <header className="page-head">
          <h1>Sell</h1>
          <span className="count">in-store till</span>
          {admin && (
            <select
              className="pos-branch"
              style={{ marginLeft: 'auto' }}
              value={branchId}
              onChange={(e) => { setBranchId(e.target.value); setCart({}); setCustomer(null); setReceipt(null); }}
            >
              <option value="">Select a branch…</option>
              {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
            </select>
          )}
        </header>
      )}

      {embedded && admin && (
        <div className="pos-branch-row">
          <select className="pos-branch" value={branchId} onChange={(e) => { setBranchId(e.target.value); setCart({}); setCustomer(null); setReceipt(null); }}>
            <option value="">Select a branch…</option>
            {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </select>
        </div>
      )}

      {receipt && (
        <p className="success">
          ✓ Sale completed — order #{receipt.orderNumber}, Rs {receipt.total.toFixed(2)}. Start the next sale below.
        </p>
      )}

      {!branchId ? (
        <p className="muted">{admin ? 'Choose a branch to start selling.' : 'Your account is not assigned to a branch.'}</p>
      ) : (
        <div className="pos-grid">
          {/* Left: searchable catalogue */}
          <div className="pos-list">
            <input
              className="pos-search"
              placeholder="Search items…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              autoFocus={!embedded}
            />
            <p className="muted pos-hint">Retail items only — bulk/wholesale isn't sold at the till.</p>
            <div className="pos-items">
              {visible.map((it) => {
                const qty = cart[it.id] ?? 0;
                const low = it.stockQuantity <= 5;
                return (
                  <div key={it.id} className={`pos-item${qty > 0 ? ' in-cart' : ''}`}>
                    <div className="pos-item-info">
                      <strong>{it.itemName}</strong>
                      <span className="muted">
                        Rs {it.price.toFixed(2)} · <span className={low ? 'warn' : ''}>{it.stockQuantity} in stock</span>
                      </span>
                    </div>
                    {qty === 0 ? (
                      <button className="btn small primary" onClick={() => setQty(it.id, 1)}>Add</button>
                    ) : (
                      <div className="qty-steppers">
                        <button className="btn small" onClick={() => setQty(it.id, qty - 1)}>−</button>
                        <span className="qty-n">{qty}</span>
                        <button className="btn small" disabled={qty >= it.stockQuantity} onClick={() => setQty(it.id, qty + 1)}>+</button>
                      </div>
                    )}
                  </div>
                );
              })}
              {visible.length === 0 && <p className="empty">No items match.</p>}
            </div>
          </div>

          {/* Right: the running ticket */}
          <div className="pos-ticket">
            <CustomerPicker customer={customer} onChange={setCustomer} />

            {lines.length === 0 ? (
              <p className="muted pos-empty">No items yet — add from the left.</p>
            ) : (
              <div className="pos-ticket-lines">
                {lines.map(([id, q]) => {
                  const it = byId[id];
                  if (!it) return null;
                  return (
                    <div key={id} className="pos-ticket-line">
                      <span className="pos-ticket-name">{it.itemName}</span>
                      <div className="qty-steppers">
                        <button className="btn small" onClick={() => setQty(id, q - 1)}>−</button>
                        <span className="qty-n">{q}</span>
                        <button className="btn small" disabled={q >= it.stockQuantity} onClick={() => setQty(id, q + 1)}>+</button>
                      </div>
                      <span className="pos-ticket-amt">Rs {(it.price * q).toFixed(2)}</span>
                    </div>
                  );
                })}
              </div>
            )}

            {discount > 0 && (
              <div className="pos-subtotal">
                <span>Subtotal</span><span>Rs {subtotal.toFixed(2)}</span>
              </div>
            )}
            {discount > 0 && (
              <div className="pos-subtotal discount">
                <span>Member discount (5%)</span><span>− Rs {discount.toFixed(2)}</span>
              </div>
            )}

            <div className="pos-total">
              <span>Total{count > 0 ? ` · ${count} item${count === 1 ? '' : 's'}` : ''}</span>
              <strong>Rs {total.toFixed(2)}</strong>
            </div>

            {error && <p className="error">{error}</p>}

            <div className="pos-pay">
              <select value={method} onChange={(e) => setMethod(e.target.value)}>
                <option value="cash">Cash</option>
                <option value="card">Card</option>
                <option value="esewa">eSewa</option>
              </select>
              <button className="btn primary" disabled={busy || lines.length === 0} onClick={completeSale}>
                {busy ? 'Completing…' : `Complete · Rs ${total.toFixed(2)}`}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}

/** Optional "attach customer" control: search by email, pick a match, or stay anonymous. */
function CustomerPicker({ customer, onChange }: { customer: CustomerLite | null; onChange: (c: CustomerLite | null) => void }) {
  const [q, setQ] = useState('');
  const [results, setResults] = useState<CustomerLite[]>([]);
  const [open, setOpen] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (customer) return;                       // already attached — don't search
    if (timer.current) clearTimeout(timer.current);
    const term = q.trim();
    if (term.length < 2) { setResults([]); setOpen(false); return; }
    timer.current = setTimeout(() => {
      searchCustomers(term)
        .then((rows) => { setResults(rows); setOpen(true); })
        .catch(() => { setResults([]); setOpen(false); });
    }, 250);
    return () => { if (timer.current) clearTimeout(timer.current); };
  }, [q, customer]);

  if (customer) {
    return (
      <div className="pos-customer attached">
        <div>
          <strong>{customer.email}</strong>
          {customer.isMember && <span className="pill member">Member</span>}
          <div className="muted">{customer.loyaltyPoints} pts · earns loyalty</div>
        </div>
        <button className="btn small" onClick={() => { onChange(null); setQ(''); }}>Remove</button>
      </div>
    );
  }

  return (
    <div className="pos-customer">
      <input
        placeholder="Attach customer by email (optional)…"
        value={q}
        onChange={(e) => setQ(e.target.value)}
        onFocus={() => { if (results.length) setOpen(true); }}
      />
      {open && (
        <div className="pos-customer-results">
          {results.length === 0
            ? <div className="pos-customer-empty muted">No matches.</div>
            : results.map((c) => (
              <button key={c.id} className="pos-customer-row" onClick={() => { onChange(c); setOpen(false); setQ(''); }}>
                <span>{c.email}</span>
                {c.isMember && <span className="pill member">Member</span>}
              </button>
            ))}
        </div>
      )}
    </div>
  );
}
