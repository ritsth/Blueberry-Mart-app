import { getStoredToken } from './authService';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

// --- types (mirror the backend catalog/query/report contracts) ---------------
export interface CatalogDimension { id: string; label: string; dataType: string; }
export interface CatalogMeasure { id: string; label: string; aggs: string[]; }
export interface Catalog {
  enabled: boolean;
  dimensions: CatalogDimension[];
  measures: CatalogMeasure[];
}

export interface MeasureSpec { field: string; agg: string; }
export interface FilterSpec { field: string; op: string; values: string[]; }
export interface OrderSpec { field: string; dir: string; }
export interface QuerySpec {
  measures: MeasureSpec[];
  dimensions: string[];
  filters: FilterSpec[];
  orderBy?: OrderSpec[];
  limit?: number;
  chartType?: string;
}

export interface ResultColumn { key: string; label: string; role: 'dimension' | 'measure'; }
export interface QueryResult {
  enabled?: boolean;
  columns: ResultColumn[];
  rows: Record<string, any>[];
}

export interface SavedReport {
  id: string;
  name: string;
  config: QuerySpec;
  createdAt: string;
  updatedAt: string;
}

async function authHeaders(): Promise<Record<string, string>> {
  const token = await getStoredToken();
  return { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
}

// --- catalog + query ----------------------------------------------------------
export async function getCatalog(): Promise<Catalog> {
  const res = await fetch(`${API_BASE}/api/analytics/catalog`, { headers: await authHeaders() });
  if (!res.ok) throw new Error('Failed to load the field catalog.');
  return res.json();
}

export async function runQuery(spec: QuerySpec): Promise<QueryResult> {
  const res = await fetch(`${API_BASE}/api/analytics/query`, {
    method: 'POST',
    headers: await authHeaders(),
    body: JSON.stringify(spec),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error ?? 'Query failed.');
  }
  return res.json();
}

// --- saved reports ------------------------------------------------------------
export async function listReports(): Promise<SavedReport[]> {
  const res = await fetch(`${API_BASE}/api/analytics/reports`, { headers: await authHeaders() });
  if (!res.ok) throw new Error('Failed to load saved reports.');
  return res.json();
}

export async function createReport(name: string, config: QuerySpec): Promise<SavedReport> {
  const res = await fetch(`${API_BASE}/api/analytics/reports`, {
    method: 'POST',
    headers: await authHeaders(),
    body: JSON.stringify({ name, config }),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error ?? 'Could not save the report.');
  }
  return res.json();
}

export async function updateReport(id: string, name: string, config: QuerySpec): Promise<SavedReport> {
  const res = await fetch(`${API_BASE}/api/analytics/reports/${id}`, {
    method: 'PUT',
    headers: await authHeaders(),
    body: JSON.stringify({ name, config }),
  });
  if (!res.ok) throw new Error('Could not update the report.');
  return res.json();
}

export async function deleteReport(id: string): Promise<void> {
  const res = await fetch(`${API_BASE}/api/analytics/reports/${id}`, {
    method: 'DELETE',
    headers: await authHeaders(),
  });
  if (!res.ok) throw new Error('Could not delete the report.');
}
