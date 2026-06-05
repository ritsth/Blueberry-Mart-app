import React, { useCallback, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  Image,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { getStoredToken } from '../../services/authService';
import EsewaCheckout, { PaymentOutcome } from '../../components/EsewaCheckout';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

// GCS returns absolute URLs; a local/relative path (e.g. "/images/reviews/x.jpg")
// is resolved against the API host so React Native can load it.
function resolveImageUrl(path: string): string {
  return /^https?:\/\//.test(path) ? path : `${API_BASE}${path}`;
}

interface OrderItem { itemId: string; itemName: string; quantity: number; unitPrice: number; }
interface Order {
  id: string; orderNumber: number; branchName: string; orderType: string;
  status: string; totalAmount: number; createdAt: string; items: OrderItem[];
  deliveryAddress?: string | null;
}
interface Review {
  id: string; itemName: string; rating: number; comment: string; createdAt: string;
  imagePath?: string | null;
}
interface ProfileData {
  orders: Order[];
  reviews: Review[];
}

export default function ActivityTab() {
  const insets = useSafeAreaInsets();
  const navigation = useNavigation<any>();
  const [data, setData]               = useState<ProfileData | null>(null);
  const [loading, setLoading]         = useState(true);
  const [refreshing, setRefreshing]   = useState(false);
  const [activeTab, setActiveTab]     = useState<'orders' | 'reviews'>('orders');
  const [expandedId, setExpandedId]   = useState<string | null>(null);
  const [payOrderId, setPayOrderId]   = useState<string | null>(null);

  useFocusEffect(useCallback(() => { fetchData(); }, []));

  // Customer confirms they received a paid order -> backend moves it to 'completed',
  // which unlocks reviewing it.
  async function markReceived(orderId: string) {
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/orders/${orderId}/receive`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        Alert.alert('Could not update', body.message ?? 'Something went wrong.');
        return;
      }
      await fetchData();
    } catch {
      Alert.alert('Error', 'Could not update the order. Check your connection.');
    }
  }

  // Retry payment for an unpaid (pending) order from the Activity list.
  async function onPaymentClose(outcome: PaymentOutcome) {
    setPayOrderId(null);
    await fetchData(); // refresh statuses so a now-paid order drops the "Not paid" tag
    if (outcome === 'success') {
      Alert.alert('Payment successful', 'Your order is confirmed.');
    }
  }

  async function onRefresh() {
    setRefreshing(true);
    try { await fetchData(); } finally { setRefreshing(false); }
  }

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
    <>
    <ScrollView
      style={styles.container}
      contentContainerStyle={[styles.content, { paddingTop: insets.top + 12 }]}
      showsVerticalScrollIndicator={false}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor="#16a34a" colors={['#16a34a']} />}
    >
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
                  <Text style={styles.orderNumber}>Order #{order.orderNumber}</Text>
                  <Text style={styles.orderBranch}>{order.branchName}</Text>
                  <Text style={styles.orderMeta}>
                    {new Date(order.createdAt).toLocaleDateString('en-NP', { day: 'numeric', month: 'short', year: 'numeric' })}
                  </Text>
                </View>
                <View style={{ alignItems: 'flex-end' }}>
                  <Text style={styles.orderTotal}>Rs {order.totalAmount.toFixed(2)}</Text>
                  <View style={[styles.statusBadge, order.status === 'pending' && styles.notPaidBadge]}>
                    <Text style={[styles.statusText, order.status === 'pending' && styles.notPaidText]}>
                      {order.status === 'pending' ? 'Not paid' : order.status}
                    </Text>
                  </View>
                </View>
              </View>

              {/* Fulfilment line: pickup code vs delivery address */}
              {order.orderType === 'delivery' ? (
                <View style={styles.fulfilRow}>
                  <Text style={styles.fulfilIcon}>🛵</Text>
                  <Text style={styles.fulfilText} numberOfLines={1}>
                    Delivery{order.deliveryAddress ? ` · ${order.deliveryAddress}` : ''}
                  </Text>
                </View>
              ) : (
                <View style={[styles.fulfilRow, styles.pickupRow]}>
                  <Text style={styles.fulfilIcon}>🏬</Text>
                  <Text style={styles.pickupText}>
                    Pickup · show <Text style={styles.pickupCode}>#{order.orderNumber}</Text> at the counter
                  </Text>
                </View>
              )}

              {order.status === 'pending' && (
                <TouchableOpacity
                  style={styles.payButton}
                  onPress={() => setPayOrderId(order.id)}
                  activeOpacity={0.85}
                >
                  <Text style={styles.payButtonText}>Pay now with eSewa</Text>
                </TouchableOpacity>
              )}
              {order.status === 'confirmed' && (
                <TouchableOpacity
                  style={styles.receiveButton}
                  onPress={() => markReceived(order.id)}
                  activeOpacity={0.85}
                >
                  <Text style={styles.receiveButtonText}>✓  Mark as received</Text>
                </TouchableOpacity>
              )}
              {order.status === 'completed' && (
                <TouchableOpacity
                  style={styles.reviewButton}
                  onPress={() => navigation.navigate('ReviewScreen', {
                    orderId: order.id,
                    items: order.items.map(i => ({ id: i.itemId, name: i.itemName })),
                  })}
                  activeOpacity={0.85}
                >
                  <Text style={styles.reviewButtonText}>★  Write a review</Text>
                </TouchableOpacity>
              )}

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
              {review.imagePath && (
                <Image
                  source={{ uri: resolveImageUrl(review.imagePath) }}
                  style={styles.reviewImage}
                  resizeMode="cover"
                />
              )}
            </View>
          ))
      )}

      <View style={{ height: 16 }} />
    </ScrollView>
    <EsewaCheckout orderId={payOrderId} onClose={onPaymentClose} />
    </>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f9fafb' },
  content: { paddingHorizontal: 24, paddingBottom: 32 },
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
  orderNumber: { fontSize: 14, fontWeight: '700', color: '#14532d', marginBottom: 2 },
  orderBranch: { fontSize: 13, fontWeight: '600', color: '#374151', marginBottom: 2 },
  orderMeta: { fontSize: 12, color: '#6b7280' },
  fulfilRow: {
    flexDirection: 'row', alignItems: 'center',
    marginTop: 10, paddingTop: 10,
    borderTopWidth: 1, borderTopColor: '#f3f4f6',
  },
  pickupRow: {},
  fulfilIcon: { fontSize: 14, marginRight: 8 },
  fulfilText: { fontSize: 12, color: '#6b7280', flex: 1 },
  pickupText: { fontSize: 12, color: '#374151', flex: 1 },
  pickupCode: { fontWeight: '800', color: '#14532d' },
  orderTotal: { fontSize: 14, fontWeight: '700', color: '#14532d', marginBottom: 4 },
  statusBadge: {
    backgroundColor: '#fef9c3', borderRadius: 6,
    paddingVertical: 2, paddingHorizontal: 8,
  },
  statusText: { fontSize: 11, fontWeight: '600', color: '#92400e' },
  notPaidBadge: { backgroundColor: '#fee2e2' },
  notPaidText: { color: '#b91c1c' },
  payButton: {
    backgroundColor: '#16a34a', borderRadius: 8,
    paddingVertical: 10, alignItems: 'center', marginTop: 12,
  },
  payButtonText: { color: '#ffffff', fontSize: 13, fontWeight: '700' },
  reviewButton: {
    backgroundColor: '#ffffff', borderRadius: 8, borderWidth: 1, borderColor: '#16a34a',
    paddingVertical: 10, alignItems: 'center', marginTop: 12,
  },
  reviewButtonText: { color: '#16a34a', fontSize: 13, fontWeight: '700' },
  receiveButton: {
    backgroundColor: '#14532d', borderRadius: 8,
    paddingVertical: 10, alignItems: 'center', marginTop: 12,
  },
  receiveButtonText: { color: '#ffffff', fontSize: 13, fontWeight: '700' },
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
  reviewImage: { width: '100%', height: 180, borderRadius: 10, marginTop: 10, backgroundColor: '#f3f4f6' },
  empty: { textAlign: 'center', color: '#9ca3af', fontSize: 13, marginTop: 32 },
});
