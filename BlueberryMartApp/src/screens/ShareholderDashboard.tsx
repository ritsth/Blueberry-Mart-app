import React, { useState } from 'react';
import {
  FlatList,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { logout } from '../services/authService';
import type { RootStackParamList } from '../../App';

type View_ = 'analytics' | 'shopping';

type Props = {
  navigation: NativeStackNavigationProp<RootStackParamList, 'ShareholderDashboard'>;
};

const PLACEHOLDER_BRANCHES = [
  { id: '1', name: 'Blueberry Mart Downtown', city: 'Kathmandu' },
  { id: '2', name: 'Blueberry Mart Suburbs',  city: 'Lalitpur'  },
];

export default function ShareholderDashboard({ navigation }: Props) {
  const [activeView, setActiveView] = useState<View_>('analytics');

  async function handleLogout() {
    await logout();
    navigation.replace('Login');
  }

  return (
    <View style={styles.container}>

      {/* Toggle button */}
      <TouchableOpacity
        style={styles.toggleButton}
        onPress={() => setActiveView(v => v === 'analytics' ? 'shopping' : 'analytics')}
        activeOpacity={0.8}
      >
        <Text style={styles.toggleText}>
          {activeView === 'analytics' ? '🛒  Switch to Shopping View' : '📊  Switch to Analytics View'}
        </Text>
      </TouchableOpacity>

      {activeView === 'analytics' ? <AnalyticsView /> : <ShoppingView />}

      <TouchableOpacity style={styles.logoutButton} onPress={handleLogout}>
        <Text style={styles.logoutText}>Sign Out</Text>
      </TouchableOpacity>

    </View>
  );
}

function AnalyticsView() {
  return (
    <View style={styles.section}>
      <Text style={styles.heading}>Shareholder Analytics</Text>
      <Text style={styles.subheading}>Business metrics across all branches</Text>

      <View style={styles.metricsGrid}>
        {[
          { label: 'Total Revenue',    value: '—'  },
          { label: 'Active Branches',  value: '2'  },
          { label: 'Top Item',         value: '—'  },
          { label: 'Low Stock Alerts', value: '4'  },
        ].map(m => (
          <View key={m.label} style={styles.metricCard}>
            <Text style={styles.metricValue}>{m.value}</Text>
            <Text style={styles.metricLabel}>{m.label}</Text>
          </View>
        ))}
      </View>
    </View>
  );
}

function ShoppingView() {
  return (
    <View style={styles.section}>
      <Text style={styles.heading}>Welcome to the Grocery Store</Text>
      <Text style={styles.subheading}>Select a branch to start shopping</Text>

      <FlatList
        data={PLACEHOLDER_BRANCHES}
        keyExtractor={item => item.id}
        contentContainerStyle={styles.list}
        renderItem={({ item }) => (
          <View style={styles.branchCard}>
            <Text style={styles.branchName}>{item.name}</Text>
            <Text style={styles.branchCity}>{item.city}</Text>
          </View>
        )}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#f0fdf4',
    paddingTop: 64,
    paddingHorizontal: 24,
  },
  toggleButton: {
    backgroundColor: '#14532d',
    borderRadius: 10,
    paddingVertical: 12,
    alignItems: 'center',
    marginBottom: 28,
  },
  toggleText: {
    color: '#ffffff',
    fontWeight: '600',
    fontSize: 15,
  },
  section: {
    flex: 1,
  },
  heading: {
    fontSize: 24,
    fontWeight: '700',
    color: '#14532d',
    marginBottom: 4,
  },
  subheading: {
    fontSize: 14,
    color: '#6b7280',
    marginBottom: 24,
  },
  metricsGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 12,
  },
  metricCard: {
    flex: 1,
    minWidth: '45%',
    backgroundColor: '#ffffff',
    borderRadius: 12,
    padding: 18,
    alignItems: 'center',
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 6,
    elevation: 2,
  },
  metricValue: {
    fontSize: 28,
    fontWeight: '700',
    color: '#14532d',
    marginBottom: 4,
  },
  metricLabel: {
    fontSize: 12,
    color: '#6b7280',
    textAlign: 'center',
  },
  list: {
    gap: 12,
  },
  branchCard: {
    backgroundColor: '#ffffff',
    borderRadius: 12,
    padding: 18,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 6,
    elevation: 2,
  },
  branchName: {
    fontSize: 16,
    fontWeight: '600',
    color: '#14532d',
    marginBottom: 2,
  },
  branchCity: {
    fontSize: 13,
    color: '#6b7280',
  },
  logoutButton: {
    marginTop: 24,
    marginBottom: 40,
    borderWidth: 1,
    borderColor: '#d1fae5',
    borderRadius: 10,
    paddingVertical: 12,
    alignItems: 'center',
  },
  logoutText: {
    color: '#16a34a',
    fontWeight: '600',
  },
});
