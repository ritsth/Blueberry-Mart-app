import React, { useCallback, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { getStoredToken, getStoredUserId, logout } from '../../services/authService';
import { fetchStoreSettings } from '../../services/storeSettings';
import { tourKeyFor } from '../../components/OnboardingTour';
import type { RootStackParamList } from '../../../App';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';
const DEFAULT_MONTHLY_FEE = 199;

interface ProfileSummary {
  email: string;
  role: string;
  loyaltyPoints: number;
  memberSince: string;
  isMember: boolean;
  membershipSince: string | null;
  memberUntil: string | null;
  membershipCancelled: boolean;
  totalOrders: number;
  totalSpent: number;
}

export default function AccountTab() {
  const insets = useSafeAreaInsets();
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const [profile, setProfile] = useState<ProfileSummary | null>(null);
  const [monthlyFee, setMonthlyFee] = useState(DEFAULT_MONTHLY_FEE);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [activating, setActivating] = useState(false);

  useFocusEffect(useCallback(() => { fetchProfile(); }, []));

  async function onRefresh() {
    setRefreshing(true);
    try { await fetchProfile(); } finally { setRefreshing(false); }
  }

  async function fetchProfile() {
    try {
      const token = await getStoredToken();
      const [res, settings] = await Promise.all([
        fetch(`${API_BASE}/api/profile`, { headers: { Authorization: `Bearer ${token}` } }),
        fetchStoreSettings(),
      ]);
      if (res.ok) setProfile(await res.json());
      if (settings) setMonthlyFee(settings.membershipMonthlyFee);
    } finally {
      setLoading(false);
    }
  }

  function confirmJoin() {
    Alert.alert(
      'Join Blueberry Plus?',
      `You'll be charged Rs ${monthlyFee}/month for:\n\n` +
        '•  5% off every order\n•  Free delivery\n•  Bulk ordering\n\n' +
        'Your membership stays active for a full month and keeps its benefits ' +
        'even if you cancel before then. It renews monthly until you cancel.',
      [
        { text: 'Not now', style: 'cancel' },
        { text: `Join · Rs ${monthlyFee}/mo`, onPress: activateMembership },
      ],
    );
  }

  async function activateMembership() {
    setActivating(true);
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/membership/activate`, { method: 'POST', headers: { Authorization: `Bearer ${token}` } });
      if (!res.ok) { Alert.alert('Activation Failed', 'Could not activate membership. Try again.'); return; }
      await fetchProfile();
      Alert.alert('Welcome to Blueberry Plus!', 'You now get 5% off and free delivery.');
    } catch {
      Alert.alert('Error', 'Could not activate membership. Check your connection.');
    } finally {
      setActivating(false);
    }
  }

  function confirmCancel() {
    const until = profile?.memberUntil
      ? new Date(profile.memberUntil).toLocaleDateString('en-NP', { day: 'numeric', month: 'short', year: 'numeric' })
      : 'the end of your period';
    Alert.alert(
      'Cancel Membership?',
      `You'll keep your Plus benefits until ${until}. After that it won't renew and you'll lose the 5% discount and free delivery.`,
      [
        { text: 'Keep Membership', style: 'cancel' },
        { text: 'Cancel Membership', style: 'destructive', onPress: cancelMembership },
      ],
    );
  }

  async function cancelMembership() {
    setActivating(true);
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/membership/cancel`, { method: 'POST', headers: { Authorization: `Bearer ${token}` } });
      if (!res.ok) { Alert.alert('Failed', 'Could not cancel membership. Try again.'); return; }
      await fetchProfile();
    } catch {
      Alert.alert('Error', 'Could not cancel membership. Check your connection.');
    } finally {
      setActivating(false);
    }
  }

  async function handleLogout() {
    await logout();
    navigation.reset({ index: 0, routes: [{ name: 'Login' }] });
  }

  async function replayTour() {
    await AsyncStorage.removeItem(tourKeyFor(await getStoredUserId()));
    Alert.alert('Tour reset', 'The welcome tour will show next time you open the app.');
  }

  if (loading) {
    return <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>;
  }

  const initials = profile?.email.slice(0, 2).toUpperCase() ?? '??';
  const name = profile?.email.split('@')[0] ?? 'Account';
  const subtitle = profile
    ? `${profile.loyaltyPoints} points · ${profile.isMember ? 'Blueberry Plus member' : 'Free account'}`
    : '';
  // Shareholders/admins get Plus automatically (no paid period to renew or cancel).
  const complimentaryMember = !!profile?.isMember && !profile?.memberUntil;

  return (
    <ScrollView
      style={styles.container}
      contentContainerStyle={[styles.content, { paddingTop: insets.top + 16 }]}
      alwaysBounceVertical
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor="#16a34a" colors={['#16a34a']} />}
    >
      <TouchableOpacity style={styles.back} onPress={() => navigation.goBack()} hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}>
        <Ionicons name="chevron-back" size={26} color="#111827" />
      </TouchableOpacity>

      {/* Header */}
      <View style={styles.header}>
        <View style={styles.avatar}><Text style={styles.avatarText}>{initials}</Text></View>
        <View style={{ flex: 1 }}>
          <Text style={styles.name} numberOfLines={1}>{name}</Text>
          <Text style={styles.subtitle} numberOfLines={1}>{subtitle}</Text>
        </View>
      </View>

      {/* Quick actions */}
      <View style={styles.quickRow}>
        <QuickCard icon="receipt-outline" label="Orders" onPress={() => (navigation as any).navigate(profile?.role === 'shareholder' ? 'ShareholderTabs' : 'CustomerTabs', { screen: 'Activity' })} />
        <QuickCard icon="location-outline" label="Addresses" onPress={() => navigation.navigate('AddressesScreen')} />
        <QuickCard icon="headset-outline" label="Help" onPress={() => Alert.alert('Help & Support', 'Reach us at support@blueberrymart.com')} />
      </View>

      {/* Stats */}
      <View style={styles.statsRow}>
        <Stat value={`${profile?.loyaltyPoints ?? 0}`} label="Loyalty points" />
        <Stat value={`${profile?.totalOrders ?? 0}`} label="Total orders" />
        <Stat value={`Rs ${(profile?.totalSpent ?? 0).toFixed(0)}`} label="Total spent" />
      </View>

      {/* Membership */}
      <Text style={styles.sectionLabel}>Membership</Text>
      {profile?.isMember ? (
        <View style={styles.memberCard}>
          <View style={styles.memberHead}>
            <Ionicons name="ribbon" size={20} color="#ffffff" />
            <Text style={styles.memberTitle}>Blueberry Plus</Text>
          </View>
          <Text style={styles.memberStatus}>
            {profile.membershipCancelled ? 'Cancelled · benefits until period ends'
              : complimentaryMember ? 'Included with your account'
              : 'Active membership'}
          </Text>
          <View style={styles.perks}>
            {['5% off every order', 'Free delivery', 'Bulk ordering'].map(p => (
              <View key={p} style={styles.perkRow}>
                <Ionicons name="checkmark-circle" size={15} color="#86efac" />
                <Text style={styles.perkText}>{p}</Text>
              </View>
            ))}
          </View>
          {profile.memberUntil && (
            <Text style={styles.memberNote}>
              {profile.membershipCancelled ? 'Active until ' : 'Renews on '}
              {new Date(profile.memberUntil).toLocaleDateString('en-NP', { day: 'numeric', month: 'short', year: 'numeric' })}
            </Text>
          )}
          {complimentaryMember ? null : profile.membershipCancelled ? (
            <TouchableOpacity style={[styles.lightBtn, activating && styles.disabled]} onPress={confirmJoin} disabled={activating} activeOpacity={0.85}>
              {activating ? <ActivityIndicator color="#14532d" /> : <Text style={styles.lightBtnText}>Resume Membership</Text>}
            </TouchableOpacity>
          ) : (
            <TouchableOpacity style={styles.cancelLink} onPress={confirmCancel} disabled={activating} activeOpacity={0.7}>
              <Text style={styles.cancelLinkText}>Cancel membership</Text>
            </TouchableOpacity>
          )}
        </View>
      ) : (
        <View style={styles.joinCard}>
          <View style={styles.joinHead}>
            <Ionicons name="ribbon-outline" size={22} color="#14532d" />
            <Text style={styles.joinTitle}>Join Blueberry Plus</Text>
          </View>
          <Text style={styles.joinSubtitle}>
            Rs {monthlyFee}/month · 5% off every order, free delivery, and bulk ordering.
          </Text>
          <TouchableOpacity style={[styles.greenBtn, activating && styles.disabled]} onPress={confirmJoin} disabled={activating} activeOpacity={0.85}>
            {activating ? <ActivityIndicator color="#fff" /> : <Text style={styles.greenBtnText}>Become a member</Text>}
          </TouchableOpacity>
        </View>
      )}

      {/* Account rows */}
      <Text style={styles.sectionLabel}>Account</Text>
      <Row icon="refresh-outline" label="Replay app tour" onPress={replayTour} />
      <Row icon="log-out-outline" label="Sign out" danger onPress={handleLogout} />

      <View style={{ height: 24 }} />
    </ScrollView>
  );
}

function QuickCard({ icon, label, onPress }: { icon: any; label: string; onPress: () => void }) {
  return (
    <TouchableOpacity style={styles.quickCard} onPress={onPress} activeOpacity={0.8}>
      <Ionicons name={icon} size={22} color="#14532d" />
      <Text style={styles.quickLabel}>{label}</Text>
    </TouchableOpacity>
  );
}

function Stat({ value, label }: { value: string; label: string }) {
  return (
    <View style={styles.statCard}>
      <Text style={styles.statValue}>{value}</Text>
      <Text style={styles.statLabel}>{label}</Text>
    </View>
  );
}

function Row({ icon, label, onPress, danger }: { icon: any; label: string; onPress: () => void; danger?: boolean }) {
  return (
    <TouchableOpacity style={styles.row} onPress={onPress} activeOpacity={0.8}>
      <Ionicons name={icon} size={20} color={danger ? '#dc2626' : '#374151'} />
      <Text style={[styles.rowLabel, danger && { color: '#dc2626' }]}>{label}</Text>
      <Ionicons name="chevron-forward" size={18} color="#9ca3af" />
    </TouchableOpacity>
  );
}

const card = {
  backgroundColor: '#ffffff', borderRadius: 14,
  shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.05, shadowRadius: 5, elevation: 1,
} as const;

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f9fafb' },
  content: { flexGrow: 1, paddingHorizontal: 24, paddingBottom: 40 },
  back: { alignSelf: 'flex-start', marginBottom: 8, paddingVertical: 2 },
  centered: { flex: 1, justifyContent: 'center', alignItems: 'center', backgroundColor: '#f9fafb' },

  header: { flexDirection: 'row', alignItems: 'center', gap: 14, marginBottom: 20 },
  avatar: { width: 60, height: 60, borderRadius: 30, backgroundColor: '#14532d', justifyContent: 'center', alignItems: 'center' },
  avatarText: { color: '#ffffff', fontSize: 22, fontWeight: '700' },
  name: { fontSize: 22, fontWeight: '700', color: '#111827', textTransform: 'capitalize' },
  subtitle: { fontSize: 13, color: '#6b7280', marginTop: 2 },

  quickRow: { flexDirection: 'row', gap: 10, marginBottom: 20 },
  quickCard: { ...card, flex: 1, paddingVertical: 16, alignItems: 'center', gap: 8 },
  quickLabel: { fontSize: 13, fontWeight: '700', color: '#111827' },

  statsRow: { flexDirection: 'row', gap: 10, marginBottom: 24 },
  statCard: { ...card, flex: 1, padding: 14, alignItems: 'center' },
  statValue: { fontSize: 17, fontWeight: '700', color: '#14532d', marginBottom: 4 },
  statLabel: { fontSize: 11, color: '#6b7280', textAlign: 'center' },

  sectionLabel: { fontSize: 12, fontWeight: '700', color: '#6b7280', textTransform: 'uppercase', letterSpacing: 0.5, marginBottom: 10, marginTop: 4 },

  memberCard: { backgroundColor: '#14532d', borderRadius: 16, padding: 20, marginBottom: 20 },
  memberHead: { flexDirection: 'row', alignItems: 'center', gap: 8, marginBottom: 4 },
  memberTitle: { fontSize: 17, fontWeight: '800', color: '#ffffff' },
  memberStatus: { fontSize: 12, color: '#bbf7d0', fontWeight: '600', marginBottom: 14 },
  perks: { gap: 6, marginBottom: 8 },
  perkRow: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  perkText: { fontSize: 14, color: '#ffffff', fontWeight: '500' },
  memberNote: { fontSize: 11, color: '#86efac', marginTop: 4 },
  lightBtn: { backgroundColor: '#ffffff', borderRadius: 10, paddingVertical: 12, alignItems: 'center', marginTop: 16 },
  lightBtnText: { color: '#14532d', fontWeight: '700', fontSize: 14 },
  cancelLink: { marginTop: 14, alignItems: 'center', paddingVertical: 4 },
  cancelLinkText: { color: '#bbf7d0', fontWeight: '600', fontSize: 13, textDecorationLine: 'underline' },

  joinCard: { ...card, padding: 20, marginBottom: 20, borderWidth: 1.5, borderColor: '#bbf7d0' },
  joinHead: { flexDirection: 'row', alignItems: 'center', gap: 8, marginBottom: 8 },
  joinTitle: { fontSize: 17, fontWeight: '700', color: '#14532d' },
  joinSubtitle: { fontSize: 13, color: '#6b7280', lineHeight: 19, marginBottom: 16 },
  greenBtn: { backgroundColor: '#16a34a', borderRadius: 12, paddingVertical: 14, alignItems: 'center' },
  greenBtnText: { color: '#ffffff', fontSize: 15, fontWeight: '700' },
  disabled: { opacity: 0.6 },

  row: { ...card, flexDirection: 'row', alignItems: 'center', gap: 12, paddingVertical: 15, paddingHorizontal: 16, marginBottom: 10 },
  rowLabel: { flex: 1, fontSize: 15, fontWeight: '600', color: '#111827' },
});
