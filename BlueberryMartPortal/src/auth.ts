const TOKEN_KEY = 'bbm_admin_token';
const API = import.meta.env.VITE_API_URL ?? 'http://localhost:5027';

const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

// Roles allowed into the back-office portal. Customers/shareholders are rejected.
const BACKOFFICE_ROLES = ['admin', 'manager', 'staff'];

function decodeJwt(token: string): Record<string, unknown> {
  const payload = token.split('.')[1];
  return JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')));
}

function claim(key: string): string {
  const t = getToken();
  if (!t) return '';
  try {
    return String(decodeJwt(t)[key] ?? '');
  } catch {
    return '';
  }
}

function roleOf(token: string): string {
  try {
    const p = decodeJwt(token);
    return String(p[ROLE_CLAIM] ?? p['role'] ?? '').toLowerCase();
  } catch {
    return '';
  }
}

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function getRole(): string {
  const t = getToken();
  return t ? roleOf(t) : '';
}

export function getEmail(): string {
  return claim('email');
}

/** Branch the signed-in staff/manager operates, or null for admin and others. */
export function getBranchId(): string | null {
  return claim('branch') || null;
}

export function isAuthed(): boolean {
  const t = getToken();
  return !!t && BACKOFFICE_ROLES.includes(roleOf(t));
}

export function isAdmin(): boolean {
  return getRole() === 'admin';
}

export function logout(): void {
  localStorage.removeItem(TOKEN_KEY);
}

/** Logs in against the shared API and stores the token only for back-office roles. */
export async function login(email: string, password: string): Promise<void> {
  const res = await fetch(`${API}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
  if (!res.ok) throw new Error('Invalid email or password.');

  const { token } = await res.json();
  if (!BACKOFFICE_ROLES.includes(roleOf(token))) {
    throw new Error('This account does not have back-office access.');
  }
  localStorage.setItem(TOKEN_KEY, token);
}
