const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

/**
 * Store-wide values an admin can edit in the portal (delivery fee, membership fee,
 * member discount, maintenance mode). Served by the public /api/system/status so any
 * screen can read the live numbers instead of hardcoding them — change it in the admin
 * portal and the app reflects it on the next fetch / pull-to-refresh.
 */
export interface StoreSettings {
  deliveryFee: number;
  membershipMonthlyFee: number;
  memberDiscountRate: number;
  maintenanceMode: boolean;
  maintenanceMessage: string | null;
}

export async function fetchStoreSettings(): Promise<StoreSettings | null> {
  try {
    const res = await fetch(`${API_BASE}/api/system/status`);
    if (!res.ok) return null;
    return (await res.json()) as StoreSettings;
  } catch {
    return null; // offline / unreachable — caller keeps its current values
  }
}
