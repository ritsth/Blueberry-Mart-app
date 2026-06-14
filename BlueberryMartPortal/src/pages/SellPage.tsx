import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Branch, createGuestCustomer, createInStoreSale, CustomerLite, customerLabel, getBranches,
  getSystemStatus, InventoryItem, listManagedItems, searchCustomers,
} from '../api';
import { getBranchId, getEmail, isAdmin } from '../auth';

interface Receipt {
  orderNumber: number;
  at: Date;
  branchName: string;
  cashier: string;
  customer: string | null;
  lines: { name: string; qty: number; unitPrice: number }[];
  subtotal: number;
  discount: number;
  total: number;
  method: string;
}

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
  const [receipt, setReceipt] = useState<Receipt | null>(null);
  const [memberRate, setMemberRate] = useState(0.05);   // live rate from settings; 5% fallback

  // Branch list (admins pick from it; everyone uses it to resolve the branch name for the receipt).
  useEffect(() => { getBranches().then(setBranches).catch(() => setBranches([])); }, []);
  const branchName = branches.find((b) => b.id === branchId)?.name ?? 'Blueberry Mart';

  // The actual member-discount rate the backend will apply (so the preview matches the charge).
  useEffect(() => { getSystemStatus().then((s) => setMemberRate(s.memberDiscountRate)).catch(() => {}); }, []);

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
  const discount = customer?.isMember ? subtotal * memberRate : 0;
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
      // Snapshot the ticket into a printable receipt before clearing it. The grand total comes
      // from the server response (authoritative); subtotal/discount are the matching breakdown.
      setReceipt({
        orderNumber: res.orderNumber,
        at: new Date(),
        branchName,
        cashier: getEmail(),
        customer: customer ? customerLabel(customer) : null,
        lines: lines.map(([id, q]) => ({ name: byId[id]?.itemName ?? '', qty: q, unitPrice: byId[id]?.price ?? 0 })),
        subtotal,
        discount,
        total: res.totalAmount,
        method,
      });
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

      {receipt && <ReceiptOverlay receipt={receipt} onClose={() => setReceipt(null)} />}

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
                      <Stepper value={qty} max={it.stockQuantity} onChange={(n) => setQty(it.id, n)} />
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
                      <Stepper value={q} max={it.stockQuantity} onChange={(n) => setQty(id, n)} />
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
                <span>Member discount ({(memberRate * 100).toFixed(0)}%)</span><span>− Rs {discount.toFixed(2)}</span>
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

/** Printable in-store receipt shown after a completed sale. `@media print` (styles.css) hides
 *  everything but the `.receipt` block, so "Print" produces a clean slip. */
function ReceiptOverlay({ receipt, onClose }: { receipt: Receipt; onClose: () => void }) {
  const r = receipt;
  return (
    <div className="receipt-overlay" onClick={onClose}>
      <div className="receipt-card" onClick={(e) => e.stopPropagation()}>
        <div className="receipt">
          <div className="receipt-head">
            <div className="receipt-logo">🫐 Blueberry Mart</div>
            <div className="receipt-sub">{r.branchName}</div>
          </div>
          <div className="receipt-meta">
            <div><span>Order</span><strong>#{r.orderNumber}</strong></div>
            <div><span>Date</span><span>{r.at.toLocaleString()}</span></div>
            <div><span>Cashier</span><span>{r.cashier}</span></div>
            <div><span>Customer</span><span>{r.customer ?? 'Walk-in'}</span></div>
          </div>
          <div className="receipt-rule" />
          <table className="receipt-lines">
            <tbody>
              {r.lines.map((l, i) => (
                <tr key={i}>
                  <td>{l.name}</td>
                  <td className="receipt-qty">{l.qty} × {l.unitPrice.toFixed(2)}</td>
                  <td className="receipt-amt">Rs {(l.qty * l.unitPrice).toFixed(2)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="receipt-rule" />
          <div className="receipt-totals">
            {r.discount > 0 && (
              <>
                <div><span>Subtotal</span><span>Rs {r.subtotal.toFixed(2)}</span></div>
                <div className="discount"><span>Member discount</span><span>− Rs {r.discount.toFixed(2)}</span></div>
              </>
            )}
            <div className="grand"><span>Total</span><strong>Rs {r.total.toFixed(2)}</strong></div>
            <div><span>Paid ({r.method})</span><span>Rs {r.total.toFixed(2)}</span></div>
          </div>
          <div className="receipt-rule" />
          <div className="receipt-foot">Thank you for shopping at Blueberry Mart!</div>
        </div>
        <div className="receipt-actions">
          <button className="btn" onClick={() => window.print()}>Print</button>
          <button className="btn primary" onClick={onClose}>New sale</button>
        </div>
      </div>
    </div>
  );
}

/** −/＋ buttons around a typable quantity field. The parent's onChange clamps to [0, max]. */
function Stepper({ value, max, onChange }: { value: number; max: number; onChange: (n: number) => void }) {
  return (
    <div className="qty-steppers">
      <button className="btn small" disabled={value <= 0} onClick={() => onChange(value - 1)}>−</button>
      <input
        className="qty-input"
        type="number"
        min={0}
        max={max}
        value={value}
        onChange={(e) => { const n = parseInt(e.target.value, 10); onChange(Number.isNaN(n) ? 0 : n); }}
        onFocus={(e) => e.target.select()}
      />
      <button className="btn small" disabled={value >= max} onClick={() => onChange(value + 1)}>＋</button>
    </div>
  );
}

/** Optional "attach customer" control: search by email/phone, pick a match, quick-create a guest
 *  by phone, or stay anonymous (walk-in). */
function CustomerPicker({ customer, onChange }: { customer: CustomerLite | null; onChange: (c: CustomerLite | null) => void }) {
  const [q, setQ] = useState('');
  const [results, setResults] = useState<CustomerLite[]>([]);
  const [open, setOpen] = useState(false);
  const [newPhone, setNewPhone] = useState<string | null>(null);   // non-null = the "new customer" form is open
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
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

  async function createGuest() {
    const phone = (newPhone ?? '').trim();
    if (!phone) { setError('Enter a phone number.'); return; }
    setBusy(true);
    setError('');
    try {
      const c = await createGuestCustomer(phone);
      onChange(c);
      setNewPhone(null); setQ('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not create the customer.');
    } finally {
      setBusy(false);
    }
  }

  if (customer) {
    const label = customerLabel(customer);
    return (
      <div className="pos-customer attached">
        <div className="pos-customer-info">
          <div className="pos-customer-email">
            <strong title={label}>{label}</strong>
            {customer.isMember && <span className="pill member">Member</span>}
          </div>
          <div className="muted">
            {customer.email && customer.phone ? `${customer.phone} · ` : ''}{customer.loyaltyPoints} pts · earns loyalty
          </div>
        </div>
        <button className="btn small" onClick={() => { onChange(null); setQ(''); }}>Remove</button>
      </div>
    );
  }

  // "New customer" (guest by phone) form.
  if (newPhone !== null) {
    return (
      <div className="pos-customer">
        <div className="pos-newcust">
          <input
            placeholder="Customer phone…"
            value={newPhone}
            inputMode="numeric"
            maxLength={10}
            autoFocus
            onChange={(e) => setNewPhone(e.target.value.replace(/\D/g, '').slice(0, 10))}
            onKeyDown={(e) => { if (e.key === 'Enter') createGuest(); }}
          />
          <button className="btn primary" disabled={busy} onClick={createGuest}>Create &amp; attach</button>
          <button className="btn" disabled={busy} onClick={() => { setNewPhone(null); setError(''); }}>Cancel</button>
        </div>
        {error && <p className="error">{error}</p>}
      </div>
    );
  }

  return (
    <div className="pos-customer">
      <input
        placeholder="Attach customer by email or phone (optional)…"
        value={q}
        onChange={(e) => setQ(e.target.value)}
        onFocus={() => { if (results.length) setOpen(true); }}
      />
      <button className="pos-newcust-link" onClick={() => { setNewPhone(q.trim()); setOpen(false); }}>+ New customer</button>
      {open && (
        <div className="pos-customer-results">
          {results.map((c) => {
            const label = customerLabel(c);
            return (
              <button key={c.id} className="pos-customer-row" onClick={() => { onChange(c); setOpen(false); setQ(''); }} title={label}>
                <span className="pos-customer-row-email">{label}</span>
                {c.isMember && <span className="pill member">Member</span>}
              </button>
            );
          })}
          <button className="pos-customer-row pos-customer-new" onClick={() => { setNewPhone(q.trim()); setOpen(false); }}>
            + New customer{results.length === 0 ? ' (no matches)' : ''}
          </button>
        </div>
      )}
    </div>
  );
}
