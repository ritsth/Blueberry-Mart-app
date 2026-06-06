import React, { useEffect, useState } from 'react';
import { ActivityIndicator, ScrollView, StyleSheet, Text, TouchableOpacity, View } from 'react-native';
import { useNavigation } from '@react-navigation/native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { getStoredToken } from '../services/authService';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

interface AppNotification { id: string; message: string; isRead: boolean; createdAt: string; }

export default function AlertsScreen() {
  const insets = useSafeAreaInsets();
  const navigation = useNavigation<any>();
  const [notifications, setNotifications] = useState<AppNotification[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    (async () => {
      try {
        const token = await getStoredToken();
        const auth = { headers: { Authorization: `Bearer ${token}` } };
        const res = await fetch(`${API_BASE}/api/notifications`, auth);
        if (res.ok) {
          const n = await res.json();
          setNotifications(n.notifications ?? []);
          if ((n.unread ?? 0) > 0) {
            await fetch(`${API_BASE}/api/notifications/read`, { method: 'POST', ...auth });
          }
        }
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  return (
    <View style={[styles.container, { paddingTop: insets.top + 8 }]}>
      <View style={styles.header}>
        <TouchableOpacity onPress={() => navigation.goBack()} hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}>
          <Ionicons name="chevron-back" size={26} color="#111827" />
        </TouchableOpacity>
        <Text style={styles.title}>Alerts</Text>
      </View>

      {loading ? (
        <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>
      ) : (
        <ScrollView contentContainerStyle={styles.list} showsVerticalScrollIndicator={false}>
          {notifications.length === 0 ? (
            <View style={styles.empty}>
              <Ionicons name="notifications-off-outline" size={42} color="#d1d5db" />
              <Text style={styles.emptyText}>No alerts yet.</Text>
              <Text style={styles.emptySub}>Back-in-stock and order updates will show here.</Text>
            </View>
          ) : (
            notifications.map(n => (
              <View key={n.id} style={[styles.card, !n.isRead && styles.cardUnread]}>
                <Ionicons name="notifications" size={18} color="#16a34a" style={{ marginRight: 10, marginTop: 1 }} />
                <View style={{ flex: 1 }}>
                  <Text style={styles.message}>{n.message}</Text>
                  <Text style={styles.date}>
                    {new Date(n.createdAt).toLocaleString('en-NP', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })}
                  </Text>
                </View>
              </View>
            ))
          )}
        </ScrollView>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f9fafb' },
  header: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingHorizontal: 20, paddingBottom: 12 },
  title: { fontSize: 22, fontWeight: '700', color: '#111827' },
  centered: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  list: { paddingHorizontal: 24, paddingBottom: 32 },
  empty: { alignItems: 'center', marginTop: 80, gap: 8 },
  emptyText: { fontSize: 15, fontWeight: '600', color: '#6b7280', marginTop: 8 },
  emptySub: { fontSize: 13, color: '#9ca3af', textAlign: 'center' },
  card: {
    flexDirection: 'row', backgroundColor: '#ffffff', borderRadius: 12, padding: 14, marginBottom: 10,
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
  },
  cardUnread: { borderLeftWidth: 3, borderLeftColor: '#16a34a' },
  message: { fontSize: 14, fontWeight: '600', color: '#111827', marginBottom: 4 },
  date: { fontSize: 12, color: '#9ca3af' },
});
