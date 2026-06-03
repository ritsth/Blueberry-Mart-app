import React, { useEffect, useState } from 'react';
import {
  ActivityIndicator,
  ScrollView,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { getStoredToken } from '../../services/authService';
import ShoppingView from '../../components/ShoppingView';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

type TopTab = 'analytics' | 'shopping';

interface BranchRevenue { branchName: string; revenue: number; orderCount: number; }
interface TopItem { itemName: string; totalQuantitySold: number; totalRevenue: number; }
interface LowStockItem { itemName: string; branchName: string; stockQuantity: number; }
interface Analytics {
  totalRevenue: number;
  revenueByBranch: BranchRevenue[];
  topSellingItems: TopItem[];
  lowStockAlerts: LowStockItem[];
}

export default function ShareholderHomeTab() {
  const insets = useSafeAreaInsets();
  const [activeTab, setActiveTab] = useState<TopTab>('analytics');

  return (
    <View style={styles.wrapper}>
      {/* Uber-style top tabs */}
      <View style={[styles.topBar, { paddingTop: insets.top + 12 }]}>
        {(['analytics', 'shopping'] as TopTab[]).map(tab => (
          <TouchableOpacity
            key={tab}
            style={styles.topTabBtn}
            onPress={() => setActiveTab(tab)}
            activeOpacity={0.7}
          >
            <Text style={[styles.topTabText, activeTab === tab && styles.topTabTextActive]}>
              {tab === 'analytics' ? '📊  Analytics' : '🛒  Shopping'}
            </Text>
            {activeTab === tab && <View style={styles.topTabUnderline} />}
          </TouchableOpacity>
        ))}
      </View>

      {activeTab === 'analytics'
        ? <AnalyticsView />
        : (
          <View style={styles.shopContainer}>
            <ShoppingView />
          </View>
        )
      }
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
    return <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>;
  }
  if (error || !analytics) {
    return <View style={styles.centered}><Text style={styles.errorText}>{error}</Text></View>;
  }

  return (
    <ScrollView style={styles.analyticsScroll} contentContainerStyle={styles.analyticsContent} showsVerticalScrollIndicator={false}>

      <View style={styles.revenueCard}>
        <Text style={styles.revenueLabel}>Total Revenue</Text>
        <Text style={styles.revenueValue}>
          Rs {analytics.totalRevenue.toLocaleString('en-NP', { minimumFractionDigits: 2 })}
        </Text>
      </View>

      <Text style={styles.sectionTitle}>Revenue by Branch</Text>
      {analytics.revenueByBranch.length === 0
        ? <Text style={styles.emptyNote}>No orders yet.</Text>
        : analytics.revenueByBranch.map((b, i) => (
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
      }

      <Text style={styles.sectionTitle}>Top Selling Items</Text>
      {analytics.topSellingItems.length === 0
        ? <Text style={styles.emptyNote}>No sales yet.</Text>
        : analytics.topSellingItems.map((item, i) => (
          <View key={i} style={styles.rowCard}>
            <View style={styles.rankBadge}><Text style={styles.rankText}>#{i + 1}</Text></View>
            <View style={{ flex: 1 }}>
              <Text style={styles.rowPrimary}>{item.itemName}</Text>
              <Text style={styles.rowSecondary}>{item.totalQuantitySold} units sold</Text>
            </View>
            <Text style={styles.rowValue}>
              Rs {item.totalRevenue.toLocaleString('en-NP', { minimumFractionDigits: 2 })}
            </Text>
          </View>
        ))
      }

      <Text style={styles.sectionTitle}>
        Low Stock Alerts
        {analytics.lowStockAlerts.length > 0 && (
          <Text style={styles.alertCount}>  {analytics.lowStockAlerts.length} items</Text>
        )}
      </Text>
      {analytics.lowStockAlerts.length === 0
        ? <Text style={styles.emptyNote}>All items are well stocked.</Text>
        : analytics.lowStockAlerts.map((item, i) => (
          <View key={i} style={styles.alertCard}>
            <View>
              <Text style={styles.rowPrimary}>{item.itemName}</Text>
              <Text style={styles.rowSecondary}>{item.branchName}</Text>
            </View>
            <View style={styles.stockBadge}>
              <Text style={styles.stockText}>{item.stockQuantity} left</Text>
            </View>
          </View>
        ))
      }

      <View style={{ height: 24 }} />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  wrapper: { flex: 1, backgroundColor: '#f9fafb' },
  // Top tabs (Uber style)
  topBar: {
    flexDirection: 'row',
    paddingHorizontal: 24,
    backgroundColor: '#ffffff',
    borderBottomWidth: 1,
    borderBottomColor: '#f3f4f6',
  },
  topTabBtn: { marginRight: 28, paddingBottom: 12, alignItems: 'center' },
  topTabText: { fontSize: 16, fontWeight: '600', color: '#9ca3af' },
  topTabTextActive: { color: '#111827' },
  topTabUnderline: {
    position: 'absolute',
    bottom: 0, left: 0, right: 0,
    height: 2.5, backgroundColor: '#111827', borderRadius: 2,
  },
  // Shopping
  shopContainer: { flex: 1, paddingHorizontal: 24 },
  // Analytics
  analyticsScroll: { flex: 1 },
  analyticsContent: { paddingHorizontal: 24, paddingTop: 20, paddingBottom: 32 },
  centered: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  errorText: { color: '#dc2626', fontSize: 13 },
  revenueCard: {
    backgroundColor: '#14532d', borderRadius: 14,
    padding: 20, marginBottom: 20, alignItems: 'center',
  },
  revenueLabel: { color: '#bbf7d0', fontSize: 13, marginBottom: 6 },
  revenueValue: { color: '#ffffff', fontSize: 28, fontWeight: '700' },
  sectionTitle: { fontSize: 15, fontWeight: '700', color: '#111827', marginBottom: 10, marginTop: 4 },
  alertCount: { color: '#dc2626', fontWeight: '700' },
  rowCard: {
    backgroundColor: '#ffffff', borderRadius: 10, padding: 14,
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    marginBottom: 8,
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
  },
  rankBadge: {
    width: 28, height: 28, borderRadius: 14,
    backgroundColor: '#f0fdf4', justifyContent: 'center', alignItems: 'center', marginRight: 10,
  },
  rankText: { fontSize: 12, fontWeight: '700', color: '#16a34a' },
  rowPrimary: { fontSize: 14, fontWeight: '600', color: '#111827' },
  rowSecondary: { fontSize: 12, color: '#6b7280', marginTop: 2 },
  rowValue: { fontSize: 13, fontWeight: '600', color: '#14532d', marginLeft: 8 },
  alertCard: {
    backgroundColor: '#fff7f7', borderRadius: 10, padding: 14,
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    marginBottom: 8, borderLeftWidth: 3, borderLeftColor: '#dc2626',
  },
  stockBadge: {
    backgroundColor: '#fee2e2', borderRadius: 8,
    paddingVertical: 4, paddingHorizontal: 10,
  },
  stockText: { fontSize: 12, fontWeight: '700', color: '#dc2626' },
  emptyNote: { fontSize: 13, color: '#9ca3af', marginBottom: 12 },
});
