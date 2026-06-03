import React, { useState } from 'react';
import {
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { logout } from '../services/authService';
import type { RootStackParamList } from '../../App';

type Tab = 'shop' | 'analytics';

type Props = {
  navigation: NativeStackNavigationProp<RootStackParamList, 'ShareholderDashboard'>;
};

export default function ShareholderDashboard({ navigation }: Props) {
  const [activeTab, setActiveTab] = useState<Tab>('shop');

  async function handleLogout() {
    await logout();
    navigation.replace('Login');
  }

  return (
    <View style={styles.container}>
      {/* Tab bar */}
      <View style={styles.tabBar}>
        <TouchableOpacity
          style={[styles.tab, activeTab === 'shop' && styles.tabActive]}
          onPress={() => setActiveTab('shop')}
          activeOpacity={0.8}
        >
          <Text style={[styles.tabText, activeTab === 'shop' && styles.tabTextActive]}>
            🛒  Shop
          </Text>
        </TouchableOpacity>
        <TouchableOpacity
          style={[styles.tab, activeTab === 'analytics' && styles.tabActive]}
          onPress={() => setActiveTab('analytics')}
          activeOpacity={0.8}
        >
          <Text style={[styles.tabText, activeTab === 'analytics' && styles.tabTextActive]}>
            📊  Analytics
          </Text>
        </TouchableOpacity>
      </View>

      {/* Content */}
      <View style={styles.content}>
        {activeTab === 'shop' ? <ShopView /> : <AnalyticsView />}
      </View>

      <TouchableOpacity style={styles.logoutButton} onPress={handleLogout}>
        <Text style={styles.logoutText}>Sign Out</Text>
      </TouchableOpacity>
    </View>
  );
}

function ShopView() {
  return (
    <View style={styles.panel}>
      <Text style={styles.panelEmoji}>🏪</Text>
      <Text style={styles.panelTitle}>Shareholder Shop View</Text>
      <Text style={styles.panelSubtitle}>
        Full inventory — including bulk-only items — across all branches.
      </Text>
    </View>
  );
}

function AnalyticsView() {
  return (
    <View style={styles.panel}>
      <Text style={styles.panelEmoji}>📈</Text>
      <Text style={styles.panelTitle}>Business Analytics</Text>
      <Text style={styles.panelSubtitle}>
        Revenue by branch, top-selling items, and low-stock alerts.
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#f0fdf4',
    paddingTop: 56,
  },
  tabBar: {
    flexDirection: 'row',
    marginHorizontal: 24,
    backgroundColor: '#dcfce7',
    borderRadius: 12,
    padding: 4,
    marginBottom: 24,
  },
  tab: {
    flex: 1,
    paddingVertical: 10,
    borderRadius: 9,
    alignItems: 'center',
  },
  tabActive: {
    backgroundColor: '#ffffff',
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 2,
  },
  tabText:       { fontSize: 14, fontWeight: '500', color: '#6b7280' },
  tabTextActive: { color: '#14532d', fontWeight: '700' },
  content: {
    flex: 1,
    paddingHorizontal: 24,
  },
  panel: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
  },
  panelEmoji:    { fontSize: 52, marginBottom: 16 },
  panelTitle:    { fontSize: 22, fontWeight: '700', color: '#14532d', marginBottom: 8, textAlign: 'center' },
  panelSubtitle: { fontSize: 14, color: '#6b7280', textAlign: 'center', lineHeight: 22 },
  logoutButton: {
    margin: 24,
    borderWidth: 1,
    borderColor: '#d1fae5',
    borderRadius: 10,
    paddingVertical: 10,
    alignItems: 'center',
  },
  logoutText: { color: '#16a34a', fontWeight: '600' },
});
