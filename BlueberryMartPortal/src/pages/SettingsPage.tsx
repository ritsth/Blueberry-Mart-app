import { FormEvent, useEffect, useState } from 'react';
import { getSettings, StoreSettings, updateSettings } from '../api';

export default function SettingsPage() {
  const [s, setS] = useState<StoreSettings | null>(null);
  const [error, setError] = useState('');
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    getSettings().then(setS).catch((e) => setError(e instanceof Error ? e.message : 'Load failed.'));
  }, []);

  if (error && !s) return <p className="error">{error}</p>;
  if (!s) return <p className="muted">Loading…</p>;

  function set<K extends keyof StoreSettings>(key: K, value: StoreSettings[K]) {
    setS((prev) => (prev ? { ...prev, [key]: value } : prev));
    setSaved(false);
  }

  async function onSave(e: FormEvent) {
    e.preventDefault();
    if (!s) return;
    setSaving(true);
    setError('');
    try {
      const updated = await updateSettings({
        deliveryFee: Number(s.deliveryFee),
        membershipMonthlyFee: Number(s.membershipMonthlyFee),
        memberDiscountRate: Number(s.memberDiscountRate),
        maintenanceMode: s.maintenanceMode,
        maintenanceMessage: s.maintenanceMessage ?? '',
      });
      setS(updated);
      setSaved(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <section>
      <header className="page-head">
        <h1>Settings</h1>
        <span className="count">updated {new Date(s.updatedAt).toLocaleString()}</span>
      </header>

      <form className="settings-form" onSubmit={onSave}>
        <fieldset>
          <legend>Pricing</legend>
          <label>
            Delivery fee (Rs)
            <input type="number" min={0} step="1" value={s.deliveryFee}
              onChange={(e) => set('deliveryFee', e.target.valueAsNumber || 0)} />
            <small>Flat fee charged on delivery orders. Waived for members.</small>
          </label>
          <label>
            Membership monthly fee (Rs)
            <input type="number" min={0} step="1" value={s.membershipMonthlyFee}
              onChange={(e) => set('membershipMonthlyFee', e.target.valueAsNumber || 0)} />
          </label>
          <label>
            Member discount (%)
            <input type="number" min={0} max={100} step="0.5"
              value={Math.round(s.memberDiscountRate * 1000) / 10}
              onChange={(e) => set('memberDiscountRate', (e.target.valueAsNumber || 0) / 100)} />
            <small>Stored as a fraction (e.g. 5% = 0.05).</small>
          </label>
        </fieldset>

        <fieldset>
          <legend>Store state</legend>
          <label className="checkbox">
            <input type="checkbox" checked={s.maintenanceMode}
              onChange={(e) => set('maintenanceMode', e.target.checked)} />
            Maintenance mode — pause new orders
          </label>
          {s.maintenanceMode && (
            <label>
              Message shown to customers
              <input type="text" placeholder="We'll be back shortly."
                value={s.maintenanceMessage ?? ''}
                onChange={(e) => set('maintenanceMessage', e.target.value)} />
            </label>
          )}
        </fieldset>

        {error && <p className="error">{error}</p>}
        <div className="settings-actions">
          <button className="btn primary" type="submit" disabled={saving}>
            {saving ? 'Saving…' : 'Save changes'}
          </button>
          {saved && <span className="saved-note">Saved ✓</span>}
        </div>
      </form>
    </section>
  );
}
