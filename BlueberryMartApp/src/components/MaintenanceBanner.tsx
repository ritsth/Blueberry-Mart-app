import React, { useEffect, useState } from 'react';
import { AppState, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';
const POLL_MS = 60_000;

/**
 * App-wide strip shown when an admin has enabled maintenance mode (ordering paused).
 * Reads the public, unauthenticated /api/system/status. Renders nothing normally,
 * so it has no layout impact when the store is open.
 */
export default function MaintenanceBanner() {
  const insets = useSafeAreaInsets();
  const [on, setOn] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;

    async function check() {
      try {
        const res = await fetch(`${API_BASE}/api/system/status`);
        if (!res.ok || !alive) return;
        const s = await res.json();
        setOn(!!s.maintenanceMode);
        setMessage(s.maintenanceMessage ?? null);
      } catch { /* offline / unreachable — leave banner hidden */ }
    }

    check();
    const timer = setInterval(check, POLL_MS);
    // Re-check when the app returns to the foreground.
    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'active') check();
    });

    return () => { alive = false; clearInterval(timer); sub.remove(); };
  }, []);

  if (!on) return null;

  return (
    <View style={[styles.banner, { paddingTop: insets.top + 8 }]}>
      <Ionicons name="construct-outline" size={16} color="#7c2d12" />
      <Text style={styles.text} numberOfLines={2}>
        {message?.trim() ? message : 'Ordering is temporarily paused for maintenance.'}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  banner: {
    flexDirection: 'row', alignItems: 'center', gap: 8,
    backgroundColor: '#fef3c7', borderBottomWidth: 1, borderBottomColor: '#fcd34d',
    paddingHorizontal: 16, paddingBottom: 10,
  },
  text: { flex: 1, color: '#7c2d12', fontSize: 13, fontWeight: '600' },
});
