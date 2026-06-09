import { getToken, logout } from './auth';

const API = import.meta.env.VITE_API_URL ?? 'http://localhost:5027';

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = getToken();
  const res = await fetch(`${API}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(init.headers ?? {}),
    },
  });

  // 401 means the token is invalid/expired or the account was banned — bounce to login.
  if (res.status === 401) {
    logout();
    window.location.assign('/login');
    throw new Error('Session expired.');
  }
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error((body as { message?: string }).message ?? `Request failed (${res.status}).`);
  }
  // 204 No Content
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

// ---- Types mirroring the API's admin responses ----
export interface AdminUser {
  id: string;
  email: string;
  role: string;
  isMember: boolean;
  loyaltyPoints: number;
  isBanned: boolean;
  bannedAt: string | null;
  banReason: string | null;
  createdAt: string;
}
export interface Page<T> { items: T[]; total: number; page: number; pageSize: number; }

export interface AdminReview {
  id: string;
  userEmail: string;
  itemName: string;
  rating: number;
  comment: string;
  imagePath: string | null;
  createdAt: string;
}

// ---- Users ----
export function listUsers(params: {
  search?: string; role?: string; banned?: boolean; page?: number; pageSize?: number;
}): Promise<Page<AdminUser>> {
  const q = new URLSearchParams();
  if (params.search) q.set('search', params.search);
  if (params.role) q.set('role', params.role);
  if (params.banned != null) q.set('banned', String(params.banned));
  if (params.page) q.set('page', String(params.page));
  if (params.pageSize) q.set('pageSize', String(params.pageSize));
  return request<Page<AdminUser>>(`/api/admin/users?${q.toString()}`);
}

export function banUser(id: string, reason: string): Promise<unknown> {
  return request(`/api/admin/users/${id}/ban`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

export function unbanUser(id: string): Promise<unknown> {
  return request(`/api/admin/users/${id}/unban`, { method: 'POST' });
}

// ---- Reviews ----
export function listReviews(page = 1, pageSize = 25): Promise<Page<AdminReview>> {
  return request<Page<AdminReview>>(`/api/admin/reviews?page=${page}&pageSize=${pageSize}`);
}

export function deleteReview(id: string): Promise<unknown> {
  return request(`/api/admin/reviews/${id}`, { method: 'DELETE' });
}

// ---- Role assignment ----
export const ASSIGNABLE_ROLES = ['customer', 'shareholder', 'staff', 'manager', 'admin'] as const;
export type Role = (typeof ASSIGNABLE_ROLES)[number];

export function assignRole(id: string, role: Role): Promise<unknown> {
  return request(`/api/admin/users/${id}/role`, {
    method: 'POST',
    body: JSON.stringify({ role }),
  });
}

// ---- Settings ----
export interface StoreSettings {
  deliveryFee: number;
  membershipMonthlyFee: number;
  memberDiscountRate: number;
  maintenanceMode: boolean;
  maintenanceMessage: string | null;
  updatedAt: string;
}

export function getSettings(): Promise<StoreSettings> {
  return request<StoreSettings>('/api/admin/settings');
}

export function updateSettings(patch: Partial<Omit<StoreSettings, 'updatedAt'>>): Promise<StoreSettings> {
  return request<StoreSettings>('/api/admin/settings', {
    method: 'PUT',
    body: JSON.stringify(patch),
  });
}

export const apiBase = API;
