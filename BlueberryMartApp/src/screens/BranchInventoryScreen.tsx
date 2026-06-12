import React, { useEffect, useMemo, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  Dimensions,
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
import FloatingCart from '../components/FloatingCart';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

const LOW_STOCK = 5;
function stockLabel(qty: number): string {
  if (qty <= 0) return 'Out of stock';
  if (qty <= LOW_STOCK) return 'Only a few left';
  return 'In stock';
}

// Two cards per row in the grid, sized off screen width so the last odd card
// never stretches to fill the row.
const H_PADDING = 24;
const GRID_GAP = 12;
const GRID_CARD_W = (Dimensions.get('window').width - H_PADDING * 2 - GRID_GAP) / 2;
const RAIL_CARD_W = 150;

interface InventoryItem { id: string; itemName: string; price: number; stockQuantity: number; imageUrl: string | null; }

// Category metadata, in display order. Each item is assigned a category by keyword
// matching on its name (categoryFor) — the inventory has no category column, so this
// mirrors the same name-based grouping the seed generator uses. `label` is the short,
// single-line chip caption; `key` is the full section title.
type CategoryKey =
  | 'Produce' | 'Meat & Poultry' | 'Dairy & Eggs' | 'Bakery'
  | 'Beverages' | 'Alcohol' | 'Grains & Pulses' | 'Cooking Oil' | 'Pantry' | 'Other';

const CATEGORIES: { key: CategoryKey; label: string; icon: keyof typeof Ionicons.glyphMap; color: string }[] = [
  { key: 'Produce', label: 'Produce', icon: 'nutrition', color: '#16a34a' },
  { key: 'Meat & Poultry', label: 'Meat', icon: 'restaurant', color: '#dc2626' },
  { key: 'Dairy & Eggs', label: 'Dairy', icon: 'egg', color: '#f59e0b' },
  { key: 'Bakery', label: 'Bakery', icon: 'pizza', color: '#d97706' },
  { key: 'Beverages', label: 'Drinks', icon: 'cafe', color: '#0284c7' },
  { key: 'Alcohol', label: 'Alcohol', icon: 'wine', color: '#7c3aed' },
  { key: 'Grains & Pulses', label: 'Grains', icon: 'basket', color: '#ca8a04' },
  { key: 'Cooking Oil', label: 'Oil', icon: 'flask', color: '#65a30d' },
  { key: 'Pantry', label: 'Pantry', icon: 'cube', color: '#0891b2' },
  { key: 'Other', label: 'Other', icon: 'pricetag', color: '#6b7280' },
];
const CATEGORY_META = Object.fromEntries(CATEGORIES.map(c => [c.key, c])) as Record<CategoryKey, typeof CATEGORIES[number]>;

// Sold-out items get their own bucket (a chip + a section) so they stay visible with a
// "Notify me" action instead of being buried at the tail of a category rail.
const OUT_OF_STOCK = 'OutOfStock' as const;
const OOS_CAT = {
  key: 'Out of stock',
  icon: 'alert-circle' as keyof typeof Ionicons.glyphMap,
  color: '#9ca3af',
};

// Best sellers — a curated cross-category rail/chip of the branch's most-ordered items,
// fed by GET /api/inventory/top (ranked by units sold). Pinned first when there's history.
const POPULAR = 'Popular' as const;
const POPULAR_CAT = {
  key: 'Best sellers',
  icon: 'flame' as keyof typeof Ionicons.glyphMap,
  color: '#ea580c',
};

// Buy again — the signed-in customer's own previously-ordered items at this branch
// (GET /api/inventory/reorder, most-recent first). Personalized, so it sits above Best sellers.
const BUY_AGAIN = 'BuyAgain' as const;
const BUY_AGAIN_CAT = {
  key: 'Buy again',
  icon: 'repeat' as keyof typeof Ionicons.glyphMap,
  color: '#0d9488',
};

type ChipKey = CategoryKey | 'All' | typeof OUT_OF_STOCK | typeof POPULAR | typeof BUY_AGAIN;

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
 * Browse groups items into categories: a pinned icon row up top, then horizontal rails per
 * category ("All"), or a two-column grid of the same product cards when one category is
 * picked. Searching collapses everything to a flat list of matches.
 */
export default function BranchInventoryScreen() {
  const navigation = useNavigation<any>();
  const route = useRoute<any>();
  const insets = useSafeAreaInsets();
  const { branch, mode = 'regular' } = route.params as { branch: Branch; mode?: 'regular' | 'bulk' };
  const isBulk = mode === 'bulk';
  const { carts, addToCart, updateQty } = useCart();

  const [inventory, setInventory] = useState<InventoryItem[]>([]);
  const [topItems, setTopItems] = useState<InventoryItem[]>([]);
  const [reorder, setReorder] = useState<InventoryItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<ChipKey>('All');
  // Inventory ids the user has subscribed to this session → swap the bell for a check.
  const [notified, setNotified] = useState<Set<string>>(new Set());

  const q = query.trim().toLowerCase();
  const isSearching = q.length > 0;
  const visible = isSearching ? inventory.filter(i => i.itemName.toLowerCase().includes(q)) : inventory;

  // In-stock items grouped into the categories actually present, in display order.
  // Sold-out items are pulled into their own bucket (below) so they don't disappear
  // off the right edge of a horizontal rail.
  const sections = useMemo(() => {
    const map = new Map<CategoryKey, InventoryItem[]>();
    for (const it of inventory) {
      if (it.stockQuantity <= 0) continue;
      const c = categoryFor(it.itemName);
      (map.get(c) ?? map.set(c, []).get(c)!).push(it);
    }
    return CATEGORIES.filter(c => map.has(c.key)).map(c => ({ ...c, items: map.get(c.key)! }));
  }, [inventory]);

  const outOfStock = useMemo(
    () => inventory.filter(i => i.stockQuantity <= 0).sort((a, b) => a.itemName.localeCompare(b.itemName)),
    [inventory],
  );

  // Keep the selected chip valid if the data (and so the available categories) changes.
  useEffect(() => {
    if (selected === 'All') return;
    if (selected === OUT_OF_STOCK) {
      if (outOfStock.length === 0) setSelected('All');
      return;
    }
    if (selected === POPULAR) {
      if (topItems.length === 0) setSelected('All');
      return;
    }
    if (selected === BUY_AGAIN) {
      if (reorder.length === 0) setSelected('All');
      return;
    }
    if (!sections.some(s => s.key === selected)) setSelected('All');
  }, [sections, outOfStock, topItems, reorder, selected]);

  useEffect(() => { load(); }, []);

  async function load() {
    setError(null);
    setLoading(true);
    try {
      const token = await getStoredToken();
      const auth = { headers: { Authorization: `Bearer ${token}` } };
      const endpoint = isBulk ? 'bulk' : 'customer';
      const [invRes, topRes, reorderRes] = await Promise.all([
        fetch(`${API_BASE}/api/inventory/${endpoint}?branchId=${branch.id}&includeOutOfStock=true`, auth),
        fetch(`${API_BASE}/api/inventory/top?branchId=${branch.id}&bulk=${isBulk}`, auth),
        fetch(`${API_BASE}/api/inventory/reorder?branchId=${branch.id}&bulk=${isBulk}`, auth),
      ]);
      if (!invRes.ok) throw new Error();
      setInventory(await invRes.json());
      // Best sellers / Buy again are nice-to-haves — never fail the screen over them.
      setTopItems(topRes.ok ? await topRes.json() : []);
      setReorder(reorderRes.ok ? await reorderRes.json() : []);
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
    if (notified.has(item.id)) return;
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/inventory/${item.id}/notify-me`, {
        method: 'POST', headers: { Authorization: `Bearer ${token}` },
      });
      if (res.ok) {
        setNotified(prev => new Set(prev).add(item.id));   // flips the button to a check
      } else {
        const body = await res.json().catch(() => ({}));
        Alert.alert('Heads up', body.message ?? 'Could not subscribe.');
      }
    } catch {
      Alert.alert('Error', 'Could not subscribe. Check your connection.');
    }
  }

  // The thin add/qty control used in the search list rows.
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

  // The control overlaid on a product card. A solid green pill once in the cart so the
  // added state reads clearly against the photo.
  function CardControl({ item }: { item: InventoryItem }) {
    if (item.stockQuantity <= 0) {
      const done = notified.has(item.id);
      return (
        <TouchableOpacity
          style={[styles.notifyChip, done && styles.notifyChipDone]}
          onPress={() => notifyMe(item)}
          activeOpacity={0.8}
          disabled={done}
        >
          <Ionicons name={done ? 'checkmark' : 'notifications-outline'} size={16} color={done ? '#fff' : '#16a34a'} />
        </TouchableOpacity>
      );
    }
    const inCart = carts[branch.id]?.items.find(c => c.itemId === item.id);
    if (!inCart) {
      return (
        <TouchableOpacity style={styles.cardAddBtn} onPress={() => addToCart(item, branch)} activeOpacity={0.8}>
          <Ionicons name="add" size={20} color="#fff" />
        </TouchableOpacity>
      );
    }
    const atMax = inCart.quantity >= item.stockQuantity;
    return (
      <View style={styles.cardQtyPill}>
        <TouchableOpacity style={styles.cardQtyBtn} onPress={() => updateQty(branch.id, item.id, -1)}><Ionicons name="remove" size={16} color="#fff" /></TouchableOpacity>
        <Text style={styles.cardQtyCount}>{inCart.quantity}</Text>
        <TouchableOpacity style={styles.cardQtyBtn} disabled={atMax} onPress={() => addToCart(item, branch)}>
          <Ionicons name="add" size={16} color={atMax ? '#bbf7d0' : '#fff'} />
        </TouchableOpacity>
      </View>
    );
  }

  // Pill button for a sold-out search row: bell → "Notify me", check → "On the list".
  function NotifyButton({ item }: { item: InventoryItem }) {
    const done = notified.has(item.id);
    return (
      <TouchableOpacity
        style={[styles.notifyButton, done && styles.notifyButtonDone]}
        onPress={() => notifyMe(item)}
        activeOpacity={0.8}
        disabled={done}
      >
        <Ionicons name={done ? 'checkmark-circle' : 'notifications-outline'} size={14} color="#16a34a" />
        <Text style={styles.notifyButtonText}>{done ? 'On the list' : 'Notify me'}</Text>
      </TouchableOpacity>
    );
  }

  // Wide horizontal-row card, used for search results.
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
        {outOfStock ? <NotifyButton item={item} /> : <QtyControl item={item} />}
      </View>
    );
  }

  // Compact vertical card for both the rails and the per-category grid.
  function ProductCard({ item, width }: { item: InventoryItem; width: number }) {
    const outOfStock = item.stockQuantity <= 0;
    return (
      <View style={[styles.productCard, { width }, outOfStock && styles.itemCardMuted]}>
        <View style={styles.productImageWrap}>
          {item.imageUrl
            ? <Image source={{ uri: item.imageUrl }} style={[styles.productImage, outOfStock && styles.imageMuted]} />
            : <View style={[styles.productImage, styles.thumbPlaceholder]}><Text style={styles.productInitial}>{item.itemName.charAt(0).toUpperCase()}</Text></View>}
          <View style={styles.productControl}><CardControl item={item} /></View>
        </View>
        <Text style={[styles.productName, outOfStock && styles.itemNameMuted]} numberOfLines={2}>{item.itemName}</Text>
        <Text style={styles.productPrice}>Rs {item.price.toFixed(2)}</Text>
        <Text style={[styles.productStock, item.stockQuantity > 0 && item.stockQuantity <= LOW_STOCK && styles.lowStock]}>
          {stockLabel(item.stockQuantity)}
        </Text>
      </View>
    );
  }

  function SectionHeader({ cat, onPress }: { cat: { key: string; icon: keyof typeof Ionicons.glyphMap; color: string }; onPress?: () => void }) {
    const inner = (
      <>
        <Ionicons name={cat.icon} size={18} color={cat.color} />
        <Text style={styles.sectionTitle}>{cat.key}</Text>
        {onPress && <Ionicons name="chevron-forward" size={16} color="#9ca3af" />}
      </>
    );
    return onPress
      ? <TouchableOpacity style={styles.sectionHeader} onPress={onPress} activeOpacity={0.7}>{inner}</TouchableOpacity>
      : <View style={styles.sectionHeader}>{inner}</View>;
  }

  function ChipRow() {
    const chips: { key: ChipKey; label: string; icon: keyof typeof Ionicons.glyphMap; color: string }[] = [
      { key: 'All', label: 'All', icon: 'grid', color: '#14532d' },
      ...(reorder.length ? [{ key: BUY_AGAIN, label: 'Buy again', icon: BUY_AGAIN_CAT.icon, color: BUY_AGAIN_CAT.color }] : []),
      ...(topItems.length ? [{ key: POPULAR, label: 'Popular', icon: POPULAR_CAT.icon, color: POPULAR_CAT.color }] : []),
      ...sections.map(s => ({ key: s.key, label: s.label, icon: s.icon, color: s.color })),
      ...(outOfStock.length ? [{ key: OUT_OF_STOCK, label: 'Sold out', icon: OOS_CAT.icon, color: OOS_CAT.color }] : []),
    ];
    return (
      <View style={styles.chipBar}>
        <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.chipRow} keyboardShouldPersistTaps="handled">
          {chips.map(c => {
            const active = selected === c.key;
            return (
              <TouchableOpacity key={c.key} style={styles.chip} onPress={() => setSelected(c.key)} activeOpacity={0.8}>
                <View style={[styles.chipIcon, { backgroundColor: c.color }, active && styles.chipIconActive]}>
                  <Ionicons name={c.icon} size={22} color="#fff" />
                </View>
                <Text style={[styles.chipLabel, active && styles.chipLabelActive]} numberOfLines={1}>{c.label}</Text>
              </TouchableOpacity>
            );
          })}
        </ScrollView>
      </View>
    );
  }

  const refresh = (
    <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor="#16a34a" colors={['#16a34a']} />
  );

  if (loading) {
    return (
      <View style={[styles.container, { paddingTop: insets.top + 8 }]}>
        <Header branch={branch} onBack={() => navigation.goBack()} />
        <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>
      </View>
    );
  }

  return (
    <View style={[styles.container, { paddingTop: insets.top + 8 }]}>
      <Header branch={branch} onBack={() => navigation.goBack()} />

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

      {isSearching ? (
        <FlatList
          key="search"
          style={styles.flex}
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
          {(sections.length > 0 || outOfStock.length > 0 || topItems.length > 0 || reorder.length > 0) && <ChipRow />}

          {selected !== 'All' ? (
            <FlatList
              key="grid"
              style={styles.flex}
              data={
                selected === OUT_OF_STOCK ? outOfStock
                  : selected === POPULAR ? topItems
                    : selected === BUY_AGAIN ? reorder
                      : (sections.find(s => s.key === selected)?.items ?? [])
              }
              keyExtractor={i => i.id}
              numColumns={2}
              columnWrapperStyle={styles.gridRow}
              contentContainerStyle={styles.list}
              refreshControl={refresh}
              keyboardShouldPersistTaps="handled"
              ListHeaderComponent={
                <SectionHeader cat={
                  selected === OUT_OF_STOCK ? OOS_CAT
                    : selected === POPULAR ? POPULAR_CAT
                      : selected === BUY_AGAIN ? BUY_AGAIN_CAT
                        : CATEGORY_META[selected]
                } />
              }
              ListEmptyComponent={<Text style={styles.emptyNote}>No items in this category.</Text>}
              renderItem={({ item }) => <ProductCard item={item} width={GRID_CARD_W} />}
            />
          ) : (
            <FlatList
              key="rails"
              style={styles.flex}
              data={sections}
              keyExtractor={s => s.key}
              contentContainerStyle={styles.railsContent}
              refreshControl={refresh}
              keyboardShouldPersistTaps="handled"
              showsVerticalScrollIndicator={false}
              ListEmptyComponent={
                (outOfStock.length || topItems.length || reorder.length) ? null : (
                  <Text style={styles.emptyNote}>
                    {isBulk ? 'No bulk items available at this branch.' : 'No items available at this branch.'}
                  </Text>
                )
              }
              ListHeaderComponent={
                <>
                  {reorder.length ? (
                    <View style={styles.section}>
                      <SectionHeader cat={BUY_AGAIN_CAT} onPress={() => setSelected(BUY_AGAIN)} />
                      <FlatList
                        data={reorder}
                        keyExtractor={i => i.id}
                        horizontal
                        showsHorizontalScrollIndicator={false}
                        contentContainerStyle={styles.rail}
                        renderItem={({ item }) => <ProductCard item={item} width={RAIL_CARD_W} />}
                      />
                    </View>
                  ) : null}
                  {topItems.length ? (
                    <View style={styles.section}>
                      <SectionHeader cat={POPULAR_CAT} onPress={() => setSelected(POPULAR)} />
                      <FlatList
                        data={topItems}
                        keyExtractor={i => i.id}
                        horizontal
                        showsHorizontalScrollIndicator={false}
                        contentContainerStyle={styles.rail}
                        renderItem={({ item }) => <ProductCard item={item} width={RAIL_CARD_W} />}
                      />
                    </View>
                  ) : null}
                </>
              }
              ListFooterComponent={
                outOfStock.length ? (
                  <View style={styles.section}>
                    <SectionHeader cat={OOS_CAT} onPress={() => setSelected(OUT_OF_STOCK)} />
                    <FlatList
                      data={outOfStock}
                      keyExtractor={i => i.id}
                      horizontal
                      showsHorizontalScrollIndicator={false}
                      contentContainerStyle={styles.rail}
                      renderItem={({ item }) => <ProductCard item={item} width={RAIL_CARD_W} />}
                    />
                  </View>
                ) : null
              }
              renderItem={({ item: s }) => (
                <View style={styles.section}>
                  <SectionHeader cat={s} onPress={() => setSelected(s.key)} />
                  <FlatList
                    data={s.items}
                    keyExtractor={i => i.id}
                    horizontal
                    showsHorizontalScrollIndicator={false}
                    contentContainerStyle={styles.rail}
                    renderItem={({ item }) => <ProductCard item={item} width={RAIL_CARD_W} />}
                  />
                </View>
              )}
            />
          )}
        </>
      )}
      {error && <Text style={styles.errorText}>{error}</Text>}
      <FloatingCart />
    </View>
  );
}

function Header({ branch, onBack }: { branch: Branch; onBack: () => void }) {
  return (
    <>
      <TouchableOpacity onPress={onBack} style={styles.backRow} activeOpacity={0.7}>
        <Ionicons name="chevron-back" size={18} color="#16a34a" />
        <Text style={styles.backText}>All branches</Text>
      </TouchableOpacity>
      <Text style={styles.heading}>{branch.name}</Text>
      <Text style={styles.subheading}>{branch.city}</Text>
    </>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f9fafb', paddingHorizontal: H_PADDING },
  flex: { flex: 1 },
  centered: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  heading: { fontSize: 22, fontWeight: '700', color: '#14532d', marginBottom: 4 },
  subheading: { fontSize: 13, color: '#6b7280', marginBottom: 16 },
  searchBar: {
    flexDirection: 'row', alignItems: 'center',
    backgroundColor: '#f3f4f6', borderRadius: 12,
    paddingHorizontal: 12, marginBottom: 12, height: 44,
  },
  searchInput: { flex: 1, fontSize: 15, color: '#111827' },
  list: { gap: 12, paddingBottom: 96, paddingTop: 4 },
  gridRow: { gap: GRID_GAP },
  backRow: { flexDirection: 'row', alignItems: 'center', marginBottom: 12 },
  backText: { color: '#16a34a', fontWeight: '600', fontSize: 14 },
  errorText: { color: '#dc2626', fontSize: 13, textAlign: 'center', marginBottom: 12 },
  emptyNote: { textAlign: 'center', color: '#9ca3af', marginTop: 40 },

  // Category chip row — fixed height so the list below can never ride up over it.
  chipBar: { height: 96, marginBottom: 4 },
  chipRow: { gap: 14, paddingRight: 8, alignItems: 'flex-start' },
  chip: { width: 60, alignItems: 'center' },
  chipIcon: { width: 56, height: 56, borderRadius: 16, justifyContent: 'center', alignItems: 'center' },
  chipIconActive: { borderWidth: 3, borderColor: '#bbf7d0' },
  chipLabel: { fontSize: 11, color: '#6b7280', textAlign: 'center', fontWeight: '600', marginTop: 6 },
  chipLabelActive: { color: '#14532d' },

  // Sections / rails
  railsContent: { paddingBottom: 96 },
  section: { marginBottom: 22 },
  sectionHeader: { flexDirection: 'row', alignItems: 'center', gap: 8, marginBottom: 12 },
  sectionTitle: { flex: 1, fontSize: 16, fontWeight: '700', color: '#111827' },
  rail: { gap: 12, paddingRight: 8 },

  // Vertical product card
  productCard: {
    backgroundColor: '#fff', borderRadius: 12, padding: 10,
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
  },
  productImageWrap: { position: 'relative', marginBottom: 8 },
  productImage: { width: '100%', height: 120, borderRadius: 8, backgroundColor: '#f3f4f6' },
  imageMuted: { opacity: 0.5 },
  productInitial: { fontSize: 32, fontWeight: '700', color: '#9ca3af' },
  productControl: { position: 'absolute', top: 6, right: 6 },
  productName: { fontSize: 13, fontWeight: '600', color: '#111827', marginBottom: 4, minHeight: 34 },
  productPrice: { fontSize: 14, fontWeight: '700', color: '#14532d' },
  productStock: { fontSize: 11, color: '#6b7280', marginTop: 2 },

  // Card overlay controls
  cardAddBtn: {
    backgroundColor: '#16a34a', borderRadius: 10, width: 34, height: 34,
    justifyContent: 'center', alignItems: 'center',
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.2, shadowRadius: 2, elevation: 3,
  },
  cardQtyPill: {
    flexDirection: 'row', alignItems: 'center', backgroundColor: '#16a34a',
    borderRadius: 17, height: 34, paddingHorizontal: 4,
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.2, shadowRadius: 2, elevation: 3,
  },
  cardQtyBtn: { width: 26, height: 26, justifyContent: 'center', alignItems: 'center' },
  cardQtyCount: { color: '#fff', fontWeight: '700', fontSize: 14, minWidth: 18, textAlign: 'center' },
  notifyChip: {
    width: 34, height: 34, borderRadius: 10, backgroundColor: '#fff',
    justifyContent: 'center', alignItems: 'center', borderWidth: 1, borderColor: '#16a34a',
  },
  notifyChipDone: { backgroundColor: '#16a34a', borderColor: '#16a34a' },

  // Wide row card (search)
  itemCard: {
    backgroundColor: '#ffffff', borderRadius: 12, padding: 14,
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
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
  notifyButtonDone: { backgroundColor: '#f0fdf4' },
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
