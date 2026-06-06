import React, { useCallback, useState } from 'react';
import { StyleSheet, Text, TouchableOpacity, View } from 'react-native';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { getStoredToken } from '../../services/authService';
import ShoppingView from '../../components/ShoppingView';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

interface Address { id: string; label: string; city: string; isDefault: boolean; }

export default function CustomerShopTab() {
  const insets = useSafeAreaInsets();
  const navigation = useNavigation<any>();
  const [address, setAddress] = useState<Address | null>(null);
  const [unread, setUnread] = useState(0);

  useFocusEffect(useCallback(() => {
    let alive = true;
    (async () => {
      try {
        const token = await getStoredToken();
        const auth = { headers: { Authorization: `Bearer ${token}` } };
        const [aRes, nRes] = await Promise.all([
          fetch(`${API_BASE}/api/addresses`, auth),
          fetch(`${API_BASE}/api/notifications`, auth),
        ]);
        if (aRes.ok && alive) {
          const list: Address[] = await aRes.json();
          setAddress(list.find(a => a.isDefault) ?? list[0] ?? null);
        }
        if (nRes.ok && alive) {
          const n = await nRes.json();
          setUnread(n.unread ?? 0);
        }
      } catch { /* non-blocking */ }
    })();
    return () => { alive = false; };
  }, []));

  return (
    <View style={styles.container}>
      {/* Top bar */}
      <View style={[styles.header, { paddingTop: insets.top + 10 }]}>
        <TouchableOpacity style={styles.location} onPress={() => navigation.navigate('AddressesScreen')} activeOpacity={0.7}>
          <Ionicons name="location-outline" size={18} color="#14532d" />
          <View style={{ maxWidth: 180 }}>
            <Text style={styles.locLabel}>Deliver to</Text>
            <View style={styles.locRow}>
              <Text style={styles.locValue} numberOfLines={1}>
                {address ? `${address.label} · ${address.city}` : 'Set delivery address'}
              </Text>
              <Ionicons name="chevron-down" size={14} color="#6b7280" />
            </View>
          </View>
        </TouchableOpacity>

        <View style={styles.actions}>
          <TouchableOpacity style={styles.iconBtn} onPress={() => navigation.navigate('Activity')} activeOpacity={0.7}>
            <Ionicons name="notifications-outline" size={22} color="#111827" />
            {unread > 0 && (
              <View style={styles.badge}><Text style={styles.badgeText}>{unread > 9 ? '9+' : unread}</Text></View>
            )}
          </TouchableOpacity>
          <TouchableOpacity style={styles.iconBtn} onPress={() => navigation.navigate('Account')} activeOpacity={0.7}>
            <Ionicons name="person-circle-outline" size={26} color="#111827" />
          </TouchableOpacity>
        </View>
      </View>

      <View style={styles.body}>
        <ShoppingView />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f9fafb' },
  header: {
    flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between',
    paddingHorizontal: 20, paddingBottom: 12,
    backgroundColor: '#ffffff', borderBottomWidth: 1, borderBottomColor: '#f3f4f6',
  },
  location: { flexDirection: 'row', alignItems: 'center', gap: 8, flex: 1 },
  locLabel: { fontSize: 11, color: '#9ca3af', fontWeight: '600' },
  locRow: { flexDirection: 'row', alignItems: 'center', gap: 4 },
  locValue: { fontSize: 14, fontWeight: '700', color: '#111827' },
  actions: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  iconBtn: { padding: 6 },
  badge: {
    position: 'absolute', top: 2, right: 0, minWidth: 16, height: 16, borderRadius: 8,
    backgroundColor: '#dc2626', justifyContent: 'center', alignItems: 'center', paddingHorizontal: 4,
  },
  badgeText: { color: '#fff', fontSize: 10, fontWeight: '700' },
  body: { flex: 1, paddingHorizontal: 24, paddingTop: 16 },
});
