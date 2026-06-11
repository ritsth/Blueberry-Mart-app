import React, { useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  FlatList,
  Image,
  RefreshControl,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from 'react-native';
import { useNavigation, useRoute } from '@react-navigation/native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { getStoredToken } from '../services/authService';
import { Branch, useCart } from '../context/CartContext';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

const LOW_STOCK = 5;
function stockLabel(qty: number): string {
  if (qty <= 0) return 'Out of stock';
  if (qty <= LOW_STOCK) return 'Only a few left';
  return 'In stock';
}

interface InventoryItem { id: string; itemName: string; price: number; stockQuantity: number; imageUrl: string | null; }

function Thumb({ url, name }: { url: string | null; name: string }) {
  if (url) return <Image source={{ uri: url }} style={styles.thumb} />;
  return (
    <View style={[styles.thumb, styles.thumbPlaceholder]}>
      <Text style={styles.thumbInitial}>{name.charAt(0).toUpperCase()}</Text>
    </View>
  );
}

/**
 * A single branch's inventory. A root-stack screen (not a tab view) so it gets the native
 * edge-swipe-back gesture — slide right to return to the branch list, like Account does.
 */
export default function BranchInventoryScreen() {
  const navigation = useNavigation<any>();
  const route = useRoute<any>();
  const insets = useSafeAreaInsets();
  const { branch, mode = 'regular' } = route.params as { branch: Branch; mode?: 'regular' | 'bulk' };
  const isBulk = mode === 'bulk';
  const { carts, addToCart, updateQty } = useCart();

  const [inventory, setInventory] = useState<InventoryItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');

  const q = query.trim().toLowerCase();
  const visible = q ? inventory.filter(i => i.itemName.toLowerCase().includes(q)) : inventory;

  useEffect(() => { load(); }, []);

  async function load() {
    setError(null);
    setLoading(true);
    try {
      const token = await getStoredToken();
      const endpoint = isBulk ? 'bulk' : 'customer';
      const qs = isBulk ? '' : '&includeOutOfStock=true';
      const res = await fetch(
        `${API_BASE}/api/inventory/${endpoint}?branchId=${branch.id}${qs}`,
        { headers: { Authorization: `Bearer ${token}` } },
      );
      if (!res.ok) throw new Error();
      setInventory(await res.json());
    } catch {
      setError('Failed to load inventory.');
    } finally {
      setLoading(false);
    }
  }

  async function onRefresh() {
    setRefreshing(true);
    try { await load(); } finally { setRefreshing(false); }
  }

  async function notifyMe(item: InventoryItem) {
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/inventory/${item.id}/notify-me`, {
        method: 'POST', headers: { Authorization: `Bearer ${token}` },
      });
      const body = await res.json().catch(() => ({}));
      Alert.alert(
        res.ok ? "You're on the list" : 'Heads up',
        body.message ?? (res.ok ? "We'll notify you when it's back." : 'Could not subscribe.'),
      );
    } catch {
      Alert.alert('Error', 'Could not subscribe. Check your connection.');
    }
  }

  function QtyControl({ item }: { item: InventoryItem }) {
    const inCart = carts[branch.id]?.items.find(c => c.itemId === item.id);
    if (!inCart) {
      return (
        <TouchableOpacity style={styles.addButton} onPress={() => addToCart(item, branch)} activeOpacity={0.8}>
          <Ionicons name="add" size={16} color="#fff" />
        </TouchableOpacity>
      );
    }
    const atMax = inCart.quantity >= item.stockQuantity;
    return (
      <View style={styles.qtyRow}>
        <TouchableOpacity style={styles.qtyBtn} onPress={() => updateQty(branch.id, item.id, -1)}><Ionicons name="remove" size={16} color="#16a34a" /></TouchableOpacity>
        <Text style={styles.qtyCount}>{inCart.quantity}</Text>
        <TouchableOpacity
          style={[styles.qtyBtn, atMax && styles.qtyBtnDisabled]}
          disabled={atMax}
          onPress={() => addToCart(item, branch)}
        >
          <Ionicons name="add" size={16} color={atMax ? '#cbd5e1' : '#16a34a'} />
        </TouchableOpacity>
      </View>
    );
  }

  return (
    <View style={[styles.container, { paddingTop: insets.top + 8 }]}>
      <TouchableOpacity onPress={() => navigation.goBack()} style={styles.backRow} activeOpacity={0.7}>
        <Ionicons name="chevron-back" size={18} color="#16a34a" />
        <Text style={styles.backText}>All branches</Text>
      </TouchableOpacity>
      <Text style={styles.heading}>{branch.name}</Text>
      <Text style={styles.subheading}>{branch.city}</Text>

      {!loading && (
        <View style={styles.searchBar}>
          <Ionicons name="search" size={16} color="#9ca3af" style={{ marginRight: 8 }} />
          <TextInput
            style={styles.searchInput}
            placeholder={`Search ${branch.name}...`}
            placeholderTextColor="#9ca3af"
            value={query}
            onChangeText={setQuery}
            returnKeyType="search"
            clearButtonMode="while-editing"
            autoCorrect={false}
          />
        </View>
      )}

      {loading ? (
        <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>
      ) : (
        <FlatList
          data={visible}
          keyExtractor={item => item.id}
          contentContainerStyle={styles.list}
          refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor="#16a34a" colors={['#16a34a']} />}
          keyboardShouldPersistTaps="handled"
          ListEmptyComponent={
            <Text style={styles.emptyNote}>
              {q
                ? `No items match "${query}"`
                : isBulk ? 'No bulk items available at this branch.' : 'No items available at this branch.'}
            </Text>
          }
          renderItem={({ item }) => {
            const outOfStock = item.stockQuantity <= 0;
            return (
              <View style={[styles.itemCard, outOfStock && styles.itemCardMuted]}>
                <Thumb url={item.imageUrl} name={item.itemName} />
                <View style={styles.itemInfo}>
                  <Text style={[styles.itemName, outOfStock && styles.itemNameMuted]}>{item.itemName}</Text>
                  <Text style={styles.itemMeta}>
                    Rs {item.price.toFixed(2)}{'  ·  '}
                    <Text style={item.stockQuantity > 0 && item.stockQuantity <= LOW_STOCK ? styles.lowStock : undefined}>
                      {stockLabel(item.stockQuantity)}
                    </Text>
                  </Text>
                </View>
                {outOfStock ? (
                  <TouchableOpacity style={styles.notifyButton} onPress={() => notifyMe(item)} activeOpacity={0.8}>
                    <Ionicons name="notifications-outline" size={14} color="#16a34a" />
                    <Text style={styles.notifyButtonText}>Notify me</Text>
                  </TouchableOpacity>
                ) : (
                  <QtyControl item={item} />
                )}
              </View>
            );
          }}
        />
      )}
      {error && <Text style={styles.errorText}>{error}</Text>}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f9fafb', paddingHorizontal: 24 },
  centered: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  heading: { fontSize: 22, fontWeight: '700', color: '#14532d', marginBottom: 4 },
  subheading: { fontSize: 13, color: '#6b7280', marginBottom: 16 },
  searchBar: {
    flexDirection: 'row', alignItems: 'center',
    backgroundColor: '#f3f4f6', borderRadius: 12,
    paddingHorizontal: 12, marginBottom: 16, height: 44,
  },
  searchInput: { flex: 1, fontSize: 15, color: '#111827' },
  list: { gap: 10, paddingBottom: 24 },
  backRow: { flexDirection: 'row', alignItems: 'center', marginBottom: 12 },
  backText: { color: '#16a34a', fontWeight: '600', fontSize: 14 },
  errorText: { color: '#dc2626', fontSize: 13, textAlign: 'center', marginBottom: 12 },
  emptyNote: { textAlign: 'center', color: '#9ca3af', marginTop: 40 },
  itemCard: {
    backgroundColor: '#ffffff', borderRadius: 12, padding: 14,
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
    marginBottom: 8,
  },
  thumb: { width: 44, height: 44, borderRadius: 8, marginRight: 12, backgroundColor: '#f3f4f6' },
  thumbPlaceholder: { justifyContent: 'center', alignItems: 'center', borderWidth: 1, borderColor: '#e5e7eb' },
  thumbInitial: { fontSize: 18, fontWeight: '700', color: '#9ca3af' },
  itemInfo: { flex: 1, marginRight: 12 },
  itemName: { fontSize: 14, fontWeight: '600', color: '#111827', marginBottom: 3 },
  itemMeta: { fontSize: 12, color: '#6b7280' },
  lowStock: { color: '#c2410c', fontWeight: '700' },
  itemCardMuted: { backgroundColor: '#f9fafb' },
  itemNameMuted: { color: '#9ca3af' },
  addButton: { backgroundColor: '#16a34a', borderRadius: 8, width: 36, height: 36, justifyContent: 'center', alignItems: 'center' },
  notifyButton: {
    flexDirection: 'row', alignItems: 'center', gap: 5,
    backgroundColor: '#fff', borderWidth: 1, borderColor: '#16a34a', borderRadius: 8, paddingVertical: 8, paddingHorizontal: 12,
  },
  notifyButtonText: { color: '#16a34a', fontWeight: '700', fontSize: 12 },
  qtyRow: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  qtyBtn: {
    width: 32, height: 32, borderRadius: 16,
    backgroundColor: '#f0fdf4', justifyContent: 'center', alignItems: 'center',
    borderWidth: 1, borderColor: '#bbf7d0',
  },
  qtyBtnDisabled: { backgroundColor: '#f3f4f6', borderColor: '#e5e7eb' },
  qtyCount: { fontSize: 15, fontWeight: '700', color: '#14532d', minWidth: 20, textAlign: 'center' },
});
