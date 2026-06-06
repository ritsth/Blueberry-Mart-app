import { getStoredToken } from './authService';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

export interface ChatTurn { role: 'user' | 'assistant'; content: string; }

export async function sendChat(messages: ChatTurn[]): Promise<{ enabled: boolean; reply: string }> {
  const token = await getStoredToken();
  const res = await fetch(`${API_BASE}/api/chat`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ messages }),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error ?? 'The assistant is unavailable.');
  }
  return res.json();
}
