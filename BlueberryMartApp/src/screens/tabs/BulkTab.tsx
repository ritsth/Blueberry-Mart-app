import React, { useCallback, useState } from 'react';
import {
  ActivityIndicator,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { getStoredToken } from '../../services/authService';
import ShoppingView from '../../components/ShoppingView';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

export default function BulkTab() {
  const insets = useSafeAreaInsets();
  const navigation = useNavigation<any>();
  const [isMember, setIsMember] = useState(false);
  const [loading, setLoading]   = useState(true);

  useFocusEffect(useCallback(() => { fetchMembership(); }, []));

  async function fetchMembership() {
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/membership/status`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.ok) {
        const data = await res.json();
        setIsMember(data.isMember);
      }
    } finally {
      setLoading(false);
    }
  }

  if (loading) {
    return <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>;
  }

  // Members get the full bulk shopping flow (cart, delivery, member pricing)
  if (isMember) {
    return (
      <View style={[styles.shopWrap, { paddingTop: insets.top + 12 }]}>
        <ShoppingView mode="bulk" />
      </View>
    );
  }

  // Non-members see an upsell that routes to the Account tab to join
  return (
    <View style={[styles.lockWrap, { paddingTop: insets.top + 40 }]}>
      <Text style={styles.lockIcon}>📦</Text>
      <Text style={styles.lockTitle}>Bulk Orders</Text>
      <View style={styles.plusBadge}><Text style={styles.plusBadgeText}>🫐 Plus members only</Text></View>
      <Text style={styles.lockBody}>
        Order business quantities — 25kg rice, 20L oil, 50kg flour and more —
        at member pricing with free delivery.
      </Text>
      <Text style={styles.lockSub}>Join Blueberry Plus to unlock bulk ordering.</Text>
      <TouchableOpacity
        style={styles.ctaButton}
        onPress={() => navigation.navigate('Account')}
        activeOpacity={0.85}
      >
        <Text style={styles.ctaText}>Go to Account → Join Plus</Text>
      </TouchableOpacity>
    </View>
  );
}

const styles = StyleSheet.create({
  centered: { flex: 1, justifyContent: 'center', alignItems: 'center', backgroundColor: '#f9fafb' },
  shopWrap: { flex: 1, backgroundColor: '#f9fafb', paddingHorizontal: 24 },
  lockWrap: { flex: 1, backgroundColor: '#f9fafb', alignItems: 'center', paddingHorizontal: 32 },
  lockIcon: { fontSize: 56, marginBottom: 12 },
  lockTitle: { fontSize: 26, fontWeight: '700', color: '#111827', marginBottom: 12 },
  plusBadge: {
    backgroundColor: '#14532d', borderRadius: 20,
    paddingVertical: 6, paddingHorizontal: 16, marginBottom: 20,
  },
  plusBadgeText: { color: '#ffffff', fontWeight: '700', fontSize: 13 },
  lockBody: { fontSize: 15, color: '#374151', textAlign: 'center', lineHeight: 22, marginBottom: 10 },
  lockSub: { fontSize: 14, color: '#6b7280', textAlign: 'center', marginBottom: 28 },
  ctaButton: {
    backgroundColor: '#16a34a', borderRadius: 12,
    paddingVertical: 15, paddingHorizontal: 28, alignSelf: 'stretch', alignItems: 'center',
  },
  ctaText: { color: '#ffffff', fontSize: 15, fontWeight: '700' },
});
