import React, { useEffect, useMemo, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  FlatList,
  Image,
  RefreshControl,
  ScrollView,
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

// Category metadata, in display order. Each item is assigned a category by keyword
// matching on its name (categoryFor) — the inventory has no category column, so this
// mirrors the same name-based grouping the seed generator uses.
type CategoryKey =
  | 'Produce' | 'Meat & Poultry' | 'Dairy & Eggs' | 'Bakery'
  | 'Beverages' | 'Alcohol' | 'Grains & Pulses' | 'Cooking Oil' | 'Pantry' | 'Other';

const CATEGORIES: { key: CategoryKey; icon: keyof typeof Ionicons.glyphMap; color: string }[] = [
  { key: 'Produce', icon: 'nutrition', color: '#16a34a' },
  { key: 'Meat & Poultry', icon: 'restaurant', color: '#dc2626' },
  { key: 'Dairy & Eggs', icon: 'egg', color: '#f59e0b' },
  { key: 'Bakery', icon: 'pizza', color: '#d97706' },
  { key: 'Beverages', icon: 'cafe', color: '#0284c7' },
  { key: 'Alcohol', icon: 'wine', color: '#7c3aed' },
  { key: 'Grains & Pulses', icon: 'basket', color: '#ca8a04' },
  { key: 'Cooking Oil', icon: 'flask', color: '#65a30d' },
  { key: 'Pantry', icon: 'cube', color: '#0891b2' },
  { key: 'Other', icon: 'pricetag', color: '#6b7280' },
];
const CATEGORY_META = Object.fromEntries(CATEGORIES.map(c => [c.key, c])) as Record<CategoryKey, typeof CATEGORIES[number]>;

function has(haystack: string, ...needles: string[]) {
  return needles.some(n => haystack.includes(n));
}

function categoryFor(name: string): CategoryKey {
  const n = name.toLowerCase();
  if (has(n, 'beer', 'wine', 'whisky', 'whiskey', 'vodka', 'rum', 'gin', 'liquor', 'alcohol', 'cider')) return 'Alcohol';
  if (has(n, 'spinach', 'tomato', 'lettuce', 'veg', 'fruit', 'apple', 'banana')) return 'Produce';
  if (has(n, 'bread', 'sourdough', 'bun', 'bagel', 'pastry')) return 'Bakery';
  if (has(n, 'milk', 'yogurt', 'cheese', 'egg', 'butter', 'cream')) return 'Dairy & Eggs';
  if (has(n, 'chicken', 'meat', 'fish', 'beef', 'pork', 'mutton')) return 'Meat & Poultry';
  if (has(n, 'rice', 'lentil', 'flour', 'wheat', 'bean', 'pulse', 'grain')) return 'Grains & Pulses';
  if (has(n, 'oil')) return 'Cooking Oil';
  if (has(n, 'juice', 'water', 'soda', 'drink', 'beverage')) return 'Beverages';
  if (has(n, 'sugar', 'salt', 'spice')) return 'Pantry';
  return 'Other';
}

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
 *
 * Browse view groups items into categories: a pinned icon row up top to jump between
 * categories, then either horizontal rails per category ("All") or a vertical list for a
 * single picked category. Searching collapses everything to a flat list of matches.
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
  const [selected, setSelected] = useState<CategoryKey | 'All'>('All');

  const q = query.trim().toLowerCase();
  const isSearching = q.length > 0;
  const visible = isSearching ? inventory.filter(i => i.itemName.toLowerCase().includes(q)) : inventory;

  // Items grouped into the categories actually present, in display order.
  const sections = useMemo(() => {
    const map = new Map<CategoryKey, InventoryItem[]>();
    for (const it of inventory) {
      const c = categoryFor(it.itemName);
      (map.get(c) ?? map.set(c, []).get(c)!).push(it);
    }
    return CATEGORIES.filter(c => map.has(c.key)).map(c => ({ ...c, items: map.get(c.key)! }));
  }, [inventory]);

  // Keep the selected chip valid if the data (and so the available categories) changes.
  useEffect(() => {
    if (selected !== 'All' && !sections.some(s => s.key === selected)) setSelected('All');
  }, [sections, selected]);

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

  // Wide horizontal-row card, used for search results and a single picked category.
  function ItemRow({ item }: { item: InventoryItem }) {
    const outOfStock = item.stockQuantity <= 0;
    return (
      <View style={[styles.itemCard, outOfStock && styles.itemCardMuted]}>
        <Thumb url={item.imageUrl} name={item.itemName} />
        <View style={styles.itemInfo}>
          <Text style={[styles.itemName, outOfStock && styles.itemNameMuted]} numberOfLines={2}>{item.itemName}</Text>
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
  }

  // Compact vertical card for the horizontal category rails.
  function ProductCard({ item }: { item: InventoryItem }) {
    const outOfStock = item.stockQuantity <= 0;
    return (
      <View style={[styles.productCard, outOfStock && styles.itemCardMuted]}>
        <View style={styles.productImageWrap}>
          {item.imageUrl
            ? <Image source={{ uri: item.imageUrl }} style={[styles.productImage, outOfStock && styles.imageMuted]} />
            : <View style={[styles.productImage, styles.thumbPlaceholder]}><Text style={styles.productInitial}>{item.itemName.charAt(0).toUpperCase()}</Text></View>}
          <View style={styles.productControl}>
            {outOfStock
              ? <TouchableOpacity style={styles.notifyChip} onPress={() => notifyMe(item)} activeOpacity={0.8}><Ionicons name="notifications-outline" size={16} color="#16a34a" /></TouchableOpacity>
              : <QtyControl item={item} />}
          </View>
        </View>
        <Text style={[styles.productName, outOfStock && styles.itemNameMuted]} numberOfLines={2}>{item.itemName}</Text>
        <Text style={styles.productPrice}>Rs {item.price.toFixed(2)}</Text>
        <Text style={[styles.productStock, item.stockQuantity > 0 && item.stockQuantity <= LOW_STOCK && styles.lowStock]}>
          {stockLabel(item.stockQuantity)}
        </Text>
      </View>
    );
  }

  function CategoryRow() {
    return (
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={styles.chipRow}
        keyboardShouldPersistTaps="handled"
      >
        <TouchableOpacity style={styles.chip} onPress={() => setSelected('All')} activeOpacity={0.8}>
          <View style={[styles.chipIcon, { backgroundColor: '#14532d' }, selected === 'All' && styles.chipIconActive]}>
            <Ionicons name="grid" size={22} color="#fff" />
          </View>
          <Text style={[styles.chipLabel, selected === 'All' && styles.chipLabelActive]}>All</Text>
        </TouchableOpacity>
        {sections.map(s => (
          <TouchableOpacity key={s.key} style={styles.chip} onPress={() => setSelected(s.key)} activeOpacity={0.8}>
            <View style={[styles.chipIcon, { backgroundColor: s.color }, selected === s.key && styles.chipIconActive]}>
              <Ionicons name={s.icon} size={22} color="#fff" />
            </View>
            <Text style={[styles.chipLabel, selected === s.key && styles.chipLabelActive]} numberOfLines={2}>{s.key}</Text>
          </TouchableOpacity>
        ))}
      </ScrollView>
    );
  }

  const refresh = (
    <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor="#16a34a" colors={['#16a34a']} />
  );

  function Browse() {
    // Single category picked → vertical list of rows.
    if (selected !== 'All') {
      const meta = CATEGORY_META[selected];
      const items = sections.find(s => s.key === selected)?.items ?? [];
      return (
        <FlatList
          data={items}
          keyExtractor={i => i.id}
          contentContainerStyle={styles.list}
          refreshControl={refresh}
          keyboardShouldPersistTaps="handled"
          ListHeaderComponent={
            <View style={styles.sectionHeader}>
              <Ionicons name={meta.icon} size={18} color={meta.color} />
              <Text style={styles.sectionTitle}>{selected}</Text>
            </View>
          }
          ListEmptyComponent={<Text style={styles.emptyNote}>No items in this category.</Text>}
          renderItem={({ item }) => <ItemRow item={item} />}
        />
      );
    }

    // "All" → a vertical scroll of horizontal rails, one per category.
    if (sections.length === 0) {
      return (
        <ScrollView refreshControl={refresh} contentContainerStyle={styles.list}>
          <Text style={styles.emptyNote}>
            {isBulk ? 'No bulk items available at this branch.' : 'No items available at this branch.'}
          </Text>
        </ScrollView>
      );
    }
    return (
      <ScrollView
        refreshControl={refresh}
        showsVerticalScrollIndicator={false}
        contentContainerStyle={styles.railsContent}
        keyboardShouldPersistTaps="handled"
      >
        {sections.map(s => (
          <View key={s.key} style={styles.section}>
            <TouchableOpacity style={styles.sectionHeader} onPress={() => setSelected(s.key)} activeOpacity={0.7}>
              <Ionicons name={s.icon} size={18} color={s.color} />
              <Text style={styles.sectionTitle}>{s.key}</Text>
              <Ionicons name="chevron-forward" size={16} color="#9ca3af" />
            </TouchableOpacity>
            <FlatList
              data={s.items}
              keyExtractor={i => i.id}
              horizontal
              showsHorizontalScrollIndicator={false}
              contentContainerStyle={styles.rail}
              renderItem={({ item }) => <ProductCard item={item} />}
            />
          </View>
        ))}
      </ScrollView>
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
      ) : isSearching ? (
        <FlatList
          data={visible}
          keyExtractor={item => item.id}
          contentContainerStyle={styles.list}
          refreshControl={refresh}
          keyboardShouldPersistTaps="handled"
          ListEmptyComponent={<Text style={styles.emptyNote}>No items match &quot;{query}&quot;</Text>}
          renderItem={({ item }) => <ItemRow item={item} />}
        />
      ) : (
        <>
          {sections.length > 0 && <CategoryRow />}
          <Browse />
        </>
      )}
      {error && <Text style={styles.errorText}>{error}</Text>}
    </View>
  );
}

const CARD_WIDTH = 150;

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

  // Category chip row
  chipRow: { gap: 16, paddingBottom: 14, paddingRight: 8 },
  chip: { width: 64, alignItems: 'center', gap: 6 },
  chipIcon: { width: 56, height: 56, borderRadius: 16, justifyContent: 'center', alignItems: 'center' },
  chipIconActive: { borderWidth: 3, borderColor: '#bbf7d0' },
  chipLabel: { fontSize: 11, color: '#6b7280', textAlign: 'center', fontWeight: '600' },
  chipLabelActive: { color: '#14532d' },

  // Sections / rails
  railsContent: { paddingBottom: 24 },
  section: { marginBottom: 18 },
  sectionHeader: { flexDirection: 'row', alignItems: 'center', gap: 8, marginBottom: 10 },
  sectionTitle: { flex: 1, fontSize: 16, fontWeight: '700', color: '#111827' },
  rail: { gap: 12, paddingRight: 8 },

  // Vertical product card (rails)
  productCard: {
    width: CARD_WIDTH, backgroundColor: '#fff', borderRadius: 12, padding: 10,
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
  },
  productImageWrap: { position: 'relative', marginBottom: 8 },
  productImage: { width: '100%', height: 110, borderRadius: 8, backgroundColor: '#f3f4f6' },
  imageMuted: { opacity: 0.5 },
  productInitial: { fontSize: 32, fontWeight: '700', color: '#9ca3af' },
  productControl: { position: 'absolute', top: 6, right: 6 },
  productName: { fontSize: 13, fontWeight: '600', color: '#111827', marginBottom: 3, minHeight: 34 },
  productPrice: { fontSize: 13, fontWeight: '700', color: '#14532d' },
  productStock: { fontSize: 11, color: '#6b7280', marginTop: 2 },
  notifyChip: {
    width: 32, height: 32, borderRadius: 16, backgroundColor: '#fff',
    justifyContent: 'center', alignItems: 'center', borderWidth: 1, borderColor: '#16a34a',
  },

  // Wide row card (search + single category)
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
