import AsyncStorage from '@react-native-async-storage/async-storage';
import Constants, { ExecutionEnvironment } from 'expo-constants';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

// Google Sign-In is a *native* module that isn't bundled into Expo Go. Importing it at module load
// (or calling GoogleSignin.configure() up top) throws while the bundle is evaluating, which prevents
// the root component from registering — Expo Go then shows "App entry not found" and nothing loads.
// So we (a) detect Expo Go and refuse early, and (b) require the module lazily, only when the user
// actually taps "Continue with Google" in a real dev/standalone build.
const isExpoGo = Constants.executionEnvironment === ExecutionEnvironment.StoreClient;

let googleConfigured = false;
function loadGoogleSignin() {
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const mod = require('@react-native-google-signin/google-signin');
  if (!googleConfigured) {
    // webClientId is the Google OAuth *Web* client id — also the audience the backend validates the
    // returned ID token against. Set per build in eas.json `env`.
    mod.GoogleSignin.configure({
      webClientId: process.env.EXPO_PUBLIC_GOOGLE_WEB_CLIENT_ID,
    });
    googleConfigured = true;
  }
  return mod;
}

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

/**
 * Thrown when a password login is attempted on an account whose email isn't confirmed yet. The
 * backend has already re-sent a verification link; the screen routes the user to CheckEmailScreen.
 */
export class EmailNotVerifiedError extends Error {
  email: string;
  constructor(email: string) {
    super('Please verify your email before logging in.');
    this.name = 'EmailNotVerifiedError';
    this.email = email;
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

  // 403 = the password was right but the email isn't verified yet (the backend just re-sent a link).
  if (response.status === 403) {
    const body = await response.json().catch(() => ({}));
    if (body.requiresVerification) {
      throw new EmailNotVerifiedError(body.email ?? email);
    }
  }

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
  // Expo Go can't run the native Google module — surface a clear message instead of crashing.
  if (isExpoGo) {
    throw new Error('Google sign-in needs a dev build — it doesn’t work in Expo Go. Use email & password here.');
  }

  const { GoogleSignin, isSuccessResponse } = loadGoogleSignin();
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
// Registration no longer logs the user straight in: the account starts unverified, the backend emails
// a confirmation link, and login is blocked until it's used. We return the email so the caller can
// route to CheckEmailScreen.
export async function register(
  email: string,
  password: string,
  phone?: string,
): Promise<{ email: string }> {
  const response = await fetch(`${API_BASE}/api/auth/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password, phone: phone || undefined }),
  });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.message ?? 'Could not create account.');
  }

  const body = await response.json().catch(() => ({}));
  return { email: body.email ?? email };
}

/** Polls whether the given email has been verified yet (used to auto-advance after the user taps
 * the link in their browser). Returns false on any error so callers can simply keep polling. */
export async function isEmailVerified(email: string): Promise<boolean> {
  try {
    const res = await fetch(
      `${API_BASE}/api/auth/verification-status?email=${encodeURIComponent(email)}`,
    );
    if (!res.ok) return false;
    const body = await res.json();
    return body.verified === true;
  } catch {
    return false;
  }
}

/** Ask the backend to re-send the verification link. Always resolves (no account enumeration). */
export async function resendVerification(email: string): Promise<void> {
  await fetch(`${API_BASE}/api/auth/resend-verification`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email }),
  }).catch(() => undefined);
}

/** Ask the backend to email a password-reset link. Always resolves (no account enumeration). */
export async function forgotPassword(email: string): Promise<void> {
  await fetch(`${API_BASE}/api/auth/forgot-password`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email }),
  }).catch(() => undefined);
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
