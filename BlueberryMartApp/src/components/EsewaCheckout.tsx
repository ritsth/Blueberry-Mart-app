import React, { useEffect, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Modal,
  SafeAreaView,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { WebView, WebViewNavigation } from 'react-native-webview';
import { getStoredToken } from '../services/authService';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

export type PaymentOutcome = 'success' | 'failure' | 'cancelled';

interface Props {
  /** When set, the eSewa checkout opens for this order. */
  orderId: string | null;
  onClose: (outcome: PaymentOutcome) => void;
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

// Builds a self-submitting HTML form that POSTs the signed fields to eSewa.
function buildAutoSubmitForm(formUrl: string, fields: Record<string, string>): string {
  const inputs = Object.entries(fields)
    .map(([k, v]) => `<input type="hidden" name="${escapeHtml(k)}" value="${escapeHtml(String(v))}">`)
    .join('');
  return `<!doctype html><html><head><meta name="viewport" content="width=device-width, initial-scale=1">
    <style>body{font-family:-apple-system,system-ui,sans-serif;text-align:center;padding:48px;color:#334155}</style></head>
    <body><p>Connecting to eSewa…</p>
    <form id="f" action="${escapeHtml(formUrl)}" method="POST">${inputs}</form>
    <script>document.getElementById('f').submit();</script></body></html>`;
}

export default function EsewaCheckout({ orderId, onClose }: Props) {
  const [html, setHtml] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const handledRef = useRef(false);

  useEffect(() => {
    if (!orderId) {
      setHtml(null);
      setError(null);
      handledRef.current = false;
      return;
    }

    let cancelled = false;
    (async () => {
      try {
        const token = await getStoredToken();
        const res = await fetch(`${API_BASE}/api/payments/esewa/initiate`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
          body: JSON.stringify({ orderId }),
        });
        if (!res.ok) {
          const body = await res.json().catch(() => ({}));
          throw new Error(body.message ?? 'Could not start payment.');
        }
        const { formUrl, fields } = await res.json();
        if (!cancelled) setHtml(buildAutoSubmitForm(formUrl, fields));
      } catch (e: any) {
        if (!cancelled) setError(e.message ?? 'Could not start payment.');
      }
    })();

    return () => { cancelled = true; };
  }, [orderId]);

  // Watch the in-page navigation and only act once the WebView lands on our own
  // result page. We match on the URL *path* (ignoring the query string) so that
  // eSewa's intermediate pages — which carry our success_url/failure_url as query
  // params — don't trip the match and close the flow before the user finishes.
  // The backend redirects to these static pages only after it has committed, so by
  // the time we see one the order state is already settled.
  function onNavChange(nav: WebViewNavigation) {
    if (handledRef.current) return;
    const path = (nav.url ?? '').split('#')[0].split('?')[0];
    const isSuccess = path.endsWith('/payment-success.html');
    const isFailure = path.endsWith('/payment-failure.html');
    if (isSuccess || isFailure) {
      handledRef.current = true;
      onClose(isSuccess ? 'success' : 'failure');
    }
  }

  return (
    <Modal visible={orderId !== null} animationType="slide" onRequestClose={() => onClose('cancelled')}>
      <SafeAreaView style={styles.container}>
        <View style={styles.header}>
          <Text style={styles.title}>Pay with eSewa</Text>
          <TouchableOpacity onPress={() => onClose('cancelled')} hitSlop={12}>
            <Text style={styles.cancel}>Cancel</Text>
          </TouchableOpacity>
        </View>

        {error ? (
          <View style={styles.centered}>
            <Text style={styles.errorText}>{error}</Text>
            <TouchableOpacity style={styles.button} onPress={() => onClose('cancelled')}>
              <Text style={styles.buttonText}>Close</Text>
            </TouchableOpacity>
          </View>
        ) : html ? (
          <WebView
            source={{ html }}
            originWhitelist={['*']}
            onNavigationStateChange={onNavChange}
            startInLoadingState
            renderLoading={() => (
              <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>
            )}
          />
        ) : (
          <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>
        )}
      </SafeAreaView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#fff' },
  header: {
    flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between',
    paddingHorizontal: 16, paddingVertical: 12, borderBottomWidth: 1, borderBottomColor: '#e2e8f0',
  },
  title: { fontSize: 17, fontWeight: '600', color: '#0f172a' },
  cancel: { fontSize: 15, color: '#dc2626', fontWeight: '500' },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 24 },
  errorText: { color: '#b91c1c', fontSize: 15, textAlign: 'center', marginBottom: 16 },
  button: { backgroundColor: '#16a34a', paddingHorizontal: 24, paddingVertical: 12, borderRadius: 10 },
  buttonText: { color: '#fff', fontWeight: '600' },
});
