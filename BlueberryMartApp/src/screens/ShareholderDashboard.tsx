import React, { useEffect, useState } from 'react';
import {
  ActivityIndicator,
  ScrollView,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { getStoredToken, logout } from '../services/authService';
import ShoppingView from '../components/ShoppingView';
import type { RootStackParamList } from '../../App';

type ActiveView = 'analytics' | 'shopping';

type Props = {
  navigation: NativeStackNavigationProp<RootStackParamList, 'ShareholderDashboard'>;
};

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

interface BranchRevenue {
  branchName: string;
  revenue: number;
  orderCount: number;
}

interface TopItem {
  itemName: string;
  totalQuantitySold: number;
  totalRevenue: number;
}

interface LowStockItem {
  itemName: string;
  branchName: string;
  stockQuantity: number;
}

interface Analytics {
  totalRevenue: number;
  revenueByBranch: BranchRevenue[];
  topSellingItems: TopItem[];
  lowStockAlerts: LowStockItem[];
}


export default function ShareholderDashboard({ navigation }: Props) {
  const [activeView, setActiveView] = useState<ActiveView>('analytics');

  async function handleLogout() {
    await logout();
    navigation.replace('Login');
  }

  return (
    <View style={styles.container}>

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
  const [analytics, setAnalytics] = useState<Analytics | null>(null);
  const [loading, setLoading]     = useState(true);
  const [error, setError]         = useState<string | null>(null);

  useEffect(() => { fetchAnalytics(); }, []);

  async function fetchAnalytics() {
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/shareholders/analytics`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) throw new Error();
      setAnalytics(await res.json());
    } catch {
      setError('Failed to load analytics.');
    } finally {
      setLoading(false);
    }
  }

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color="#16a34a" />
      </View>
    );
  }

  if (error || !analytics) {
    return (
      <View style={styles.centered}>
        <Text style={styles.errorText}>{error ?? 'No data available.'}</Text>
      </View>
    );
  }

  return (
    <ScrollView style={styles.section} showsVerticalScrollIndicator={false}>

      {/* Total Revenue */}
      <View style={styles.revenueCard}>
        <Text style={styles.revenueLabel}>Total Revenue</Text>
        <Text style={styles.revenueValue}>
          Rs {analytics.totalRevenue.toLocaleString('en-NP', { minimumFractionDigits: 2 })}
        </Text>
      </View>

      {/* Revenue by Branch */}
      <Text style={styles.sectionTitle}>Revenue by Branch</Text>
      {analytics.revenueByBranch.length === 0 ? (
        <Text style={styles.emptyNote}>No orders yet.</Text>
      ) : (
        analytics.revenueByBranch.map((b, i) => (
          <View key={i} style={styles.rowCard}>
            <View>
              <Text style={styles.rowPrimary}>{b.branchName}</Text>
              <Text style={styles.rowSecondary}>{b.orderCount} orders</Text>
            </View>
            <Text style={styles.rowValue}>
              Rs {b.revenue.toLocaleString('en-NP', { minimumFractionDigits: 2 })}
            </Text>
          </View>
        ))
      )}

      {/* Top Selling Items */}
      <Text style={styles.sectionTitle}>Top Selling Items</Text>
      {analytics.topSellingItems.length === 0 ? (
        <Text style={styles.emptyNote}>No sales yet.</Text>
      ) : (
        analytics.topSellingItems.map((item, i) => (
          <View key={i} style={styles.rowCard}>
            <View style={styles.rankBadge}>
              <Text style={styles.rankText}>#{i + 1}</Text>
            </View>
            <View style={styles.rowMiddle}>
              <Text style={styles.rowPrimary}>{item.itemName}</Text>
              <Text style={styles.rowSecondary}>{item.totalQuantitySold} units sold</Text>
            </View>
            <Text style={styles.rowValue}>
              Rs {item.totalRevenue.toLocaleString('en-NP', { minimumFractionDigits: 2 })}
            </Text>
          </View>
        ))
      )}

      {/* Low Stock Alerts */}
      <Text style={styles.sectionTitle}>
        Low Stock Alerts
        {analytics.lowStockAlerts.length > 0 && (
          <Text style={styles.alertBadge}>  {analytics.lowStockAlerts.length} items</Text>
        )}
      </Text>
      {analytics.lowStockAlerts.length === 0 ? (
        <Text style={styles.emptyNote}>All items are well stocked.</Text>
      ) : (
        analytics.lowStockAlerts.map((item, i) => (
          <View key={i} style={styles.alertCard}>
            <View>
              <Text style={styles.alertItemName}>{item.itemName}</Text>
              <Text style={styles.alertBranch}>{item.branchName}</Text>
            </View>
            <View style={styles.stockBadge}>
              <Text style={styles.stockText}>{item.stockQuantity} left</Text>
            </View>
          </View>
        ))
      )}

      <View style={{ height: 16 }} />
    </ScrollView>
  );
}


const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#f0fdf4',
    paddingTop: 64,
    paddingHorizontal: 24,
  },
  centered: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
  },
  toggleButton: {
    backgroundColor: '#14532d',
    borderRadius: 10,
    paddingVertical: 12,
    alignItems: 'center',
    marginBottom: 20,
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
    fontSize: 22,
    fontWeight: '700',
    color: '#14532d',
    marginBottom: 4,
  },
  subheading: {
    fontSize: 13,
    color: '#6b7280',
    marginBottom: 20,
  },
  // Analytics
  revenueCard: {
    backgroundColor: '#14532d',
    borderRadius: 14,
    padding: 20,
    marginBottom: 20,
    alignItems: 'center',
  },
  revenueLabel: {
    color: '#bbf7d0',
    fontSize: 13,
    marginBottom: 6,
  },
  revenueValue: {
    color: '#ffffff',
    fontSize: 28,
    fontWeight: '700',
  },
  sectionTitle: {
    fontSize: 15,
    fontWeight: '700',
    color: '#14532d',
    marginBottom: 10,
    marginTop: 4,
  },
  alertBadge: {
    color: '#dc2626',
    fontWeight: '700',
  },
  rowCard: {
    backgroundColor: '#ffffff',
    borderRadius: 10,
    padding: 14,
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 8,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.05,
    shadowRadius: 4,
    elevation: 1,
  },
  rankBadge: {
    width: 28,
    height: 28,
    borderRadius: 14,
    backgroundColor: '#f0fdf4',
    justifyContent: 'center',
    alignItems: 'center',
    marginRight: 10,
  },
  rankText: {
    fontSize: 12,
    fontWeight: '700',
    color: '#16a34a',
  },
  rowMiddle: {
    flex: 1,
  },
  rowPrimary: {
    fontSize: 14,
    fontWeight: '600',
    color: '#111827',
  },
  rowSecondary: {
    fontSize: 12,
    color: '#6b7280',
    marginTop: 2,
  },
  rowValue: {
    fontSize: 13,
    fontWeight: '600',
    color: '#14532d',
    marginLeft: 8,
  },
  alertCard: {
    backgroundColor: '#fff7f7',
    borderRadius: 10,
    padding: 14,
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 8,
    borderLeftWidth: 3,
    borderLeftColor: '#dc2626',
  },
  alertItemName: {
    fontSize: 14,
    fontWeight: '600',
    color: '#111827',
  },
  alertBranch: {
    fontSize: 12,
    color: '#6b7280',
    marginTop: 2,
  },
  stockBadge: {
    backgroundColor: '#fee2e2',
    borderRadius: 8,
    paddingVertical: 4,
    paddingHorizontal: 10,
  },
  stockText: {
    fontSize: 12,
    fontWeight: '700',
    color: '#dc2626',
  },
  emptyNote: {
    fontSize: 13,
    color: '#9ca3af',
    marginBottom: 12,
  },
  errorText: {
    color: '#dc2626',
    fontSize: 13,
    textAlign: 'center',
    marginBottom: 12,
  },
  logoutButton: {
    marginTop: 16,
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
