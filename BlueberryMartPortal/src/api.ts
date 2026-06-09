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
  branchId: string | null;
  branchName: string | null;
  isMember: boolean;
  loyaltyPoints: number;
  isBanned: boolean;
  bannedAt: string | null;
  banReason: string | null;
  createdAt: string;
}
export interface Page<T> { items: T[]; total: number; page: number; pageSize: number; }

export interface Branch { id: string; name: string; city: string; }

export function getBranches(): Promise<Branch[]> {
  return request<Branch[]>('/api/branches');
}

// ---- Inventory management (staff/manager/admin) ----
export interface InventoryItem {
  id: string;
  branchId: string;
  branchName: string;
  itemName: string;
  price: number;
  stockQuantity: number;
  isBulkOnly: boolean;
  isActive: boolean;
  updatedAt: string;
}

export function listManagedItems(params: {
  branchId?: string; search?: string; lowStock?: boolean; includeInactive?: boolean;
  page?: number; pageSize?: number;
}): Promise<Page<InventoryItem>> {
  const q = new URLSearchParams();
  if (params.branchId) q.set('branchId', params.branchId);
  if (params.search) q.set('search', params.search);
  if (params.lowStock) q.set('lowStock', 'true');
  if (params.includeInactive) q.set('includeInactive', 'true');
  if (params.page) q.set('page', String(params.page));
  if (params.pageSize) q.set('pageSize', String(params.pageSize));
  return request<Page<InventoryItem>>(`/api/inventory/manage?${q.toString()}`);
}

export function createItem(body: {
  branchId: string; itemName: string; price: number; stockQuantity: number; isBulkOnly: boolean;
}): Promise<InventoryItem> {
  return request<InventoryItem>('/api/inventory/manage', { method: 'POST', body: JSON.stringify(body) });
}

export function updateItem(id: string, body: {
  itemName: string; price: number; isBulkOnly: boolean;
}): Promise<InventoryItem> {
  return request<InventoryItem>(`/api/inventory/manage/${id}`, { method: 'PUT', body: JSON.stringify(body) });
}

export function adjustStock(id: string, delta: number, reason: string): Promise<InventoryItem> {
  return request<InventoryItem>(`/api/inventory/manage/${id}/adjust`, {
    method: 'POST', body: JSON.stringify({ delta, reason }),
  });
}

export function setItemActive(id: string, active: boolean): Promise<InventoryItem> {
  return request<InventoryItem>(`/api/inventory/manage/${id}/${active ? 'activate' : 'deactivate'}`, {
    method: 'POST',
  });
}

// ---- Order fulfillment (staff/manager/admin) ----
export interface ManagedOrder {
  id: string;
  orderNumber: number;
  customerEmail: string;
  branchId: string;
  branchName: string;
  orderType: string;
  status: string;
  totalAmount: number;
  paymentStatus: string;
  createdAt: string;
}

export interface ManagedOrderLine { itemName: string; quantity: number; unitPrice: number; }

export interface ManagedOrderDetail extends ManagedOrder {
  discountAmount: number;
  deliveryFee: number;
  deliveryAddress: string | null;
  paymentRef: string | null;
  items: ManagedOrderLine[];
}

// confirmed → processing → ready → completed
export const NEXT_STATUS: Record<string, string> = {
  confirmed: 'processing',
  processing: 'ready',
  ready: 'completed',
};

export function listOrders(params: {
  branchId?: string; status?: string; search?: string; page?: number; pageSize?: number;
}): Promise<Page<ManagedOrder>> {
  const q = new URLSearchParams();
  if (params.branchId) q.set('branchId', params.branchId);
  if (params.status) q.set('status', params.status);
  if (params.search) q.set('search', params.search);
  if (params.page) q.set('page', String(params.page));
  if (params.pageSize) q.set('pageSize', String(params.pageSize));
  return request<Page<ManagedOrder>>(`/api/orders/manage?${q.toString()}`);
}

export function getOrder(id: string): Promise<ManagedOrderDetail> {
  return request<ManagedOrderDetail>(`/api/orders/manage/${id}`);
}

export function advanceOrderStatus(id: string, status: string): Promise<unknown> {
  return request(`/api/orders/manage/${id}/status`, { method: 'POST', body: JSON.stringify({ status }) });
}

export function recordPayment(id: string, method: string): Promise<unknown> {
  return request(`/api/orders/manage/${id}/record-payment`, { method: 'POST', body: JSON.stringify({ method }) });
}

export function cancelOrder(id: string): Promise<unknown> {
  return request(`/api/orders/manage/${id}/cancel`, { method: 'POST' });
}

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

export function assignRole(id: string, role: Role, branchId?: string): Promise<unknown> {
  return request(`/api/admin/users/${id}/role`, {
    method: 'POST',
    body: JSON.stringify({ role, branchId: branchId ?? null }),
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
