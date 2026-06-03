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
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { getStoredToken } from '../services/authService';
import type { RootStackParamList } from '../../App';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

interface Branch {
  id: string;
  name: string;
  city: string;
}

interface InventoryItem {
  id: string;
  itemName: string;
  price: number;
  stockQuantity: number;
}

interface CartItem {
  itemId: string;
  itemName: string;
  price: number;
  quantity: number;
}

export default function ShoppingView() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const [branches, setBranches]                 = useState<Branch[]>([]);
  const [selectedBranch, setSelectedBranch]     = useState<Branch | null>(null);
  const [inventory, setInventory]               = useState<InventoryItem[]>([]);
  const [cart, setCart]                         = useState<CartItem[]>([]);
  const [cartVisible, setCartVisible]           = useState(false);
  const [loadingBranches, setLoadingBranches]   = useState(true);
  const [loadingInventory, setLoadingInventory] = useState(false);
  const [placing, setPlacing]                   = useState(false);
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
    setCart([]);
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
    setCart(prev => {
      const existing = prev.find(c => c.itemId === item.id);
      if (existing) {
        return prev.map(c =>
          c.itemId === item.id ? { ...c, quantity: c.quantity + 1 } : c,
        );
      }
      return [...prev, { itemId: item.id, itemName: item.itemName, price: item.price, quantity: 1 }];
    });
  }

  function updateQty(itemId: string, delta: number) {
    setCart(prev =>
      prev
        .map(c => c.itemId === itemId ? { ...c, quantity: c.quantity + delta } : c)
        .filter(c => c.quantity > 0),
    );
  }

  async function placeOrder() {
    if (!selectedBranch || cart.length === 0) return;
    setPlacing(true);
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/orders`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          branchId: selectedBranch.id,
          orderType: 'Pickup',
          items: cart.map(c => ({ itemId: c.itemId, quantity: c.quantity })),
        }),
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        Alert.alert('Order Failed', body.message ?? 'Something went wrong.');
        return;
      }

      const data = await res.json();
      const orderedItems = cart.map(c => ({ id: c.itemId, name: c.itemName }));
      setCart([]);
      setCartVisible(false);
      selectBranch(selectedBranch);
      Alert.alert(
        'Order Placed!',
        `Total: Rs ${data.totalAmount?.toFixed(2)}\nLoyalty points earned: ${data.loyaltyPointsEarned}`,
        [
          { text: 'Done', style: 'cancel' },
          {
            text: 'Write a Review',
            onPress: () => navigation.navigate('ReviewScreen', {
              orderId: data.id,
              items: orderedItems,
            }),
          },
        ],
      );
    } catch {
      Alert.alert('Error', 'Could not place order. Check your connection.');
    } finally {
      setPlacing(false);
    }
  }

  const cartTotal = cart.reduce((sum, c) => sum + c.price * c.quantity, 0);
  const cartCount = cart.reduce((sum, c) => sum + c.quantity, 0);

  if (loadingBranches) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color="#16a34a" />
      </View>
    );
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
            renderItem={({ item }) => (
              <TouchableOpacity
                style={styles.branchCard}
                onPress={() => selectBranch(item)}
                activeOpacity={0.8}
              >
                <View>
                  <Text style={styles.branchName}>{item.name}</Text>
                  <Text style={styles.branchCity}>{item.city}</Text>
                </View>
                <Text style={styles.chevron}>›</Text>
              </TouchableOpacity>
            )}
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
            <View style={styles.centered}>
              <ActivityIndicator size="large" color="#16a34a" />
            </View>
          ) : (
            <FlatList
              data={inventory}
              keyExtractor={item => item.id}
              contentContainerStyle={styles.list}
              ListEmptyComponent={
                <Text style={styles.emptyNote}>No items available at this branch.</Text>
              }
              renderItem={({ item }) => {
                const inCart = cart.find(c => c.itemId === item.id);
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
                        <TouchableOpacity style={styles.qtyBtn} onPress={() => updateQty(item.id, -1)}>
                          <Text style={styles.qtyBtnText}>−</Text>
                        </TouchableOpacity>
                        <Text style={styles.qtyCount}>{inCart.quantity}</Text>
                        <TouchableOpacity style={styles.qtyBtn} onPress={() => addToCart(item)}>
                          <Text style={styles.qtyBtnText}>+</Text>
                        </TouchableOpacity>
                      </View>
                    ) : (
                      <TouchableOpacity
                        style={styles.addButton}
                        onPress={() => addToCart(item)}
                        activeOpacity={0.8}
                      >
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
      {cartCount > 0 && (
        <TouchableOpacity style={styles.cartFab} onPress={() => setCartVisible(true)} activeOpacity={0.9}>
          <Text style={styles.cartFabText}>🛒  {cartCount} item{cartCount !== 1 ? 's' : ''}  ·  Rs {cartTotal.toFixed(2)}</Text>
          <Text style={styles.cartFabArrow}>›</Text>
        </TouchableOpacity>
      )}

      {/* Cart modal */}
      <Modal visible={cartVisible} animationType="slide" presentationStyle="pageSheet">
        <View style={styles.modalContainer}>
          <View style={styles.modalHeader}>
            <Text style={styles.modalTitle}>Your Cart</Text>
            <TouchableOpacity onPress={() => setCartVisible(false)}>
              <Text style={styles.modalClose}>✕</Text>
            </TouchableOpacity>
          </View>

          <FlatList
            data={cart}
            keyExtractor={item => item.itemId}
            contentContainerStyle={styles.cartList}
            ListEmptyComponent={<Text style={styles.emptyNote}>Cart is empty.</Text>}
            renderItem={({ item }) => (
              <View style={styles.cartItem}>
                <View style={styles.itemInfo}>
                  <Text style={styles.itemName}>{item.itemName}</Text>
                  <Text style={styles.itemMeta}>Rs {item.price.toFixed(2)} each</Text>
                </View>
                <View style={styles.qtyRow}>
                  <TouchableOpacity style={styles.qtyBtn} onPress={() => updateQty(item.itemId, -1)}>
                    <Text style={styles.qtyBtnText}>−</Text>
                  </TouchableOpacity>
                  <Text style={styles.qtyCount}>{item.quantity}</Text>
                  <TouchableOpacity style={styles.qtyBtn} onPress={() => updateQty(item.itemId, 1)}>
                    <Text style={styles.qtyBtnText}>+</Text>
                  </TouchableOpacity>
                </View>
                <Text style={styles.cartItemTotal}>
                  Rs {(item.price * item.quantity).toFixed(2)}
                </Text>
              </View>
            )}
          />

          <View style={styles.cartFooter}>
            <View style={styles.totalRow}>
              <Text style={styles.totalLabel}>Total</Text>
              <Text style={styles.totalValue}>Rs {cartTotal.toFixed(2)}</Text>
            </View>
            <TouchableOpacity
              style={[styles.placeOrderBtn, placing && styles.placeOrderBtnDisabled]}
              onPress={placeOrder}
              disabled={placing}
              activeOpacity={0.8}
            >
              {placing
                ? <ActivityIndicator color="#fff" />
                : <Text style={styles.placeOrderText}>Place Order (Pickup)</Text>}
            </TouchableOpacity>
          </View>
        </View>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  centered: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  heading: { fontSize: 22, fontWeight: '700', color: '#14532d', marginBottom: 4 },
  subheading: { fontSize: 13, color: '#6b7280', marginBottom: 20 },
  list: { gap: 12, paddingBottom: 100 },
  backRow: { marginBottom: 16 },
  backText: { color: '#16a34a', fontWeight: '600', fontSize: 14 },
  branchCard: {
    backgroundColor: '#ffffff', borderRadius: 12, padding: 18,
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06, shadowRadius: 6, elevation: 2,
  },
  branchName: { fontSize: 15, fontWeight: '600', color: '#14532d', marginBottom: 2 },
  branchCity: { fontSize: 13, color: '#6b7280' },
  chevron: { fontSize: 22, color: '#16a34a' },
  itemCard: {
    backgroundColor: '#ffffff', borderRadius: 12, padding: 16,
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06, shadowRadius: 6, elevation: 2,
  },
  itemInfo: { flex: 1, marginRight: 12 },
  itemName: { fontSize: 14, fontWeight: '600', color: '#111827', marginBottom: 3 },
  itemMeta: { fontSize: 12, color: '#6b7280' },
  addButton: {
    backgroundColor: '#16a34a', borderRadius: 8,
    paddingVertical: 8, paddingHorizontal: 14,
  },
  addButtonText: { color: '#ffffff', fontWeight: '600', fontSize: 13 },
  qtyRow: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  qtyBtn: {
    width: 30, height: 30, borderRadius: 15,
    backgroundColor: '#f0fdf4', justifyContent: 'center', alignItems: 'center',
    borderWidth: 1, borderColor: '#bbf7d0',
  },
  qtyBtnText: { fontSize: 16, fontWeight: '700', color: '#16a34a' },
  qtyCount: { fontSize: 15, fontWeight: '700', color: '#14532d', minWidth: 20, textAlign: 'center' },
  errorText: { color: '#dc2626', fontSize: 13, textAlign: 'center', marginBottom: 12 },
  emptyNote: { textAlign: 'center', color: '#9ca3af', marginTop: 40 },
  // FAB
  cartFab: {
    position: 'absolute', bottom: 0, left: 0, right: 0,
    backgroundColor: '#14532d', borderRadius: 14, marginHorizontal: 0,
    paddingVertical: 16, paddingHorizontal: 20,
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
  },
  cartFabText: { color: '#ffffff', fontWeight: '700', fontSize: 15 },
  cartFabArrow: { color: '#bbf7d0', fontSize: 20, fontWeight: '300' },
  // Modal
  modalContainer: { flex: 1, backgroundColor: '#f9fafb' },
  modalHeader: {
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    paddingHorizontal: 24, paddingTop: 24, paddingBottom: 16,
    borderBottomWidth: 1, borderBottomColor: '#f0fdf4',
  },
  modalTitle: { fontSize: 20, fontWeight: '700', color: '#14532d' },
  modalClose: { fontSize: 18, color: '#6b7280', fontWeight: '600' },
  cartList: { paddingHorizontal: 24, paddingTop: 16, gap: 12, paddingBottom: 16 },
  cartItem: {
    backgroundColor: '#ffffff', borderRadius: 12, padding: 16,
    flexDirection: 'row', alignItems: 'center', gap: 10,
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
  },
  cartItemTotal: { fontSize: 13, fontWeight: '700', color: '#14532d', minWidth: 70, textAlign: 'right' },
  cartFooter: {
    padding: 24, borderTopWidth: 1, borderTopColor: '#e5e7eb',
    backgroundColor: '#ffffff',
  },
  totalRow: { flexDirection: 'row', justifyContent: 'space-between', marginBottom: 16 },
  totalLabel: { fontSize: 16, fontWeight: '600', color: '#374151' },
  totalValue: { fontSize: 20, fontWeight: '700', color: '#14532d' },
  placeOrderBtn: {
    backgroundColor: '#16a34a', borderRadius: 12,
    paddingVertical: 16, alignItems: 'center',
  },
  placeOrderBtnDisabled: { backgroundColor: '#86efac' },
  placeOrderText: { color: '#ffffff', fontSize: 16, fontWeight: '700' },
});
