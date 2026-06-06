import React, { useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  Dimensions,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { BarChart, LineChart, PieChart } from 'react-native-chart-kit';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import { Ionicons } from '@expo/vector-icons';
import { getStoredToken } from '../../services/authService';
import { SavedReport, deleteReport, listReports } from '../../services/analyticsService';
import { SavedReportCard } from '../../components/ReportChart';

const SCREEN_WIDTH = Dimensions.get('window').width;
const CHART_WIDTH = SCREEN_WIDTH - 48;

const chartConfig = {
  backgroundGradientFrom: '#ffffff',
  backgroundGradientTo: '#ffffff',
  decimalPlaces: 0,
  color: (opacity = 1) => `rgba(20, 83, 45, ${opacity})`,
  labelColor: (opacity = 1) => `rgba(107, 114, 128, ${opacity})`,
  propsForDots: { r: '4', strokeWidth: '2', stroke: '#16a34a' },
  barPercentage: 0.6,
};

const PIE_COLORS = ['#16a34a', '#0284c7', '#d97706', '#7c3aed'];
const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

interface BranchRevenue { branchName: string; revenue: number; orderCount: number; }
interface TopItem { itemName: string; totalQuantitySold: number; totalRevenue: number; }
interface LowStockItem { itemName: string; branchName: string; stockQuantity: number; }
interface DailyRevenue { date: string; revenue: number; }
interface OrderTypeSplit { type: string; count: number; revenue: number; }
interface Analytics {
  totalRevenue: number;
  revenueByBranch: BranchRevenue[];
  topSellingItems: TopItem[];
  lowStockAlerts: LowStockItem[];
  revenueOverTime: DailyRevenue[];
  orderTypeSplit: OrderTypeSplit[];
}

export default function ShareholderHomeTab() {
  const insets = useSafeAreaInsets();
  const navigation = useNavigation<any>();

  const [analytics, setAnalytics] = useState<Analytics | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reports, setReports] = useState<SavedReport[]>([]);
  const [reportsLoading, setReportsLoading] = useState(true);

  useEffect(() => { fetchAnalytics(); }, []);

  // Refresh saved reports whenever the tab regains focus (e.g. after saving in Explore).
  useFocusEffect(
    React.useCallback(() => {
      let alive = true;
      (async () => {
        try {
          const r = await listReports();
          if (alive) setReports(r);
        } catch {
          // leave the list as-is
        } finally {
          if (alive) setReportsLoading(false);
        }
      })();
      return () => { alive = false; };
    }, []),
  );

  function confirmDeleteReport(r: SavedReport) {
    Alert.alert('Delete report', `Delete "${r.name}"?`, [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Delete', style: 'destructive',
        onPress: async () => {
          try {
            await deleteReport(r.id);
            setReports(prev => prev.filter(x => x.id !== r.id));
          } catch (e: any) {
            Alert.alert('Delete failed', e?.message ?? '');
          }
        },
      },
    ]);
  }

  async function onRefresh() {
    setRefreshing(true);
    try { await fetchAnalytics(); } finally { setRefreshing(false); }
  }

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
    <ScrollView
      style={styles.scroll}
      contentContainerStyle={[styles.content, { paddingTop: insets.top + 16 }]}
      showsVerticalScrollIndicator={false}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor="#16a34a" colors={['#16a34a']} />}
    >
      <View style={styles.headerRow}>
        <Text style={styles.heading}>Analytics</Text>
        <TouchableOpacity onPress={() => navigation.navigate('Account')} activeOpacity={0.7}>
          <Ionicons name="person-circle-outline" size={28} color="#111827" />
        </TouchableOpacity>
      </View>

      <View style={styles.revenueCard}>
        <Text style={styles.revenueLabel}>Total Revenue</Text>
        <Text style={styles.revenueValue}>
          Rs {analytics.totalRevenue.toLocaleString('en-NP', { minimumFractionDigits: 2 })}
        </Text>
      </View>

      <Text style={styles.sectionTitle}>Revenue · last 14 days</Text>
      <View style={styles.chartCard}>
        <LineChart
          data={{
            labels: analytics.revenueOverTime.map((d, i) => {
              const dt = new Date(d.date);
              return i % 3 === 0 ? `${dt.getDate()}/${dt.getMonth() + 1}` : '';
            }),
            datasets: [{ data: analytics.revenueOverTime.map(d => d.revenue) }],
          }}
          width={CHART_WIDTH} height={200} chartConfig={chartConfig} bezier
          yAxisLabel="Rs " yAxisSuffix="" style={styles.chart}
        />
      </View>

      <Text style={styles.sectionTitle}>Revenue by Branch</Text>
      {analytics.revenueByBranch.length === 0 ? (
        <Text style={styles.emptyNote}>No orders yet.</Text>
      ) : (
        <>
          <View style={styles.chartCard}>
            <BarChart
              data={{
                labels: analytics.revenueByBranch.map(b => b.branchName.replace('Blueberry Mart ', '')),
                datasets: [{ data: analytics.revenueByBranch.map(b => b.revenue) }],
              }}
              width={CHART_WIDTH} height={220} chartConfig={chartConfig}
              yAxisLabel="Rs " yAxisSuffix="" fromZero showValuesOnTopOfBars style={styles.chart}
            />
          </View>
          {analytics.revenueByBranch.map((b, i) => (
            <View key={i} style={styles.rowCard}>
              <View>
                <Text style={styles.rowPrimary}>{b.branchName}</Text>
                <Text style={styles.rowSecondary}>{b.orderCount} orders</Text>
              </View>
              <Text style={styles.rowValue}>
                Rs {b.revenue.toLocaleString('en-NP', { minimumFractionDigits: 2 })}
              </Text>
            </View>
          ))}
        </>
      )}

      {analytics.orderTypeSplit.length > 0 && (
        <>
          <Text style={styles.sectionTitle}>Pickup vs Delivery</Text>
          <View style={styles.chartCard}>
            <PieChart
              data={analytics.orderTypeSplit.map((s, i) => ({
                name: s.type === 'delivery' ? 'Delivery' : 'Pickup',
                population: s.count,
                color: PIE_COLORS[i % PIE_COLORS.length],
                legendFontColor: '#374151',
                legendFontSize: 13,
              }))}
              width={CHART_WIDTH} height={170} chartConfig={chartConfig}
              accessor="population" backgroundColor="transparent" paddingLeft="12"
            />
          </View>
        </>
      )}

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

      <Text style={styles.sectionTitle}>My Reports</Text>
      {reportsLoading
        ? <ActivityIndicator color="#16a34a" style={{ marginVertical: 16 }} />
        : reports.length === 0
          ? <Text style={styles.emptyNote}>No saved reports yet. Build one in the Explore tab.</Text>
          : reports.map(r => (
            <SavedReportCard
              key={r.id}
              report={r}
              onEdit={() => navigation.navigate('Explore', { report: r })}
              onDelete={() => confirmDeleteReport(r)}
            />
          ))
      }

      <View style={{ height: 24 }} />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  scroll: { flex: 1, backgroundColor: '#f9fafb' },
  content: { paddingHorizontal: 24, paddingBottom: 32 },
  centered: { flex: 1, justifyContent: 'center', alignItems: 'center', backgroundColor: '#f9fafb' },
  errorText: { color: '#dc2626', fontSize: 13 },
  headerRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 },
  heading: { fontSize: 26, fontWeight: '700', color: '#111827' },
  revenueCard: { backgroundColor: '#14532d', borderRadius: 14, padding: 20, marginBottom: 20, alignItems: 'center' },
  revenueLabel: { color: '#bbf7d0', fontSize: 13, marginBottom: 6 },
  revenueValue: { color: '#ffffff', fontSize: 28, fontWeight: '700' },
  sectionTitle: { fontSize: 15, fontWeight: '700', color: '#111827', marginBottom: 10, marginTop: 4 },
  chartCard: {
    backgroundColor: '#ffffff', borderRadius: 14, paddingVertical: 12, marginBottom: 16,
    alignItems: 'center', overflow: 'hidden',
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.05, shadowRadius: 6, elevation: 1,
  },
  chart: { borderRadius: 12 },
  alertCount: { color: '#dc2626', fontWeight: '700' },
  rowCard: {
    backgroundColor: '#ffffff', borderRadius: 10, padding: 14,
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8,
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
  },
  rankBadge: { width: 28, height: 28, borderRadius: 14, backgroundColor: '#f0fdf4', justifyContent: 'center', alignItems: 'center', marginRight: 10 },
  rankText: { fontSize: 12, fontWeight: '700', color: '#16a34a' },
  rowPrimary: { fontSize: 14, fontWeight: '600', color: '#111827' },
  rowSecondary: { fontSize: 12, color: '#6b7280', marginTop: 2 },
  rowValue: { fontSize: 13, fontWeight: '600', color: '#14532d', marginLeft: 8 },
  alertCard: {
    backgroundColor: '#fff7f7', borderRadius: 10, padding: 14,
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    marginBottom: 8, borderLeftWidth: 3, borderLeftColor: '#dc2626',
  },
  stockBadge: { backgroundColor: '#fee2e2', borderRadius: 8, paddingVertical: 4, paddingHorizontal: 10 },
  stockText: { fontSize: 12, fontWeight: '700', color: '#dc2626' },
  emptyNote: { fontSize: 13, color: '#9ca3af', marginBottom: 12 },
});
