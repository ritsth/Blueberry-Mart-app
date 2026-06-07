import AsyncStorage from '@react-native-async-storage/async-storage';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

export type UserRole = 'Customer' | 'Shareholder';

export interface AuthResult {
  token: string;
  role: UserRole;
}

function parseRole(token: string): UserRole {
  const payload = JSON.parse(atob(token.split('.')[1]));
  const roleClaim =
    payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] ??
    payload['role'] ??
    '';
  const normalized = String(roleClaim).toLowerCase();
  return normalized === 'shareholder' ? 'Shareholder' : 'Customer';
}

export async function login(email: string, password: string): Promise<AuthResult> {
  const response = await fetch(`${API_BASE}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });

  if (!response.ok) {
    throw new Error('Invalid email or password.');
  }

  const { token } = await response.json();
  const role = parseRole(token);

  await AsyncStorage.setItem('jwt_token', token);
  await AsyncStorage.setItem('user_role', role);

  return { token, role };
}

export async function register(email: string, password: string): Promise<AuthResult> {
  const response = await fetch(`${API_BASE}/api/auth/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.message ?? 'Could not create account.');
  }

  const { token } = await response.json();
  const role = parseRole(token);

  await AsyncStorage.setItem('jwt_token', token);
  await AsyncStorage.setItem('user_role', role);

  return { token, role };
}

export async function logout(): Promise<void> {
  await AsyncStorage.removeItem('jwt_token');
  await AsyncStorage.removeItem('user_role');
}

export async function getStoredToken(): Promise<string | null> {
  return AsyncStorage.getItem('jwt_token');
}

export async function getStoredRole(): Promise<UserRole | null> {
  const role = await AsyncStorage.getItem('user_role');
  return role as UserRole | null;
}

/** Stable identifier for the signed-in user (from the JWT), used e.g. to key the first-login tour per customer. */
export async function getStoredUserId(): Promise<string | null> {
  const token = await AsyncStorage.getItem('jwt_token');
  if (!token) return null;
  try {
    const payload = JSON.parse(atob(token.split('.')[1]));
    return (
      payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/nameidentifier'] ??
      payload['nameid'] ??
      payload['sub'] ??
      payload['email'] ??
      null
    );
  } catch {
    return null;
  }
}
