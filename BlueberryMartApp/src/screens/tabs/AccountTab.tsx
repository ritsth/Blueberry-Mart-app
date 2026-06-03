import React, { useEffect, useState } from 'react';
import {
  ActivityIndicator,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
  ScrollView,
} from 'react-native';
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
  totalOrders: number;
  totalSpent: number;
}

export default function AccountTab() {
  const insets = useSafeAreaInsets();
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const [profile, setProfile] = useState<ProfileSummary | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => { fetchProfile(); }, []);

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
    <ScrollView style={styles.container} contentContainerStyle={[styles.content, { paddingTop: insets.top + 12 }]}>
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
  signOutButton: {
    borderWidth: 1.5, borderColor: '#e5e7eb',
    borderRadius: 12, paddingVertical: 14, alignItems: 'center',
  },
  signOutText: { fontSize: 15, fontWeight: '600', color: '#374151' },
});
