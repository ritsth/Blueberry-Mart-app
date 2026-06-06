import React, { useCallback, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { Ionicons } from '@expo/vector-icons';
import { getStoredToken } from '../../services/authService';
import AppHeader from '../../components/AppHeader';
import ShoppingView from '../../components/ShoppingView';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';
const MONTHLY_FEE = 199;

export default function BulkTab() {
  const [isMember, setIsMember] = useState(false);
  const [loading, setLoading]   = useState(true);
  const [activating, setActivating] = useState(false);

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

  // Second confirmation layer before joining (same as the Account tab)
  function confirmJoin() {
    Alert.alert(
      'Join Blueberry Plus?',
      `You'll be charged Rs ${MONTHLY_FEE}/month for:\n\n` +
        '•  5% off every order\n' +
        '•  Free delivery\n' +
        '•  Bulk ordering\n\n' +
        'Your membership stays active for a full month and keeps its benefits ' +
        'even if you cancel before then. It renews monthly until you cancel.',
      [
        { text: 'Not now', style: 'cancel' },
        { text: `Join · Rs ${MONTHLY_FEE}/mo`, onPress: activateMembership },
      ],
    );
  }

  async function activateMembership() {
    setActivating(true);
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/membership/activate`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) {
        Alert.alert('Activation Failed', 'Could not activate membership. Try again.');
        return;
      }
      await fetchMembership();
      Alert.alert('Welcome to Blueberry Plus!', 'Bulk ordering is now unlocked.');
    } catch {
      Alert.alert('Error', 'Could not activate membership. Check your connection.');
    } finally {
      setActivating(false);
    }
  }

  if (loading) {
    return <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>;
  }

  // Members get the full bulk shopping flow (cart, delivery, member pricing)
  if (isMember) {
    return (
      <View style={styles.container}>
        <AppHeader />
        <View style={styles.shopWrap}>
          <ShoppingView mode="bulk" />
        </View>
      </View>
    );
  }

  // Non-members see an upsell that routes to the Account tab to join
  return (
    <View style={styles.container}>
      <AppHeader />
      <View style={styles.lockWrap}>
      <Ionicons name="cube-outline" size={54} color="#14532d" style={{ marginBottom: 12 }} />
      <Text style={styles.lockTitle}>Bulk Orders</Text>
      <View style={styles.plusBadge}>
        <Ionicons name="ribbon" size={13} color="#ffffff" />
        <Text style={styles.plusBadgeText}>Plus members only</Text>
      </View>
      <Text style={styles.lockBody}>
        Order business quantities — 25kg rice, 20L oil, 50kg flour and more —
        at member pricing with free delivery.
      </Text>
      <Text style={styles.lockSub}>Join Blueberry Plus to unlock bulk ordering.</Text>
      <TouchableOpacity
        style={[styles.ctaButton, activating && styles.ctaButtonDisabled]}
        onPress={confirmJoin}
        disabled={activating}
        activeOpacity={0.85}
      >
        {activating
          ? <ActivityIndicator color="#fff" />
          : <Text style={styles.ctaText}>Join Plus · Rs {MONTHLY_FEE}/mo</Text>}
      </TouchableOpacity>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f9fafb' },
  centered: { flex: 1, justifyContent: 'center', alignItems: 'center', backgroundColor: '#f9fafb' },
  shopWrap: { flex: 1, backgroundColor: '#f9fafb', paddingHorizontal: 24, paddingTop: 16 },
  lockWrap: { flex: 1, backgroundColor: '#f9fafb', alignItems: 'center', paddingHorizontal: 32, paddingTop: 40 },
  lockIcon: { fontSize: 56, marginBottom: 12 },
  lockTitle: { fontSize: 26, fontWeight: '700', color: '#111827', marginBottom: 12 },
  plusBadge: {
    flexDirection: 'row', alignItems: 'center', gap: 6,
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
  ctaButtonDisabled: { backgroundColor: '#86efac' },
  ctaText: { color: '#ffffff', fontSize: 15, fontWeight: '700' },
});
