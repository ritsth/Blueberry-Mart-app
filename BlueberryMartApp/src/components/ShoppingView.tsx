import React, { useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  FlatList,
  Modal,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { useNavigation } from '@react-navigation/native';
import { getStoredToken } from '../services/authService';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

// Deterministic branch colour from name
const PALETTE = ['#4f46e5', '#0284c7', '#059669', '#d97706', '#7c3aed', '#db2777'];
function branchColor(name: string) {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = name.charCodeAt(i) + ((h << 5) - h);
  return PALETTE[Math.abs(h) % PALETTE.length];
}

interface Branch   { id: string; name: string; city: string; }
interface InventoryItem { id: string; itemName: string; price: number; stockQuantity: number; }
interface CartItem { itemId: string; itemName: string; price: number; quantity: number; }
interface BranchCart { branch: Branch; items: CartItem[]; }

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

  useEffect(() => { fetchBranches(); }, []);

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

  function addToCart(item: InventoryItem) {
    if (!selectedBranch) return;
    setCarts(prev => {
      const existing = prev[selectedBranch.id]?.items ?? [];
      const found    = existing.find(c => c.itemId === item.id);
      const updated  = found
        ? existing.map(c => c.itemId === item.id ? { ...c, quantity: c.quantity + 1 } : c)
        : [...existing, { itemId: item.id, itemName: item.itemName, price: item.price, quantity: 1 }];
      return { ...prev, [selectedBranch.id]: { branch: selectedBranch, items: updated } };
    });
  }

  function updateQty(branchId: string, itemId: string, delta: number) {
    setCarts(prev => {
      const bc = prev[branchId];
      if (!bc) return prev;
      const updated = bc.items
        .map(c => c.itemId === itemId ? { ...c, quantity: c.quantity + delta } : c)
        .filter(c => c.quantity > 0);
      if (updated.length === 0) {
        const { [branchId]: _, ...rest } = prev;
        return rest;
      }
      return { ...prev, [branchId]: { ...bc, items: updated } };
    });
  }

  async function placeOrder(branchId: string) {
    const bc = carts[branchId];
    if (!bc) return;
    setPlacingId(branchId);
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/orders`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
        body: JSON.stringify({
          branchId,
          orderType: 'Pickup',
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

      // Remove this branch's cart
      setCarts(prev => { const { [branchId]: _, ...rest } = prev; return rest; });
      if (Object.keys(carts).length <= 1) setCartVisible(false);

      // Refresh inventory if still on that branch
      if (selectedBranch?.id === branchId) selectBranch(selectedBranch);

      Alert.alert(
        'Order Placed!',
        `Total: Rs ${data.totalAmount?.toFixed(2)}\nLoyalty points earned: ${data.loyaltyPointsEarned}`,
        [
          { text: 'Done', style: 'cancel' },
          {
            text: 'Write a Review',
            onPress: () => navigation.navigate('ReviewScreen', { orderId: data.id, items: orderedItems }),
          },
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

  if (loadingBranches) {
    return <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>;
  }

  return (
    <View style={styles.container}>

      {selectedBranch === null ? (
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
                <TouchableOpacity
                  style={styles.branchCard}
                  onPress={() => selectBranch(item)}
                  activeOpacity={0.8}
                >
                  {/* Branch logo */}
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
      ) : (
        <>
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
                      <Text style={styles.itemMeta}>
                        Rs {item.price.toFixed(2)}{'  ·  '}{item.stockQuantity} in stock
                      </Text>
                    </View>
                    {inCart ? (
                      <View style={styles.qtyRow}>
                        <TouchableOpacity style={styles.qtyBtn} onPress={() => updateQty(selectedBranch.id, item.id, -1)}>
                          <Text style={styles.qtyBtnText}>−</Text>
                        </TouchableOpacity>
                        <Text style={styles.qtyCount}>{inCart.quantity}</Text>
                        <TouchableOpacity style={styles.qtyBtn} onPress={() => addToCart(item)}>
                          <Text style={styles.qtyBtnText}>+</Text>
                        </TouchableOpacity>
                      </View>
                    ) : (
                      <TouchableOpacity style={styles.addButton} onPress={() => addToCart(item)} activeOpacity={0.8}>
                        <Text style={styles.addButtonText}>+ Add</Text>
                      </TouchableOpacity>
                    )}
                  </View>
                );
              }}
            />
          )}
          {error && <Text style={styles.errorText}>{error}</Text>}
        </>
      )}

      {/* Floating cart button */}
      {totalCartCount > 0 && (
        <TouchableOpacity style={styles.cartFab} onPress={() => setCartVisible(true)} activeOpacity={0.9}>
          <Text style={styles.cartFabText}>
            🛒  {totalCartCount} item{totalCartCount !== 1 ? 's' : ''} across {Object.keys(carts).length} branch{Object.keys(carts).length !== 1 ? 'es' : ''}
          </Text>
          <Text style={styles.cartFabArrow}>›</Text>
        </TouchableOpacity>
      )}

      {/* Multi-branch Carts modal */}
      <Modal visible={cartVisible} animationType="slide" presentationStyle="pageSheet">
        <View style={styles.modalContainer}>
          <View style={styles.modalHeader}>
            <TouchableOpacity onPress={() => setCartVisible(false)}>
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
              const total     = bc.items.reduce((s, i) => s + i.price * i.quantity, 0);
              const count     = bc.items.reduce((s, i) => s + i.quantity, 0);
              const color     = branchColor(bc.branch.name);
              const expanded  = expandedCartId === bc.branch.id;
              const placing   = placingId === bc.branch.id;

              return (
                <View style={styles.branchCartSection}>
                  {/* Branch row — tap to expand */}
                  <TouchableOpacity
                    style={styles.branchCartRow}
                    onPress={() => setExpandedCartId(expanded ? null : bc.branch.id)}
                    activeOpacity={0.8}
                  >
                    <View style={[styles.cartBranchLogo, { backgroundColor: color }]}>
                      <Text style={styles.cartBranchLogoEmoji}>🫐</Text>
                    </View>
                    <View style={styles.cartBranchInfo}>
                      <Text style={styles.cartBranchName}>{bc.branch.name}</Text>
                      <Text style={styles.cartBranchMeta}>
                        {count} item{count !== 1 ? 's' : ''} · Rs {total.toFixed(2)}
                      </Text>
                      <Text style={styles.cartBranchCity}>Pickup from {bc.branch.city}</Text>
                    </View>
                    <Text style={styles.cartChevron}>{expanded ? '▲' : '▼'}</Text>
                  </TouchableOpacity>

                  {/* Expanded: items + place order */}
                  {expanded && (
                    <View style={styles.cartExpanded}>
                      {bc.items.map(item => (
                        <View key={item.itemId} style={styles.cartItemRow}>
                          <View style={styles.itemInfo}>
                            <Text style={styles.itemName}>{item.itemName}</Text>
                            <Text style={styles.itemMeta}>Rs {item.price.toFixed(2)} each</Text>
                          </View>
                          <View style={styles.qtyRow}>
                            <TouchableOpacity style={styles.qtyBtn} onPress={() => updateQty(bc.branch.id, item.itemId, -1)}>
                              <Text style={styles.qtyBtnText}>−</Text>
                            </TouchableOpacity>
                            <Text style={styles.qtyCount}>{item.quantity}</Text>
                            <TouchableOpacity
                              style={styles.qtyBtn}
                              onPress={() => updateQty(bc.branch.id, item.itemId, 1)}
                            >
                              <Text style={styles.qtyBtnText}>+</Text>
                            </TouchableOpacity>
                          </View>
                          <Text style={styles.cartItemTotal}>
                            Rs {(item.price * item.quantity).toFixed(2)}
                          </Text>
                        </View>
                      ))}

                      <View style={styles.cartFooter}>
                        <View style={styles.totalRow}>
                          <Text style={styles.totalLabel}>Total</Text>
                          <Text style={styles.totalValue}>Rs {total.toFixed(2)}</Text>
                        </View>
                        <TouchableOpacity
                          style={[styles.placeOrderBtn, placing && styles.placeOrderBtnDisabled]}
                          onPress={() => placeOrder(bc.branch.id)}
                          disabled={placing}
                          activeOpacity={0.8}
                        >
                          {placing
                            ? <ActivityIndicator color="#fff" />
                            : <Text style={styles.placeOrderText}>Place Order (Pickup)</Text>}
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
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  centered:  { flex: 1, justifyContent: 'center', alignItems: 'center' },
  heading:   { fontSize: 22, fontWeight: '700', color: '#14532d', marginBottom: 4 },
  subheading:{ fontSize: 13, color: '#6b7280', marginBottom: 20 },
  list:      { gap: 12, paddingBottom: 100 },
  backRow:   { marginBottom: 16 },
  backText:  { color: '#16a34a', fontWeight: '600', fontSize: 14 },
  errorText: { color: '#dc2626', fontSize: 13, textAlign: 'center', marginBottom: 12 },
  emptyNote: { textAlign: 'center', color: '#9ca3af', marginTop: 40 },

  // Branch card with logo
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

  // Inventory item
  itemCard: {
    backgroundColor: '#ffffff', borderRadius: 12, padding: 16,
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06, shadowRadius: 6, elevation: 2,
  },
  itemInfo: { flex: 1, marginRight: 12 },
  itemName: { fontSize: 14, fontWeight: '600', color: '#111827', marginBottom: 3 },
  itemMeta: { fontSize: 12, color: '#6b7280' },
  addButton: { backgroundColor: '#16a34a', borderRadius: 8, paddingVertical: 8, paddingHorizontal: 14 },
  addButtonText: { color: '#ffffff', fontWeight: '600', fontSize: 13 },
  qtyRow:  { flexDirection: 'row', alignItems: 'center', gap: 8 },
  qtyBtn:  {
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

  branchCartSection: {
    borderBottomWidth: 1, borderBottomColor: '#f3f4f6',
  },
  branchCartRow: {
    flexDirection: 'row', alignItems: 'center',
    paddingHorizontal: 24, paddingVertical: 16,
  },
  cartBranchLogo: {
    width: 56, height: 56, borderRadius: 10,
    justifyContent: 'center', alignItems: 'center',
    marginRight: 16,
  },
  cartBranchLogoEmoji: { fontSize: 26 },
  cartBranchInfo:      { flex: 1 },
  cartBranchName:      { fontSize: 16, fontWeight: '700', color: '#111827', marginBottom: 2 },
  cartBranchMeta:      { fontSize: 13, color: '#374151', marginBottom: 1 },
  cartBranchCity:      { fontSize: 12, color: '#9ca3af' },
  cartChevron:         { fontSize: 13, color: '#9ca3af', marginLeft: 8 },

  cartExpanded: {
    paddingHorizontal: 24, paddingBottom: 16,
    backgroundColor: '#f9fafb',
  },
  cartItemRow: {
    flexDirection: 'row', alignItems: 'center',
    backgroundColor: '#ffffff', borderRadius: 10,
    padding: 12, marginBottom: 8,
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.04, shadowRadius: 3, elevation: 1,
  },
  cartItemTotal: { fontSize: 13, fontWeight: '700', color: '#14532d', minWidth: 72, textAlign: 'right' },

  cartFooter:   { paddingTop: 12 },
  totalRow:     { flexDirection: 'row', justifyContent: 'space-between', marginBottom: 12 },
  totalLabel:   { fontSize: 15, fontWeight: '600', color: '#374151' },
  totalValue:   { fontSize: 19, fontWeight: '700', color: '#14532d' },
  placeOrderBtn: {
    backgroundColor: '#16a34a', borderRadius: 12,
    paddingVertical: 14, alignItems: 'center',
  },
  placeOrderBtnDisabled: { backgroundColor: '#86efac' },
  placeOrderText: { color: '#ffffff', fontSize: 15, fontWeight: '700' },
});
