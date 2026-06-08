const TOKEN_KEY = 'bbm_admin_token';
const API = import.meta.env.VITE_API_URL ?? 'http://localhost:5027';

const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

function decodeJwt(token: string): Record<string, unknown> {
  const payload = token.split('.')[1];
  return JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')));
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

export function isAuthed(): boolean {
  const t = getToken();
  return !!t && roleOf(t) === 'admin';
}

export function logout(): void {
  localStorage.removeItem(TOKEN_KEY);
}

/** Logs in against the shared API and stores the token only if it is an admin account. */
export async function login(email: string, password: string): Promise<void> {
  const res = await fetch(`${API}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
  if (!res.ok) throw new Error('Invalid email or password.');

  const { token } = await res.json();
  if (roleOf(token) !== 'admin') {
    throw new Error('This account is not an administrator.');
  }
  localStorage.setItem(TOKEN_KEY, token);
}
