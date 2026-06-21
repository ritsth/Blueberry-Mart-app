import AsyncStorage from '@react-native-async-storage/async-storage';
import { GoogleSignin, isSuccessResponse } from '@react-native-google-signin/google-signin';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

// Configure Google Sign-In once. webClientId is the Google OAuth *Web* client id — it's also the
// audience the backend validates the returned ID token against. Set per build in eas.json `env`.
GoogleSignin.configure({
  webClientId: process.env.EXPO_PUBLIC_GOOGLE_WEB_CLIENT_ID,
});

export type UserRole = 'Customer' | 'Shareholder';

export interface AuthResult {
  token: string;
  role: UserRole;
}

/**
 * Thrown when a back-office account (staff / manager / admin) tries to sign into the
 * customer app. The LoginScreen shows this message instead of the generic
 * "invalid credentials" so the user knows to use the portal, not that their password is wrong.
 */
export class WorkAccountError extends Error {
  constructor() {
    super('This is a staff account. Please sign in at the Blueberry Mart portal instead.');
    this.name = 'WorkAccountError';
  }
}

/** Thrown when the user dismisses the Google account picker — callers can ignore it silently. */
export class GoogleCancelledError extends Error {
  constructor() {
    super('Google sign-in was cancelled.');
    this.name = 'GoogleCancelledError';
  }
}

// Only these two roles belong to the customer mobile app; staff/manager/admin are portal-only.
const APP_ROLES = ['customer', 'shareholder'] as const;

/** The raw, lower-cased role claim straight from the JWT (e.g. 'customer', 'manager'). */
function rawRole(token: string): string {
  const payload = JSON.parse(atob(token.split('.')[1]));
  const roleClaim =
    payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] ??
    payload['role'] ??
    '';
  return String(roleClaim).toLowerCase();
}

function parseRole(token: string): UserRole {
  return rawRole(token) === 'shareholder' ? 'Shareholder' : 'Customer';
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

  // Block back-office (staff/manager/admin) accounts from the customer app *before* we
  // persist anything — so they can't get a half-signed-in session that 403s on every fetch.
  if (!APP_ROLES.includes(rawRole(token) as (typeof APP_ROLES)[number])) {
    throw new WorkAccountError();
  }

  const role = parseRole(token);

  await AsyncStorage.setItem('jwt_token', token);
  await AsyncStorage.setItem('user_role', role);

  return { token, role };
}

/**
 * "Continue with Google". Opens the native Google account picker, sends the resulting ID token to
 * the backend (which verifies it and creates/links the account), then stores our own JWT — same
 * result as password login. Throws GoogleCancelledError if the user dismisses the picker.
 */
export async function googleSignIn(): Promise<AuthResult> {
  await GoogleSignin.hasPlayServices({ showPlayServicesUpdateDialog: true });

  const googleResult = await GoogleSignin.signIn();
  if (!isSuccessResponse(googleResult)) {
    throw new GoogleCancelledError();
  }

  const idToken = googleResult.data.idToken;
  if (!idToken) {
    throw new Error('Google did not return a sign-in token.');
  }

  const response = await fetch(`${API_BASE}/api/auth/google`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ idToken }),
  });

  if (!response.ok) {
    throw new Error('Could not sign in with Google. Please try again.');
  }

  const { token } = await response.json();

  // Same back-office guard as password login: staff/manager/admin belong in the portal, not here.
  if (!APP_ROLES.includes(rawRole(token) as (typeof APP_ROLES)[number])) {
    throw new WorkAccountError();
  }

  const role = parseRole(token);
  await AsyncStorage.setItem('jwt_token', token);
  await AsyncStorage.setItem('user_role', role);
  return { token, role };
}

// `phone` is optional — when given, the backend links this sign-up to a "guest" account created at
// the till with the same phone, so in-store loyalty/orders carry over (account claim).
export async function register(email: string, password: string, phone?: string): Promise<AuthResult> {
  const response = await fetch(`${API_BASE}/api/auth/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password, phone: phone || undefined }),
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

/**
 * Permanently deletes the signed-in user's account. The backend anonymizes the account
 * (scrubs email/phone/password, removes addresses & notifications) and keeps order history
 * in anonymized form. On success the local session is cleared.
 */
export async function deleteAccount(): Promise<void> {
  const token = await getStoredToken();
  const response = await fetch(`${API_BASE}/api/profile`, {
    method: 'DELETE',
    headers: { Authorization: `Bearer ${token}` },
  });

  if (!response.ok) {
    throw new Error('Could not delete your account. Please try again.');
  }

  await logout();
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
