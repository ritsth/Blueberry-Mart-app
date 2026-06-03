import React, { useEffect, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  FlatList,
  Modal,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from 'react-native';
import { useNavigation } from '@react-navigation/native';
import { getStoredToken } from '../services/authService';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

const PALETTE = ['#4f46e5', '#0284c7', '#059669', '#d97706', '#7c3aed', '#db2777'];
function branchColor(name: string) {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = name.charCodeAt(i) + ((h << 5) - h);
  return PALETTE[Math.abs(h) % PALETTE.length];
}

interface Branch        { id: string; name: string; city: string; }
interface InventoryItem { id: string; itemName: string; price: number; stockQuantity: number; }
interface CartItem      { itemId: string; itemName: string; price: number; quantity: number; }
interface BranchCart    { branch: Branch; items: CartItem[]; }

interface SearchItem    { id: string; itemName: string; price: number; stockQuantity: number; }
interface SearchGroup   { branchId: string; branchName: string; branchCity: string; items: SearchItem[]; }

interface Address       { id: string; label: string; addressLine: string; city: string; phone: string | null; isDefault: boolean; }

type OrderMode = 'pickup' | 'delivery';
const DELIVERY_FEE = 100;

export default function ShoppingView() {
  const navigation = useNavigation<any>();

  const [branches, setBranches]                 = useState<Branch[]>([]);
  const [selectedBranch, setSelectedBranch]     = useState<Branch | null>(null);
  const [inventory, setInventory]               = useState<InventoryItem[]>([]);
  const [carts, setCarts]                       = useState<Record<string, BranchCart>>({});
  const [cartVisible, setCartVisible]           = useState(false);
  const [expandedCartId, setExpandedCartId]     = useState<string | null>(null);
  const [placingId, setPlacingId]               = useState<string | null>(null);
  const [loadingBranches, setLoadingBranches]   = useState(true);
  const [loadingInventory, setLoadingInventory] = useState(false);
  const [error, setError]                       = useState<string | null>(null);

  // Membership
  const [isMember, setIsMember]         = useState(false);
  const [discountRate, setDiscountRate] = useState(0);

  // Delivery
  const [addresses, setAddresses]             = useState<Address[]>([]);
  const [orderModes, setOrderModes]           = useState<Record<string, OrderMode>>({});
  const [selectedAddressId, setSelectedAddressId] = useState<Record<string, string>>({});

  // Search
  const [query, setQuery]               = useState('');
  const [searchResults, setSearchResults] = useState<SearchGroup[]>([]);
  const [searching, setSearching]       = useState(false);
  const debounceRef                     = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => { fetchBranches(); fetchMembership(); fetchAddresses(); }, []);

  async function fetchMembership() {
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/membership/status`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) return;
      const data = await res.json();
      setIsMember(data.isMember);
      setDiscountRate(data.discountRate ?? 0);
    } catch { /* non-blocking */ }
  }

  async function fetchAddresses() {
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/addresses`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) return;
      setAddresses(await res.json());
    } catch { /* non-blocking */ }
  }

  // Refresh addresses whenever the cart opens (user may have added one in Account)
  useEffect(() => { if (cartVisible) fetchAddresses(); }, [cartVisible]);

  function setMode(branchId: string, mode: OrderMode) {
    setOrderModes(prev => ({ ...prev, [branchId]: mode }));
    // Auto-select the default address when switching to delivery
    if (mode === 'delivery' && !selectedAddressId[branchId]) {
      const def = addresses.find(a => a.isDefault) ?? addresses[0];
      if (def) setSelectedAddressId(prev => ({ ...prev, [branchId]: def.id }));
    }
  }

  function selectAddress(branchId: string, addressId: string) {
    setSelectedAddressId(prev => ({ ...prev, [branchId]: addressId }));
  }

  // Debounced search
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
      const res = await fetch(`${API_BASE}/api/inventory/search?q=${encodeURIComponent(q)}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
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
      const res = await fetch(`${API_BASE}/api/branches`, {
        headers: { Authorization: `Bearer ${token}` },
      });
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
      const res = await fetch(
        `${API_BASE}/api/inventory/customer?branchId=${branch.id}`,
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

  function addToCart(item: { id: string; itemName: string; price: number }, branch: Branch) {
    setCarts(prev => {
      const existing = prev[branch.id]?.items ?? [];
      const found    = existing.find(c => c.itemId === item.id);
      const updated  = found
        ? existing.map(c => c.itemId === item.id ? { ...c, quantity: c.quantity + 1 } : c)
        : [...existing, { itemId: item.id, itemName: item.itemName, price: item.price, quantity: 1 }];
      return { ...prev, [branch.id]: { branch, items: updated } };
    });
  }

  function updateQty(branchId: string, itemId: string, delta: number) {
    setCarts(prev => {
      const bc = prev[branchId];
      if (!bc) return prev;
      const updated = bc.items
        .map(c => c.itemId === itemId ? { ...c, quantity: c.quantity + delta } : c)
        .filter(c => c.quantity > 0);
      if (updated.length === 0) { const { [branchId]: _, ...rest } = prev; return rest; }
      return { ...prev, [branchId]: { ...bc, items: updated } };
    });
  }

  async function placeOrder(branchId: string) {
    const bc = carts[branchId];
    if (!bc) return;

    const mode = orderModes[branchId] ?? 'pickup';
    const addressId = selectedAddressId[branchId];
    if (mode === 'delivery' && !addressId) {
      Alert.alert('Address required', 'Please select a delivery address.');
      return;
    }

    setPlacingId(branchId);
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/orders`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
        body: JSON.stringify({
          branchId,
          orderType: mode === 'delivery' ? 'Delivery' : 'Pickup',
          addressId: mode === 'delivery' ? addressId : null,
          items: bc.items.map(c => ({ itemId: c.itemId, quantity: c.quantity })),
        }),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        Alert.alert('Order Failed', body.message ?? 'Something went wrong.');
        return;
      }
      const data = await res.json();
      const orderedItems = bc.items.map(c => ({ id: c.itemId, name: c.itemName }));
      setCarts(prev => { const { [branchId]: _, ...rest } = prev; return rest; });
      if (Object.keys(carts).length <= 1) setCartVisible(false);
      if (selectedBranch?.id === branchId) selectBranch(selectedBranch);
      Alert.alert(
        'Order Placed!',
        `Total: Rs ${data.totalAmount?.toFixed(2)}\nLoyalty points earned: ${data.loyaltyPointsEarned}`,
        [
          { text: 'Done', style: 'cancel' },
          { text: 'Write a Review', onPress: () => navigation.navigate('ReviewScreen', { orderId: data.id, items: orderedItems }) },
        ],
      );
    } catch {
      Alert.alert('Error', 'Could not place order. Check your connection.');
    } finally {
      setPlacingId(null);
    }
  }

  const totalCartCount = Object.values(carts).reduce(
    (sum, bc) => sum + bc.items.reduce((s, i) => s + i.quantity, 0), 0,
  );

  const isSearching = query.trim().length >= 2;

  if (loadingBranches) {
    return <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>;
  }

  // ─── Inventory view (branch selected) ──────────────────────────────────────
  if (selectedBranch !== null) {
    return (
      <View style={styles.container}>
        <TouchableOpacity onPress={() => setSelectedBranch(null)} style={styles.backRow}>
          <Text style={styles.backText}>← All Branches</Text>
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
            ListEmptyComponent={<Text style={styles.emptyNote}>No items available at this branch.</Text>}
            renderItem={({ item }) => {
              const inCart = carts[selectedBranch.id]?.items.find(c => c.itemId === item.id);
              return (
                <View style={styles.itemCard}>
                  <View style={styles.itemInfo}>
                    <Text style={styles.itemName}>{item.itemName}</Text>
                    <Text style={styles.itemMeta}>Rs {item.price.toFixed(2)}{'  ·  '}{item.stockQuantity} in stock</Text>
                  </View>
                  {inCart ? (
                    <View style={styles.qtyRow}>
                      <TouchableOpacity style={styles.qtyBtn} onPress={() => updateQty(selectedBranch.id, item.id, -1)}>
                        <Text style={styles.qtyBtnText}>−</Text>
                      </TouchableOpacity>
                      <Text style={styles.qtyCount}>{inCart.quantity}</Text>
                      <TouchableOpacity style={styles.qtyBtn} onPress={() => addToCart(item, selectedBranch)}>
                        <Text style={styles.qtyBtnText}>+</Text>
                      </TouchableOpacity>
                    </View>
                  ) : (
                    <TouchableOpacity style={styles.addButton} onPress={() => addToCart(item, selectedBranch)} activeOpacity={0.8}>
                      <Text style={styles.addButtonText}>+ Add</Text>
                    </TouchableOpacity>
                  )}
                </View>
              );
            }}
          />
        )}
        {error && <Text style={styles.errorText}>{error}</Text>}
        {totalCartCount > 0 && <CartFab count={totalCartCount} cartCount={Object.keys(carts).length} onPress={() => setCartVisible(true)} />}
        <CartsModal
          carts={carts} visible={cartVisible} expandedCartId={expandedCartId}
          placingId={placingId} isMember={isMember} discountRate={discountRate}
          addresses={addresses} orderModes={orderModes} selectedAddressId={selectedAddressId}
          onSetMode={setMode} onSelectAddress={selectAddress}
          onClose={() => setCartVisible(false)}
          onToggle={id => setExpandedCartId(prev => prev === id ? null : id)}
          onUpdateQty={updateQty} onPlaceOrder={placeOrder}
        />
      </View>
    );
  }

  // ─── Branch list + search ───────────────────────────────────────────────────
  return (
    <View style={styles.container}>
      {/* Search bar */}
      <View style={styles.searchBar}>
        <Text style={styles.searchIcon}>🔍</Text>
        <TextInput
          style={styles.searchInput}
          placeholder="Search across all branches..."
          placeholderTextColor="#9ca3af"
          value={query}
          onChangeText={setQuery}
          returnKeyType="search"
          clearButtonMode="while-editing"
          autoCorrect={false}
        />
        {searching && <ActivityIndicator size="small" color="#9ca3af" style={{ marginRight: 8 }} />}
      </View>

      {/* Search results */}
      {isSearching ? (
        <FlatList
          data={searchResults}
          keyExtractor={g => g.branchId}
          contentContainerStyle={styles.list}
          ListEmptyComponent={
            !searching
              ? <Text style={styles.emptyNote}>No items found for "{query}"</Text>
              : null
          }
          renderItem={({ item: group }) => {
            const branch: Branch = { id: group.branchId, name: group.branchName, city: group.branchCity };
            const color = branchColor(group.branchName);
            return (
              <View style={styles.searchGroup}>
                {/* Branch header */}
                <View style={styles.searchGroupHeader}>
                  <View style={[styles.searchGroupLogo, { backgroundColor: color }]}>
                    <Text style={styles.branchLogoEmoji}>🫐</Text>
                  </View>
                  <View>
                    <Text style={styles.searchGroupName}>{group.branchName}</Text>
                    <Text style={styles.searchGroupCity}>{group.branchCity}</Text>
                  </View>
                </View>
                {/* Items */}
                {group.items.map(item => {
                  const inCart = carts[group.branchId]?.items.find(c => c.itemId === item.id);
                  return (
                    <View key={item.id} style={styles.itemCard}>
                      <View style={styles.itemInfo}>
                        <Text style={styles.itemName}>{item.itemName}</Text>
                        <Text style={styles.itemMeta}>Rs {item.price.toFixed(2)}{'  ·  '}{item.stockQuantity} in stock</Text>
                      </View>
                      {inCart ? (
                        <View style={styles.qtyRow}>
                          <TouchableOpacity style={styles.qtyBtn} onPress={() => updateQty(group.branchId, item.id, -1)}>
                            <Text style={styles.qtyBtnText}>−</Text>
                          </TouchableOpacity>
                          <Text style={styles.qtyCount}>{inCart.quantity}</Text>
                          <TouchableOpacity style={styles.qtyBtn} onPress={() => addToCart(item, branch)}>
                            <Text style={styles.qtyBtnText}>+</Text>
                          </TouchableOpacity>
                        </View>
                      ) : (
                        <TouchableOpacity style={styles.addButton} onPress={() => addToCart(item, branch)} activeOpacity={0.8}>
                          <Text style={styles.addButtonText}>+ Add</Text>
                        </TouchableOpacity>
                      )}
                    </View>
                  );
                })}
              </View>
            );
          }}
        />
      ) : (
        /* Branch list */
        <>
          <Text style={styles.heading}>Welcome to the Grocery Store</Text>
          <Text style={styles.subheading}>Select a branch to start shopping</Text>
          {error && <Text style={styles.errorText}>{error}</Text>}
          <FlatList
            data={branches}
            keyExtractor={item => item.id}
            contentContainerStyle={styles.list}
            renderItem={({ item }) => {
              const color = branchColor(item.name);
              const branchCart = carts[item.id];
              return (
                <TouchableOpacity style={styles.branchCard} onPress={() => selectBranch(item)} activeOpacity={0.8}>
                  <View style={[styles.branchLogo, { backgroundColor: color }]}>
                    <Text style={styles.branchLogoEmoji}>🫐</Text>
                    <Text style={styles.branchLogoInitial} numberOfLines={1}>
                      {item.name.replace('Blueberry Mart ', '')}
                    </Text>
                  </View>
                  <View style={styles.branchInfo}>
                    <Text style={styles.branchName}>{item.name}</Text>
                    <Text style={styles.branchCity}>{item.city}</Text>
                    {branchCart && (
                      <Text style={styles.branchCartHint}>
                        🛒 {branchCart.items.reduce((s, i) => s + i.quantity, 0)} items in cart
                      </Text>
                    )}
                  </View>
                  <Text style={styles.chevron}>›</Text>
                </TouchableOpacity>
              );
            }}
          />
        </>
      )}

      {totalCartCount > 0 && <CartFab count={totalCartCount} cartCount={Object.keys(carts).length} onPress={() => setCartVisible(true)} />}
      <CartsModal
        carts={carts} visible={cartVisible} expandedCartId={expandedCartId}
        placingId={placingId} isMember={isMember} discountRate={discountRate}
        addresses={addresses} orderModes={orderModes} selectedAddressId={selectedAddressId}
        onSetMode={setMode} onSelectAddress={selectAddress}
        onClose={() => setCartVisible(false)}
        onToggle={id => setExpandedCartId(prev => prev === id ? null : id)}
        onUpdateQty={updateQty} onPlaceOrder={placeOrder}
      />
    </View>
  );
}

// ─── Sub-components ─────────────────────────────────────────────────────────

function CartFab({ count, cartCount, onPress }: { count: number; cartCount: number; onPress: () => void }) {
  return (
    <TouchableOpacity style={styles.cartFab} onPress={onPress} activeOpacity={0.9}>
      <Text style={styles.cartFabText}>
        🛒  {count} item{count !== 1 ? 's' : ''} · {cartCount} branch{cartCount !== 1 ? 'es' : ''}
      </Text>
      <Text style={styles.cartFabArrow}>›</Text>
    </TouchableOpacity>
  );
}

function CartsModal({ carts, visible, expandedCartId, placingId, isMember, discountRate, addresses, orderModes, selectedAddressId, onSetMode, onSelectAddress, onClose, onToggle, onUpdateQty, onPlaceOrder }: {
  carts: Record<string, BranchCart>;
  visible: boolean;
  expandedCartId: string | null;
  placingId: string | null;
  isMember: boolean;
  discountRate: number;
  addresses: Address[];
  orderModes: Record<string, OrderMode>;
  selectedAddressId: Record<string, string>;
  onSetMode: (branchId: string, mode: OrderMode) => void;
  onSelectAddress: (branchId: string, addressId: string) => void;
  onClose: () => void;
  onToggle: (id: string) => void;
  onUpdateQty: (branchId: string, itemId: string, delta: number) => void;
  onPlaceOrder: (branchId: string) => void;
}) {
  return (
    <Modal visible={visible} animationType="slide" presentationStyle="pageSheet">
      <View style={styles.modalContainer}>
        <View style={styles.modalHeader}>
          <TouchableOpacity onPress={onClose}>
            <Text style={styles.modalClose}>✕</Text>
          </TouchableOpacity>
          <Text style={styles.modalTitle}>Carts</Text>
        </View>

        <FlatList
          data={Object.values(carts)}
          keyExtractor={bc => bc.branch.id}
          contentContainerStyle={styles.cartList}
          ListEmptyComponent={<Text style={styles.emptyNote}>Your carts are empty.</Text>}
          renderItem={({ item: bc }) => {
            const subtotal = bc.items.reduce((s, i) => s + i.price * i.quantity, 0);
            const discount = isMember ? Math.round(subtotal * discountRate * 100) / 100 : 0;
            const mode     = orderModes[bc.branch.id] ?? 'pickup';
            const deliveryFee = mode === 'delivery' ? (isMember ? 0 : DELIVERY_FEE) : 0;
            const total    = subtotal - discount + deliveryFee;
            const count    = bc.items.reduce((s, i) => s + i.quantity, 0);
            const color    = branchColor(bc.branch.name);
            const expanded = expandedCartId === bc.branch.id;
            const placing  = placingId === bc.branch.id;
            const chosenAddressId = selectedAddressId[bc.branch.id];
            return (
              <View style={styles.branchCartSection}>
                <TouchableOpacity style={styles.branchCartRow} onPress={() => onToggle(bc.branch.id)} activeOpacity={0.8}>
                  <View style={[styles.cartBranchLogo, { backgroundColor: color }]}>
                    <Text style={styles.cartBranchLogoEmoji}>🫐</Text>
                  </View>
                  <View style={styles.cartBranchInfo}>
                    <Text style={styles.cartBranchName}>{bc.branch.name}</Text>
                    <Text style={styles.cartBranchMeta}>{count} item{count !== 1 ? 's' : ''} · Rs {total.toFixed(2)}</Text>
                    <Text style={styles.cartBranchCity}>
                      {mode === 'delivery' ? 'Delivery' : `Pickup from ${bc.branch.city}`}
                    </Text>
                  </View>
                  <Text style={styles.cartChevron}>{expanded ? '▲' : '▼'}</Text>
                </TouchableOpacity>
                {expanded && (
                  <View style={styles.cartExpanded}>
                    {bc.items.map(item => (
                      <View key={item.itemId} style={styles.cartItemRow}>
                        <View style={styles.itemInfo}>
                          <Text style={styles.itemName}>{item.itemName}</Text>
                          <Text style={styles.itemMeta}>Rs {item.price.toFixed(2)} each</Text>
                        </View>
                        <View style={styles.qtyRow}>
                          <TouchableOpacity style={styles.qtyBtn} onPress={() => onUpdateQty(bc.branch.id, item.itemId, -1)}>
                            <Text style={styles.qtyBtnText}>−</Text>
                          </TouchableOpacity>
                          <Text style={styles.qtyCount}>{item.quantity}</Text>
                          <TouchableOpacity style={styles.qtyBtn} onPress={() => onUpdateQty(bc.branch.id, item.itemId, 1)}>
                            <Text style={styles.qtyBtnText}>+</Text>
                          </TouchableOpacity>
                        </View>
                        <Text style={styles.cartItemTotal}>Rs {(item.price * item.quantity).toFixed(2)}</Text>
                      </View>
                    ))}
                    {/* Pickup / Delivery toggle */}
                    <View style={styles.modeToggle}>
                      {(['pickup', 'delivery'] as OrderMode[]).map(m => (
                        <TouchableOpacity
                          key={m}
                          style={[styles.modeBtn, mode === m && styles.modeBtnActive]}
                          onPress={() => onSetMode(bc.branch.id, m)}
                          activeOpacity={0.8}
                        >
                          <Text style={[styles.modeBtnText, mode === m && styles.modeBtnTextActive]}>
                            {m === 'pickup' ? '🏬  Pickup' : '🛵  Delivery'}
                          </Text>
                        </TouchableOpacity>
                      ))}
                    </View>

                    {/* Address picker (delivery only) */}
                    {mode === 'delivery' && (
                      addresses.length === 0 ? (
                        <Text style={styles.noAddressNote}>
                          No saved address. Add one in Account → Delivery Addresses.
                        </Text>
                      ) : (
                        <View style={styles.addressPicker}>
                          {addresses.map(addr => (
                            <TouchableOpacity
                              key={addr.id}
                              style={[styles.addressOption, chosenAddressId === addr.id && styles.addressOptionSelected]}
                              onPress={() => onSelectAddress(bc.branch.id, addr.id)}
                              activeOpacity={0.8}
                            >
                              <View style={styles.radioOuter}>
                                {chosenAddressId === addr.id && <View style={styles.radioInner} />}
                              </View>
                              <View style={{ flex: 1 }}>
                                <Text style={styles.addressOptLabel}>{addr.label}</Text>
                                <Text style={styles.addressOptText}>{addr.addressLine}, {addr.city}</Text>
                              </View>
                            </TouchableOpacity>
                          ))}
                        </View>
                      )
                    )}

                    <View style={styles.cartFooter}>
                      {(discount > 0 || deliveryFee > 0 || mode === 'delivery') && (
                        <View style={styles.breakdownRow}>
                          <Text style={styles.breakdownLabel}>Subtotal</Text>
                          <Text style={styles.breakdownValue}>Rs {subtotal.toFixed(2)}</Text>
                        </View>
                      )}
                      {discount > 0 && (
                        <View style={styles.breakdownRow}>
                          <Text style={styles.discountLabel}>
                            🫐 Member discount ({Math.round(discountRate * 100)}%)
                          </Text>
                          <Text style={styles.discountValue}>− Rs {discount.toFixed(2)}</Text>
                        </View>
                      )}
                      {mode === 'delivery' && (
                        <View style={styles.breakdownRow}>
                          <Text style={styles.breakdownLabel}>Delivery fee</Text>
                          {deliveryFee === 0
                            ? <Text style={styles.freeText}>{isMember ? 'FREE (member)' : 'FREE'}</Text>
                            : <Text style={styles.breakdownValue}>Rs {deliveryFee.toFixed(2)}</Text>}
                        </View>
                      )}
                      <View style={styles.totalRow}>
                        <Text style={styles.totalLabel}>Total</Text>
                        <Text style={styles.totalValue}>Rs {total.toFixed(2)}</Text>
                      </View>
                      <TouchableOpacity
                        style={[styles.placeOrderBtn, placing && styles.placeOrderBtnDisabled]}
                        onPress={() => onPlaceOrder(bc.branch.id)}
                        disabled={placing}
                        activeOpacity={0.8}
                      >
                        {placing
                          ? <ActivityIndicator color="#fff" />
                          : <Text style={styles.placeOrderText}>
                              Place Order ({mode === 'delivery' ? 'Delivery' : 'Pickup'})
                            </Text>}
                      </TouchableOpacity>
                    </View>
                  </View>
                )}
              </View>
            );
          }}
        />
      </View>
    </Modal>
  );
}

// ─── Styles ──────────────────────────────────────────────────────────────────

const styles = StyleSheet.create({
  container:  { flex: 1 },
  centered:   { flex: 1, justifyContent: 'center', alignItems: 'center' },
  heading:    { fontSize: 22, fontWeight: '700', color: '#14532d', marginBottom: 4 },
  subheading: { fontSize: 13, color: '#6b7280', marginBottom: 16 },
  list:       { gap: 10, paddingBottom: 100 },
  backRow:    { marginBottom: 16 },
  backText:   { color: '#16a34a', fontWeight: '600', fontSize: 14 },
  errorText:  { color: '#dc2626', fontSize: 13, textAlign: 'center', marginBottom: 12 },
  emptyNote:  { textAlign: 'center', color: '#9ca3af', marginTop: 40 },

  // Search
  searchBar: {
    flexDirection: 'row', alignItems: 'center',
    backgroundColor: '#f3f4f6', borderRadius: 12,
    paddingHorizontal: 12, marginBottom: 20, height: 44,
  },
  searchIcon:  { fontSize: 15, marginRight: 6 },
  searchInput: { flex: 1, fontSize: 15, color: '#111827' },

  // Search results grouped by branch
  searchGroup: { marginBottom: 8 },
  searchGroupHeader: {
    flexDirection: 'row', alignItems: 'center',
    marginBottom: 10,
  },
  searchGroupLogo: {
    width: 40, height: 40, borderRadius: 8,
    justifyContent: 'center', alignItems: 'center', marginRight: 10,
  },
  searchGroupName: { fontSize: 15, fontWeight: '700', color: '#111827' },
  searchGroupCity: { fontSize: 12, color: '#6b7280' },

  // Branch card
  branchCard: {
    backgroundColor: '#ffffff', borderRadius: 14, padding: 14,
    flexDirection: 'row', alignItems: 'center',
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.07, shadowRadius: 6, elevation: 2,
  },
  branchLogo: {
    width: 64, height: 64, borderRadius: 12,
    justifyContent: 'center', alignItems: 'center',
    marginRight: 14, overflow: 'hidden',
  },
  branchLogoEmoji:   { fontSize: 22 },
  branchLogoInitial: { fontSize: 9, color: '#fff', fontWeight: '700', marginTop: 2, textAlign: 'center' },
  branchInfo:        { flex: 1 },
  branchName:        { fontSize: 15, fontWeight: '700', color: '#111827', marginBottom: 2 },
  branchCity:        { fontSize: 13, color: '#6b7280' },
  branchCartHint:    { fontSize: 11, color: '#16a34a', fontWeight: '600', marginTop: 3 },
  chevron:           { fontSize: 22, color: '#9ca3af' },

  // Item card
  itemCard: {
    backgroundColor: '#ffffff', borderRadius: 12, padding: 14,
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
    marginBottom: 8,
  },
  itemInfo:      { flex: 1, marginRight: 12 },
  itemName:      { fontSize: 14, fontWeight: '600', color: '#111827', marginBottom: 3 },
  itemMeta:      { fontSize: 12, color: '#6b7280' },
  addButton:     { backgroundColor: '#16a34a', borderRadius: 8, paddingVertical: 8, paddingHorizontal: 14 },
  addButtonText: { color: '#ffffff', fontWeight: '600', fontSize: 13 },
  qtyRow:        { flexDirection: 'row', alignItems: 'center', gap: 8 },
  qtyBtn:        {
    width: 30, height: 30, borderRadius: 15,
    backgroundColor: '#f0fdf4', justifyContent: 'center', alignItems: 'center',
    borderWidth: 1, borderColor: '#bbf7d0',
  },
  qtyBtnText: { fontSize: 16, fontWeight: '700', color: '#16a34a' },
  qtyCount:   { fontSize: 15, fontWeight: '700', color: '#14532d', minWidth: 20, textAlign: 'center' },

  // FAB
  cartFab: {
    position: 'absolute', bottom: 0, left: 0, right: 0,
    backgroundColor: '#14532d', borderRadius: 14,
    paddingVertical: 16, paddingHorizontal: 20,
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
  },
  cartFabText:  { color: '#ffffff', fontWeight: '700', fontSize: 14 },
  cartFabArrow: { color: '#bbf7d0', fontSize: 20 },

  // Carts modal
  modalContainer: { flex: 1, backgroundColor: '#ffffff' },
  modalHeader: {
    flexDirection: 'row', alignItems: 'center',
    paddingHorizontal: 24, paddingTop: 24, paddingBottom: 16,
    borderBottomWidth: 1, borderBottomColor: '#f3f4f6',
  },
  modalClose: { fontSize: 18, color: '#374151', fontWeight: '600', marginRight: 16 },
  modalTitle: { fontSize: 26, fontWeight: '800', color: '#111827' },
  cartList:   { paddingTop: 8, paddingBottom: 32 },
  branchCartSection: { borderBottomWidth: 1, borderBottomColor: '#f3f4f6' },
  branchCartRow: {
    flexDirection: 'row', alignItems: 'center',
    paddingHorizontal: 24, paddingVertical: 16,
  },
  cartBranchLogo: {
    width: 56, height: 56, borderRadius: 10,
    justifyContent: 'center', alignItems: 'center', marginRight: 16,
  },
  cartBranchLogoEmoji: { fontSize: 26 },
  cartBranchInfo:      { flex: 1 },
  cartBranchName:      { fontSize: 16, fontWeight: '700', color: '#111827', marginBottom: 2 },
  cartBranchMeta:      { fontSize: 13, color: '#374151', marginBottom: 1 },
  cartBranchCity:      { fontSize: 12, color: '#9ca3af' },
  cartChevron:         { fontSize: 13, color: '#9ca3af', marginLeft: 8 },
  cartExpanded:        { paddingHorizontal: 24, paddingBottom: 16, backgroundColor: '#f9fafb' },
  cartItemRow: {
    flexDirection: 'row', alignItems: 'center',
    backgroundColor: '#ffffff', borderRadius: 10, padding: 12, marginBottom: 8,
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.04, shadowRadius: 3, elevation: 1,
  },
  cartItemTotal: { fontSize: 13, fontWeight: '700', color: '#14532d', minWidth: 72, textAlign: 'right' },
  cartFooter:    { paddingTop: 12 },
  breakdownRow:   { flexDirection: 'row', justifyContent: 'space-between', marginBottom: 6 },
  breakdownLabel: { fontSize: 13, color: '#6b7280' },
  breakdownValue: { fontSize: 13, color: '#374151' },
  discountLabel:  { fontSize: 13, color: '#16a34a', fontWeight: '600' },
  discountValue:  { fontSize: 13, color: '#16a34a', fontWeight: '600' },
  freeText:       { fontSize: 13, color: '#16a34a', fontWeight: '700' },
  // Mode toggle
  modeToggle: {
    flexDirection: 'row', gap: 8, marginTop: 4, marginBottom: 12,
  },
  modeBtn: {
    flex: 1, paddingVertical: 10, borderRadius: 10, alignItems: 'center',
    backgroundColor: '#ffffff', borderWidth: 1.5, borderColor: '#e5e7eb',
  },
  modeBtnActive: { backgroundColor: '#f0fdf4', borderColor: '#16a34a' },
  modeBtnText: { fontSize: 13, fontWeight: '600', color: '#6b7280' },
  modeBtnTextActive: { color: '#14532d' },
  // Address picker
  noAddressNote: {
    fontSize: 12, color: '#dc2626', marginBottom: 12, lineHeight: 17,
  },
  addressPicker: { marginBottom: 12, gap: 8 },
  addressOption: {
    flexDirection: 'row', alignItems: 'center',
    backgroundColor: '#ffffff', borderRadius: 10, padding: 12,
    borderWidth: 1.5, borderColor: '#e5e7eb',
  },
  addressOptionSelected: { borderColor: '#16a34a', backgroundColor: '#f0fdf4' },
  radioOuter: {
    width: 20, height: 20, borderRadius: 10, borderWidth: 2, borderColor: '#16a34a',
    marginRight: 12, justifyContent: 'center', alignItems: 'center',
  },
  radioInner: { width: 10, height: 10, borderRadius: 5, backgroundColor: '#16a34a' },
  addressOptLabel: { fontSize: 13, fontWeight: '700', color: '#111827' },
  addressOptText: { fontSize: 12, color: '#6b7280', marginTop: 1 },
  totalRow:      { flexDirection: 'row', justifyContent: 'space-between', marginBottom: 12, marginTop: 4 },
  totalLabel:    { fontSize: 15, fontWeight: '600', color: '#374151' },
  totalValue:    { fontSize: 19, fontWeight: '700', color: '#14532d' },
  placeOrderBtn: { backgroundColor: '#16a34a', borderRadius: 12, paddingVertical: 14, alignItems: 'center' },
  placeOrderBtnDisabled: { backgroundColor: '#86efac' },
  placeOrderText: { color: '#ffffff', fontSize: 15, fontWeight: '700' },
});
