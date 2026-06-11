import React, { useEffect, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  FlatList,
  RefreshControl,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from 'react-native';
import { useNavigation } from '@react-navigation/native';
import { Ionicons } from '@expo/vector-icons';
import { getStoredToken } from '../services/authService';
import { Branch, useCart } from '../context/CartContext';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

const PALETTE = ['#4f46e5', '#0284c7', '#059669', '#d97706', '#7c3aed', '#db2777'];
function branchColor(name: string) {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = name.charCodeAt(i) + ((h << 5) - h);
  return PALETTE[Math.abs(h) % PALETTE.length];
}

interface InventoryItem { id: string; itemName: string; price: number; stockQuantity: number; }
interface SearchItem { id: string; itemName: string; price: number; stockQuantity: number; }
interface SearchGroup { branchId: string; branchName: string; branchCity: string; items: SearchItem[]; }

export default function ShoppingView({ mode = 'regular' }: { mode?: 'regular' | 'bulk' }) {
  const navigation = useNavigation<any>();
  const isBulk = mode === 'bulk';
  const { carts, addToCart, updateQty } = useCart();

  const [branches, setBranches] = useState<Branch[]>([]);
  const [selectedBranch, setSelectedBranch] = useState<Branch | null>(null);
  const [inventory, setInventory] = useState<InventoryItem[]>([]);
  const [loadingBranches, setLoadingBranches] = useState(true);
  const [loadingInventory, setLoadingInventory] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [query, setQuery] = useState('');
  const [searchResults, setSearchResults] = useState<SearchGroup[]>([]);
  const [searching, setSearching] = useState(false);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => { fetchBranches(); }, []);

  // Pressing the bottom tab while inside a branch returns to the branch list.
  useEffect(() => {
    const unsub = navigation.addListener('tabPress', () => {
      setSelectedBranch(null);
      setQuery('');
    });
    return unsub;
  }, [navigation]);

  async function onRefresh() {
    setRefreshing(true);
    try {
      if (selectedBranch) await selectBranch(selectedBranch);
      else await fetchBranches();
    } finally {
      setRefreshing(false);
    }
  }

  // Debounced cross-branch search
  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    if (query.trim().length < 2) { setSearchResults([]); setSearching(false); return; }
    setSearching(true);
    debounceRef.current = setTimeout(() => runSearch(query.trim()), 350);
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current); };
  }, [query]);

  async function runSearch(q: string) {
    try {
      const token = await getStoredToken();
      const res = await fetch(
        `${API_BASE}/api/inventory/search?q=${encodeURIComponent(q)}${isBulk ? '&bulk=true' : ''}`,
        { headers: { Authorization: `Bearer ${token}` } },
      );
      if (!res.ok) throw new Error();
      setSearchResults(await res.json());
    } catch {
      setSearchResults([]);
    } finally {
      setSearching(false);
    }
  }

  async function fetchBranches() {
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/branches`, { headers: { Authorization: `Bearer ${token}` } });
      if (!res.ok) throw new Error();
      setBranches(await res.json());
    } catch {
      setError('Failed to load branches.');
    } finally {
      setLoadingBranches(false);
    }
  }

  async function selectBranch(branch: Branch) {
    setSelectedBranch(branch);
    setQuery('');
    setSearchResults([]);
    setInventory([]);
    setError(null);
    setLoadingInventory(true);
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
      setLoadingInventory(false);
    }
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

  const isSearching = query.trim().length >= 2;

  function QtyControl({ branchId, item }: { branchId: string; item: { id: string; itemName: string; price: number; stockQuantity: number } }) {
    const inCart = carts[branchId]?.items.find(c => c.itemId === item.id);
    if (!inCart) {
      return (
        <TouchableOpacity style={styles.addButton} onPress={() => addToCart(item, branches.find(b => b.id === branchId) ?? selectedBranch!)} activeOpacity={0.8}>
          <Ionicons name="add" size={16} color="#fff" />
        </TouchableOpacity>
      );
    }
    const atMax = inCart.quantity >= item.stockQuantity;
    return (
      <View style={styles.qtyRow}>
        <TouchableOpacity style={styles.qtyBtn} onPress={() => updateQty(branchId, item.id, -1)}><Ionicons name="remove" size={16} color="#16a34a" /></TouchableOpacity>
        <Text style={styles.qtyCount}>{inCart.quantity}</Text>
        <TouchableOpacity
          style={[styles.qtyBtn, atMax && styles.qtyBtnDisabled]}
          disabled={atMax}
          onPress={() => addToCart(item, branches.find(b => b.id === branchId) ?? selectedBranch!)}
        >
          <Ionicons name="add" size={16} color={atMax ? '#cbd5e1' : '#16a34a'} />
        </TouchableOpacity>
      </View>
    );
  }

  if (loadingBranches) {
    return <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>;
  }

  // ─── Inventory view (branch selected) ──────────────────────────────────────
  if (selectedBranch !== null) {
    return (
      <View style={styles.container}>
        <TouchableOpacity onPress={() => setSelectedBranch(null)} style={styles.backRow}>
          <Ionicons name="chevron-back" size={18} color="#16a34a" />
          <Text style={styles.backText}>All branches</Text>
        </TouchableOpacity>
        <Text style={styles.heading}>{selectedBranch.name}</Text>
        <Text style={styles.subheading}>{selectedBranch.city}</Text>

        {loadingInventory ? (
          <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>
        ) : (
          <FlatList
            data={inventory}
            keyExtractor={item => item.id}
            contentContainerStyle={styles.list}
            refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor="#16a34a" colors={['#16a34a']} />}
            ListEmptyComponent={<Text style={styles.emptyNote}>{isBulk ? 'No bulk items available at this branch.' : 'No items available at this branch.'}</Text>}
            renderItem={({ item }) => {
              const outOfStock = item.stockQuantity <= 0;
              return (
                <View style={[styles.itemCard, outOfStock && styles.itemCardMuted]}>
                  <View style={styles.itemInfo}>
                    <Text style={[styles.itemName, outOfStock && styles.itemNameMuted]}>{item.itemName}</Text>
                    <Text style={styles.itemMeta}>
                      Rs {item.price.toFixed(2)}{'  ·  '}{outOfStock ? 'Out of stock' : `${item.stockQuantity} in stock`}
                    </Text>
                  </View>
                  {outOfStock ? (
                    <TouchableOpacity style={styles.notifyButton} onPress={() => notifyMe(item)} activeOpacity={0.8}>
                      <Ionicons name="notifications-outline" size={14} color="#16a34a" />
                      <Text style={styles.notifyButtonText}>Notify me</Text>
                    </TouchableOpacity>
                  ) : (
                    <QtyControl branchId={selectedBranch.id} item={item} />
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

  // ─── Branch list + search ───────────────────────────────────────────────────
  return (
    <View style={styles.container}>
      <View style={styles.searchBar}>
        <Ionicons name="search" size={16} color="#9ca3af" style={{ marginRight: 8 }} />
        <TextInput
          style={styles.searchInput}
          placeholder={isBulk ? 'Search bulk items...' : 'Search across all branches...'}
          placeholderTextColor="#9ca3af"
          value={query}
          onChangeText={setQuery}
          returnKeyType="search"
          clearButtonMode="while-editing"
          autoCorrect={false}
        />
        {searching && <ActivityIndicator size="small" color="#9ca3af" style={{ marginRight: 8 }} />}
      </View>

      {isSearching ? (
        <FlatList
          data={searchResults}
          keyExtractor={g => g.branchId}
          contentContainerStyle={styles.list}
          refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor="#16a34a" colors={['#16a34a']} />}
          ListEmptyComponent={!searching ? <Text style={styles.emptyNote}>No items found for &quot;{query}&quot;</Text> : null}
          renderItem={({ item: group }) => {
            const color = branchColor(group.branchName);
            return (
              <View style={styles.searchGroup}>
                <View style={styles.searchGroupHeader}>
                  <View style={[styles.searchGroupLogo, { backgroundColor: color }]}>
                    <Ionicons name="storefront" size={18} color="#fff" />
                  </View>
                  <View>
                    <Text style={styles.searchGroupName}>{group.branchName}</Text>
                    <Text style={styles.searchGroupCity}>{group.branchCity}</Text>
                  </View>
                </View>
                {group.items.map(item => (
                  <View key={item.id} style={styles.itemCard}>
                    <View style={styles.itemInfo}>
                      <Text style={styles.itemName}>{item.itemName}</Text>
                      <Text style={styles.itemMeta}>Rs {item.price.toFixed(2)}{'  ·  '}{item.stockQuantity} in stock</Text>
                    </View>
                    <QtyControl branchId={group.branchId} item={item} />
                  </View>
                ))}
              </View>
            );
          }}
        />
      ) : (
        <>
          <Text style={styles.heading}>{isBulk ? 'Bulk Orders' : 'Shop by branch'}</Text>
          <Text style={styles.subheading}>
            {isBulk ? 'Business quantities · members only · select a branch' : 'Select a branch to start shopping'}
          </Text>
          {error && <Text style={styles.errorText}>{error}</Text>}
          <FlatList
            data={branches}
            keyExtractor={item => item.id}
            contentContainerStyle={styles.list}
            refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor="#16a34a" colors={['#16a34a']} />}
            renderItem={({ item }) => {
              const color = branchColor(item.name);
              const branchCart = carts[item.id];
              const count = branchCart?.items.reduce((s, i) => s + i.quantity, 0) ?? 0;
              return (
                <TouchableOpacity style={styles.branchCard} onPress={() => selectBranch(item)} activeOpacity={0.8}>
                  <View style={[styles.branchLogo, { backgroundColor: color }]}>
                    <Ionicons name="storefront" size={26} color="#fff" />
                  </View>
                  <View style={styles.branchInfo}>
                    <Text style={styles.branchName}>{item.name}</Text>
                    <Text style={styles.branchCity}>{item.city}</Text>
                    {count > 0 && (
                      <View style={styles.cartHintRow}>
                        <Ionicons name="cart" size={12} color="#16a34a" />
                        <Text style={styles.branchCartHint}>{count} in cart</Text>
                      </View>
                    )}
                  </View>
                  <Ionicons name="chevron-forward" size={20} color="#9ca3af" />
                </TouchableOpacity>
              );
            }}
          />
        </>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  centered: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  heading: { fontSize: 22, fontWeight: '700', color: '#14532d', marginBottom: 4 },
  subheading: { fontSize: 13, color: '#6b7280', marginBottom: 16 },
  list: { gap: 10, paddingBottom: 24 },
  backRow: { flexDirection: 'row', alignItems: 'center', marginBottom: 12 },
  backText: { color: '#16a34a', fontWeight: '600', fontSize: 14 },
  errorText: { color: '#dc2626', fontSize: 13, textAlign: 'center', marginBottom: 12 },
  emptyNote: { textAlign: 'center', color: '#9ca3af', marginTop: 40 },

  searchBar: {
    flexDirection: 'row', alignItems: 'center',
    backgroundColor: '#f3f4f6', borderRadius: 12,
    paddingHorizontal: 12, marginBottom: 20, height: 44,
  },
  searchInput: { flex: 1, fontSize: 15, color: '#111827' },

  searchGroup: { marginBottom: 8 },
  searchGroupHeader: { flexDirection: 'row', alignItems: 'center', marginBottom: 10 },
  searchGroupLogo: { width: 40, height: 40, borderRadius: 8, justifyContent: 'center', alignItems: 'center', marginRight: 10 },
  searchGroupName: { fontSize: 15, fontWeight: '700', color: '#111827' },
  searchGroupCity: { fontSize: 12, color: '#6b7280' },

  branchCard: {
    backgroundColor: '#ffffff', borderRadius: 14, padding: 14,
    flexDirection: 'row', alignItems: 'center',
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.07, shadowRadius: 6, elevation: 2,
  },
  branchLogo: { width: 60, height: 60, borderRadius: 12, justifyContent: 'center', alignItems: 'center', marginRight: 14 },
  branchInfo: { flex: 1 },
  branchName: { fontSize: 15, fontWeight: '700', color: '#111827', marginBottom: 2 },
  branchCity: { fontSize: 13, color: '#6b7280' },
  cartHintRow: { flexDirection: 'row', alignItems: 'center', gap: 4, marginTop: 3 },
  branchCartHint: { fontSize: 11, color: '#16a34a', fontWeight: '600' },

  itemCard: {
    backgroundColor: '#ffffff', borderRadius: 12, padding: 14,
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
    marginBottom: 8,
  },
  itemInfo: { flex: 1, marginRight: 12 },
  itemName: { fontSize: 14, fontWeight: '600', color: '#111827', marginBottom: 3 },
  itemMeta: { fontSize: 12, color: '#6b7280' },
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
