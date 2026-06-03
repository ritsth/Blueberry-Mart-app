import React, { useEffect, useState } from 'react';
import {
  ActivityIndicator,
  ScrollView,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { getStoredToken } from '../../services/authService';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

interface OrderItem { itemName: string; quantity: number; unitPrice: number; }
interface Order {
  id: string; branchName: string; orderType: string;
  status: string; totalAmount: number; createdAt: string; items: OrderItem[];
}
interface Review {
  id: string; itemName: string; rating: number; comment: string; createdAt: string;
}
interface ProfileData {
  orders: Order[];
  reviews: Review[];
}

export default function ActivityTab() {
  const [data, setData]               = useState<ProfileData | null>(null);
  const [loading, setLoading]         = useState(true);
  const [activeTab, setActiveTab]     = useState<'orders' | 'reviews'>('orders');
  const [expandedId, setExpandedId]   = useState<string | null>(null);

  useEffect(() => { fetchData(); }, []);

  async function fetchData() {
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/profile`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.ok) setData(await res.json());
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

  const orders  = data?.orders  ?? [];
  const reviews = data?.reviews ?? [];

  return (
    <ScrollView style={styles.container} contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
      <Text style={styles.heading}>Activity</Text>

      {/* Tabs */}
      <View style={styles.tabs}>
        <TouchableOpacity
          style={[styles.tab, activeTab === 'orders' && styles.tabActive]}
          onPress={() => setActiveTab('orders')}
        >
          <Text style={[styles.tabText, activeTab === 'orders' && styles.tabTextActive]}>
            Orders ({orders.length})
          </Text>
        </TouchableOpacity>
        <TouchableOpacity
          style={[styles.tab, activeTab === 'reviews' && styles.tabActive]}
          onPress={() => setActiveTab('reviews')}
        >
          <Text style={[styles.tabText, activeTab === 'reviews' && styles.tabTextActive]}>
            Reviews ({reviews.length})
          </Text>
        </TouchableOpacity>
      </View>

      {activeTab === 'orders' && (
        orders.length === 0
          ? <Text style={styles.empty}>No orders yet.</Text>
          : orders.map(order => (
            <TouchableOpacity
              key={order.id}
              style={styles.card}
              onPress={() => setExpandedId(expandedId === order.id ? null : order.id)}
              activeOpacity={0.8}
            >
              <View style={styles.orderHeader}>
                <View style={{ flex: 1, marginRight: 8 }}>
                  <Text style={styles.orderBranch}>{order.branchName}</Text>
                  <Text style={styles.orderMeta}>
                    {new Date(order.createdAt).toLocaleDateString('en-NP', { day: 'numeric', month: 'short', year: 'numeric' })}
                    {'  ·  '}{order.orderType}
                  </Text>
                </View>
                <View style={{ alignItems: 'flex-end' }}>
                  <Text style={styles.orderTotal}>Rs {order.totalAmount.toFixed(2)}</Text>
                  <View style={styles.statusBadge}>
                    <Text style={styles.statusText}>{order.status}</Text>
                  </View>
                </View>
              </View>

              {expandedId === order.id && (
                <View style={styles.itemsContainer}>
                  {order.items.map((item, i) => (
                    <View key={i} style={styles.itemRow}>
                      <Text style={styles.itemName}>{item.itemName}</Text>
                      <Text style={styles.itemQty}>×{item.quantity}</Text>
                      <Text style={styles.itemPrice}>Rs {(item.unitPrice * item.quantity).toFixed(2)}</Text>
                    </View>
                  ))}
                </View>
              )}

              <Text style={styles.expandHint}>
                {expandedId === order.id ? '▲ Hide items' : '▼ Show items'}
              </Text>
            </TouchableOpacity>
          ))
      )}

      {activeTab === 'reviews' && (
        reviews.length === 0
          ? <Text style={styles.empty}>No reviews yet.</Text>
          : reviews.map(review => (
            <View key={review.id} style={styles.card}>
              <View style={styles.reviewHeader}>
                <Text style={styles.reviewItem}>{review.itemName}</Text>
                <Text style={styles.reviewDate}>
                  {new Date(review.createdAt).toLocaleDateString('en-NP', { day: 'numeric', month: 'short', year: 'numeric' })}
                </Text>
              </View>
              <View style={styles.stars}>
                {[1,2,3,4,5].map(s => (
                  <Text key={s} style={s <= review.rating ? styles.starOn : styles.starOff}>★</Text>
                ))}
              </View>
              <Text style={styles.comment}>{review.comment}</Text>
            </View>
          ))
      )}

      <View style={{ height: 16 }} />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f9fafb' },
  content: { paddingTop: 64, paddingHorizontal: 24, paddingBottom: 32 },
  centered: { flex: 1, justifyContent: 'center', alignItems: 'center', backgroundColor: '#f9fafb' },
  heading: { fontSize: 26, fontWeight: '700', color: '#111827', marginBottom: 20 },
  tabs: {
    flexDirection: 'row', backgroundColor: '#ffffff',
    borderRadius: 10, padding: 4, marginBottom: 16,
    borderWidth: 1, borderColor: '#e5e7eb',
  },
  tab: { flex: 1, paddingVertical: 10, alignItems: 'center', borderRadius: 8 },
  tabActive: { backgroundColor: '#14532d' },
  tabText: { fontSize: 13, fontWeight: '600', color: '#6b7280' },
  tabTextActive: { color: '#ffffff' },
  card: {
    backgroundColor: '#ffffff', borderRadius: 12,
    padding: 16, marginBottom: 10,
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
  },
  orderHeader: { flexDirection: 'row', justifyContent: 'space-between' },
  orderBranch: { fontSize: 14, fontWeight: '600', color: '#111827', marginBottom: 2 },
  orderMeta: { fontSize: 12, color: '#6b7280' },
  orderTotal: { fontSize: 14, fontWeight: '700', color: '#14532d', marginBottom: 4 },
  statusBadge: {
    backgroundColor: '#fef9c3', borderRadius: 6,
    paddingVertical: 2, paddingHorizontal: 8,
  },
  statusText: { fontSize: 11, fontWeight: '600', color: '#92400e' },
  itemsContainer: { borderTopWidth: 1, borderTopColor: '#f0fdf4', marginTop: 10, paddingTop: 10, gap: 6 },
  itemRow: { flexDirection: 'row', alignItems: 'center' },
  itemName: { flex: 1, fontSize: 13, color: '#374151' },
  itemQty: { fontSize: 13, color: '#6b7280', marginRight: 12 },
  itemPrice: { fontSize: 13, fontWeight: '600', color: '#14532d' },
  expandHint: { fontSize: 11, color: '#9ca3af', marginTop: 10, textAlign: 'center' },
  reviewHeader: { flexDirection: 'row', justifyContent: 'space-between', marginBottom: 6 },
  reviewItem: { fontSize: 14, fontWeight: '600', color: '#111827', flex: 1, marginRight: 8 },
  reviewDate: { fontSize: 11, color: '#9ca3af' },
  stars: { flexDirection: 'row', gap: 2, marginBottom: 8 },
  starOn: { fontSize: 16, color: '#f59e0b' },
  starOff: { fontSize: 16, color: '#d1d5db' },
  comment: { fontSize: 13, color: '#374151', lineHeight: 18 },
  empty: { textAlign: 'center', color: '#9ca3af', fontSize: 13, marginTop: 32 },
});
