import React, { useCallback, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  RefreshControl,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
  ScrollView,
} from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { getStoredToken, logout } from '../../services/authService';
import type { RootStackParamList } from '../../../App';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

interface ProfileSummary {
  email: string;
  role: string;
  loyaltyPoints: number;
  memberSince: string;
  isMember: boolean;
  membershipSince: string | null;
  totalOrders: number;
  totalSpent: number;
}

export default function AccountTab() {
  const insets = useSafeAreaInsets();
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const [profile, setProfile] = useState<ProfileSummary | null>(null);
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
      const res = await fetch(`${API_BASE}/api/profile`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.ok) setProfile(await res.json());
    } finally {
      setLoading(false);
    }
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
      await fetchProfile();
      Alert.alert('Welcome to Blueberry Plus!', 'You now get 5% off every order.');
    } catch {
      Alert.alert('Error', 'Could not activate membership. Check your connection.');
    } finally {
      setActivating(false);
    }
  }

  async function handleLogout() {
    await logout();
    navigation.replace('Login');
  }

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color="#16a34a" />
      </View>
    );
  }

  const initials = profile?.email.slice(0, 2).toUpperCase() ?? '??';
  const memberYear = profile ? new Date(profile.memberSince).getFullYear() : '—';

  return (
    <ScrollView
      style={styles.container}
      contentContainerStyle={[styles.content, { paddingTop: insets.top + 12 }]}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor="#16a34a" colors={['#16a34a']} />}
    >
      <Text style={styles.heading}>Account</Text>

      {/* Avatar */}
      <View style={styles.avatarCard}>
        <View style={styles.avatar}>
          <Text style={styles.avatarText}>{initials}</Text>
        </View>
        <Text style={styles.email}>{profile?.email ?? '—'}</Text>
        <View style={styles.roleBadge}>
          <Text style={styles.roleText}>
            {profile ? profile.role.charAt(0).toUpperCase() + profile.role.slice(1) : '—'}
          </Text>
        </View>
        <Text style={styles.memberSince}>Member since {memberYear}</Text>
      </View>

      {/* Stats */}
      <View style={styles.statsRow}>
        <View style={styles.statCard}>
          <Text style={styles.statValue}>{profile?.loyaltyPoints ?? 0}</Text>
          <Text style={styles.statLabel}>Loyalty{'\n'}Points</Text>
        </View>
        <View style={styles.statCard}>
          <Text style={styles.statValue}>{profile?.totalOrders ?? 0}</Text>
          <Text style={styles.statLabel}>Total{'\n'}Orders</Text>
        </View>
        <View style={styles.statCard}>
          <Text style={styles.statValue}>Rs {(profile?.totalSpent ?? 0).toFixed(0)}</Text>
          <Text style={styles.statLabel}>Total{'\n'}Spent</Text>
        </View>
      </View>

      {/* Delivery addresses link */}
      <TouchableOpacity
        style={styles.linkRow}
        onPress={() => navigation.navigate('AddressesScreen')}
        activeOpacity={0.8}
      >
        <Text style={styles.linkIcon}>📍</Text>
        <Text style={styles.linkLabel}>Delivery Addresses</Text>
        <Text style={styles.linkChevron}>›</Text>
      </TouchableOpacity>

      {/* Membership */}
      {profile?.isMember ? (
        <View style={styles.memberCard}>
          <Text style={styles.memberBadgeIcon}>🫐  Blueberry Plus</Text>
          <Text style={styles.memberActiveLabel}>Active Membership</Text>
          <View style={styles.perkRow}>
            <Text style={styles.perkText}>✓  5% off every order</Text>
          </View>
          {profile.membershipSince && (
            <Text style={styles.memberSinceNote}>
              Member since {new Date(profile.membershipSince).toLocaleDateString('en-NP', {
                day: 'numeric', month: 'short', year: 'numeric',
              })}
            </Text>
          )}
        </View>
      ) : (
        <View style={styles.joinCard}>
          <Text style={styles.joinIcon}>🫐</Text>
          <Text style={styles.joinTitle}>Join Blueberry Plus</Text>
          <Text style={styles.joinSubtitle}>Get 5% off every order, automatically applied at checkout.</Text>
          <TouchableOpacity
            style={[styles.joinButton, activating && styles.joinButtonDisabled]}
            onPress={activateMembership}
            disabled={activating}
            activeOpacity={0.8}
          >
            {activating
              ? <ActivityIndicator color="#fff" />
              : <Text style={styles.joinButtonText}>Become a Member</Text>}
          </TouchableOpacity>
        </View>
      )}

      {/* Sign out */}
      <TouchableOpacity style={styles.signOutButton} onPress={handleLogout} activeOpacity={0.8}>
        <Text style={styles.signOutText}>Sign Out</Text>
      </TouchableOpacity>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f9fafb' },
  content: { paddingHorizontal: 24, paddingBottom: 40 },
  centered: { flex: 1, justifyContent: 'center', alignItems: 'center', backgroundColor: '#f9fafb' },
  heading: { fontSize: 26, fontWeight: '700', color: '#111827', marginBottom: 24 },
  avatarCard: {
    backgroundColor: '#ffffff',
    borderRadius: 16,
    padding: 28,
    alignItems: 'center',
    marginBottom: 16,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 8,
    elevation: 2,
  },
  avatar: {
    width: 72, height: 72, borderRadius: 36,
    backgroundColor: '#14532d',
    justifyContent: 'center', alignItems: 'center',
    marginBottom: 12,
  },
  avatarText: { color: '#ffffff', fontSize: 26, fontWeight: '700' },
  email: { fontSize: 15, fontWeight: '600', color: '#111827', marginBottom: 8 },
  roleBadge: {
    backgroundColor: '#f0fdf4', borderRadius: 20,
    paddingVertical: 4, paddingHorizontal: 14,
    borderWidth: 1, borderColor: '#bbf7d0', marginBottom: 6,
  },
  roleText: { fontSize: 12, fontWeight: '700', color: '#16a34a' },
  memberSince: { fontSize: 12, color: '#9ca3af' },
  statsRow: { flexDirection: 'row', gap: 10, marginBottom: 32 },
  statCard: {
    flex: 1, backgroundColor: '#ffffff', borderRadius: 12,
    padding: 16, alignItems: 'center',
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
  },
  statValue: { fontSize: 18, fontWeight: '700', color: '#14532d', marginBottom: 4 },
  statLabel: { fontSize: 11, color: '#6b7280', textAlign: 'center', lineHeight: 15 },
  // Link row
  linkRow: {
    flexDirection: 'row', alignItems: 'center',
    backgroundColor: '#ffffff', borderRadius: 12,
    paddingVertical: 16, paddingHorizontal: 16, marginBottom: 16,
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
  },
  linkIcon: { fontSize: 18, marginRight: 12 },
  linkLabel: { flex: 1, fontSize: 15, fontWeight: '600', color: '#111827' },
  linkChevron: { fontSize: 20, color: '#9ca3af' },
  // Active member card
  memberCard: {
    backgroundColor: '#14532d', borderRadius: 16,
    padding: 22, marginBottom: 16,
  },
  memberBadgeIcon: { fontSize: 18, fontWeight: '800', color: '#ffffff', marginBottom: 4 },
  memberActiveLabel: { fontSize: 12, color: '#bbf7d0', fontWeight: '600', marginBottom: 14 },
  perkRow: { marginBottom: 10 },
  perkText: { fontSize: 14, color: '#ffffff', fontWeight: '500' },
  memberSinceNote: { fontSize: 11, color: '#86efac', marginTop: 4 },
  // Join card
  joinCard: {
    backgroundColor: '#ffffff', borderRadius: 16,
    padding: 24, marginBottom: 16, alignItems: 'center',
    borderWidth: 1.5, borderColor: '#bbf7d0',
  },
  joinIcon: { fontSize: 32, marginBottom: 8 },
  joinTitle: { fontSize: 18, fontWeight: '700', color: '#14532d', marginBottom: 6 },
  joinSubtitle: { fontSize: 13, color: '#6b7280', textAlign: 'center', lineHeight: 19, marginBottom: 18 },
  joinButton: {
    backgroundColor: '#16a34a', borderRadius: 12,
    paddingVertical: 14, paddingHorizontal: 32, alignSelf: 'stretch', alignItems: 'center',
  },
  joinButtonDisabled: { backgroundColor: '#86efac' },
  joinButtonText: { color: '#ffffff', fontSize: 15, fontWeight: '700' },
  signOutButton: {
    borderWidth: 1.5, borderColor: '#e5e7eb',
    borderRadius: 12, paddingVertical: 14, alignItems: 'center',
  },
  signOutText: { fontSize: 15, fontWeight: '600', color: '#374151' },
});
