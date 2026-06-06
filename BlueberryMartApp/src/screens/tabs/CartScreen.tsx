import React, { useCallback, useState } from 'react';
import {
  ActivityIndicator, Alert, FlatList, StyleSheet, Text, TouchableOpacity, View,
} from 'react-native';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { getStoredToken } from '../../services/authService';
import { useCart } from '../../context/CartContext';
import EsewaCheckout, { PaymentOutcome } from '../../components/EsewaCheckout';

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';
const DELIVERY_FEE = 100;

type OrderMode = 'pickup' | 'delivery';
interface Address { id: string; label: string; addressLine: string; city: string; isDefault: boolean; }

const PALETTE = ['#4f46e5', '#0284c7', '#059669', '#d97706', '#7c3aed', '#db2777'];
function branchColor(name: string) {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = name.charCodeAt(i) + ((h << 5) - h);
  return PALETTE[Math.abs(h) % PALETTE.length];
}

export default function CartScreen() {
  const insets = useSafeAreaInsets();
  const navigation = useNavigation<any>();
  const { carts, updateQty, clearBranch } = useCart();

  const [isMember, setIsMember] = useState(false);
  const [discountRate, setDiscountRate] = useState(0);
  const [addresses, setAddresses] = useState<Address[]>([]);
  const [orderModes, setOrderModes] = useState<Record<string, OrderMode>>({});
  const [selectedAddressId, setSelectedAddressId] = useState<Record<string, string>>({});
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [placingId, setPlacingId] = useState<string | null>(null);
  const [payOrder, setPayOrder] = useState<{ id: string; orderNumber: number; total: number; mode: OrderMode; items: { id: string; name: string }[] } | null>(null);

  useFocusEffect(useCallback(() => { fetchMembership(); fetchAddresses(); }, []));

  async function fetchMembership() {
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/membership/status`, { headers: { Authorization: `Bearer ${token}` } });
      if (!res.ok) return;
      const data = await res.json();
      setIsMember(data.isMember);
      setDiscountRate(data.discountRate ?? 0);
    } catch { /* non-blocking */ }
  }

  async function fetchAddresses() {
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/addresses`, { headers: { Authorization: `Bearer ${token}` } });
      if (!res.ok) return;
      setAddresses(await res.json());
    } catch { /* non-blocking */ }
  }

  function setMode(branchId: string, mode: OrderMode) {
    setOrderModes(prev => ({ ...prev, [branchId]: mode }));
    if (mode === 'delivery' && !selectedAddressId[branchId]) {
      const def = addresses.find(a => a.isDefault) ?? addresses[0];
      if (def) setSelectedAddressId(prev => ({ ...prev, [branchId]: def.id }));
    }
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
      clearBranch(branchId);
      setPayOrder({ id: data.id, orderNumber: data.orderNumber, total: data.totalAmount, mode, items: orderedItems });
    } catch {
      Alert.alert('Error', 'Could not place order. Check your connection.');
    } finally {
      setPlacingId(null);
    }
  }

  async function onPaymentClose(outcome: PaymentOutcome) {
    const order = payOrder;
    setPayOrder(null);
    if (!order) return;

    let paid = outcome === 'success';
    if (!paid) {
      try {
        const token = await getStoredToken();
        const res = await fetch(`${API_BASE}/api/orders/${order.id}`, { headers: { Authorization: `Bearer ${token}` } });
        if (res.ok) { const data = await res.json(); paid = data?.payment?.status === 'completed'; }
      } catch { /* fall back to reported outcome */ }
    }

    if (paid) {
      const fulfilment = order.mode === 'delivery'
        ? `\nDelivery — track with order #${order.orderNumber}.`
        : `\nShow order #${order.orderNumber} at the counter to collect.`;
      Alert.alert(
        `Payment successful — Order #${order.orderNumber} confirmed!`,
        `Paid Rs ${order.total?.toFixed(2)}${fulfilment}`,
        [
          { text: 'Done', style: 'cancel' },
          { text: 'Write a Review', onPress: () => navigation.navigate('ReviewScreen', { orderId: order.id, items: order.items }) },
        ],
      );
    } else {
      Alert.alert(
        `Order #${order.orderNumber} not paid`,
        outcome === 'cancelled'
          ? 'Payment was cancelled. Your order is saved as pending.'
          : 'Payment did not complete. Your order is saved as pending.',
      );
    }
  }

  const branchCarts = Object.values(carts);

  return (
    <View style={[styles.container, { paddingTop: insets.top + 16 }]}>
      <Text style={styles.heading}>Cart</Text>

      {branchCarts.length === 0 ? (
        <View style={styles.empty}>
          <Ionicons name="cart-outline" size={48} color="#d1d5db" />
          <Text style={styles.emptyText}>Your cart is empty.</Text>
          <Text style={styles.emptySub}>Add items from the Shop or Bulk tab.</Text>
        </View>
      ) : (
        <FlatList
          data={branchCarts}
          keyExtractor={bc => bc.branch.id}
          contentContainerStyle={styles.list}
          renderItem={({ item: bc }) => {
            const subtotal = bc.items.reduce((s, i) => s + i.price * i.quantity, 0);
            const discount = isMember ? Math.round(subtotal * discountRate * 100) / 100 : 0;
            const mode = orderModes[bc.branch.id] ?? 'pickup';
            const deliveryFee = mode === 'delivery' ? (isMember ? 0 : DELIVERY_FEE) : 0;
            const total = subtotal - discount + deliveryFee;
            const count = bc.items.reduce((s, i) => s + i.quantity, 0);
            const color = branchColor(bc.branch.name);
            const expanded = expandedId === bc.branch.id;
            const placing = placingId === bc.branch.id;
            const chosenAddressId = selectedAddressId[bc.branch.id];
            return (
              <View style={styles.section}>
                <TouchableOpacity style={styles.branchRow} onPress={() => setExpandedId(prev => prev === bc.branch.id ? null : bc.branch.id)} activeOpacity={0.8}>
                  <View style={[styles.branchLogo, { backgroundColor: color }]}><Ionicons name="storefront" size={22} color="#fff" /></View>
                  <View style={{ flex: 1 }}>
                    <Text style={styles.branchName}>{bc.branch.name}</Text>
                    <Text style={styles.branchMeta}>{count} item{count !== 1 ? 's' : ''} · Rs {total.toFixed(2)}</Text>
                    <Text style={styles.branchCity}>{mode === 'delivery' ? 'Delivery' : `Pickup from ${bc.branch.city}`}</Text>
                  </View>
                  <Ionicons name={expanded ? 'chevron-up' : 'chevron-down'} size={18} color="#9ca3af" />
                </TouchableOpacity>

                {expanded && (
                  <View style={styles.expanded}>
                    {bc.items.map(item => (
                      <View key={item.itemId} style={styles.itemRow}>
                        <View style={{ flex: 1 }}>
                          <Text style={styles.itemName}>{item.itemName}</Text>
                          <Text style={styles.itemMeta}>Rs {item.price.toFixed(2)} each</Text>
                        </View>
                        <View style={styles.qtyRow}>
                          <TouchableOpacity style={styles.qtyBtn} onPress={() => updateQty(bc.branch.id, item.itemId, -1)}><Ionicons name="remove" size={15} color="#16a34a" /></TouchableOpacity>
                          <Text style={styles.qtyCount}>{item.quantity}</Text>
                          <TouchableOpacity style={styles.qtyBtn} onPress={() => updateQty(bc.branch.id, item.itemId, 1)}><Ionicons name="add" size={15} color="#16a34a" /></TouchableOpacity>
                        </View>
                        <Text style={styles.itemTotal}>Rs {(item.price * item.quantity).toFixed(2)}</Text>
                      </View>
                    ))}

                    <View style={styles.modeToggle}>
                      {(['pickup', 'delivery'] as OrderMode[]).map(m => (
                        <TouchableOpacity key={m} style={[styles.modeBtn, mode === m && styles.modeBtnActive]} onPress={() => setMode(bc.branch.id, m)} activeOpacity={0.8}>
                          <Ionicons name={m === 'pickup' ? 'storefront-outline' : 'bicycle-outline'} size={16} color={mode === m ? '#14532d' : '#6b7280'} />
                          <Text style={[styles.modeBtnText, mode === m && styles.modeBtnTextActive]}>{m === 'pickup' ? 'Pickup' : 'Delivery'}</Text>
                        </TouchableOpacity>
                      ))}
                    </View>

                    {mode === 'delivery' && (
                      addresses.length === 0 ? (
                        <Text style={styles.noAddress}>No saved address. Add one in Account → Delivery addresses.</Text>
                      ) : (
                        <View style={styles.addressPicker}>
                          {addresses.map(addr => (
                            <TouchableOpacity key={addr.id} style={[styles.addressOption, chosenAddressId === addr.id && styles.addressSelected]} onPress={() => setSelectedAddressId(prev => ({ ...prev, [bc.branch.id]: addr.id }))} activeOpacity={0.8}>
                              <View style={styles.radioOuter}>{chosenAddressId === addr.id && <View style={styles.radioInner} />}</View>
                              <View style={{ flex: 1 }}>
                                <Text style={styles.addressLabel}>{addr.label}</Text>
                                <Text style={styles.addressText}>{addr.addressLine}, {addr.city}</Text>
                              </View>
                            </TouchableOpacity>
                          ))}
                        </View>
                      )
                    )}

                    <View style={styles.footer}>
                      <View style={styles.breakdownRow}><Text style={styles.breakdownLabel}>Subtotal</Text><Text style={styles.breakdownValue}>Rs {subtotal.toFixed(2)}</Text></View>
                      {discount > 0 && (
                        <View style={styles.breakdownRow}><Text style={styles.discountLabel}>Member discount ({Math.round(discountRate * 100)}%)</Text><Text style={styles.discountValue}>− Rs {discount.toFixed(2)}</Text></View>
                      )}
                      {mode === 'delivery' && (
                        <View style={styles.breakdownRow}>
                          <Text style={styles.breakdownLabel}>Delivery fee</Text>
                          {deliveryFee === 0 ? <Text style={styles.freeText}>{isMember ? 'FREE (member)' : 'FREE'}</Text> : <Text style={styles.breakdownValue}>Rs {deliveryFee.toFixed(2)}</Text>}
                        </View>
                      )}
                      <View style={styles.totalRow}><Text style={styles.totalLabel}>Total</Text><Text style={styles.totalValue}>Rs {total.toFixed(2)}</Text></View>
                      <TouchableOpacity style={[styles.placeBtn, placing && styles.placeBtnDisabled]} onPress={() => placeOrder(bc.branch.id)} disabled={placing} activeOpacity={0.85}>
                        {placing ? <ActivityIndicator color="#fff" /> : <Text style={styles.placeText}>Place order · {mode === 'delivery' ? 'Delivery' : 'Pickup'}</Text>}
                      </TouchableOpacity>
                    </View>
                  </View>
                )}
              </View>
            );
          }}
        />
      )}

      <EsewaCheckout orderId={payOrder?.id ?? null} onClose={onPaymentClose} />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f9fafb', paddingHorizontal: 24 },
  heading: { fontSize: 26, fontWeight: '700', color: '#111827', marginBottom: 16 },
  list: { paddingBottom: 32 },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', gap: 8 },
  emptyText: { fontSize: 15, fontWeight: '600', color: '#6b7280', marginTop: 8 },
  emptySub: { fontSize: 13, color: '#9ca3af' },

  section: { backgroundColor: '#ffffff', borderRadius: 14, marginBottom: 12, overflow: 'hidden', shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.05, shadowRadius: 5, elevation: 1 },
  branchRow: { flexDirection: 'row', alignItems: 'center', padding: 14 },
  branchLogo: { width: 48, height: 48, borderRadius: 10, justifyContent: 'center', alignItems: 'center', marginRight: 14 },
  branchName: { fontSize: 15, fontWeight: '700', color: '#111827', marginBottom: 2 },
  branchMeta: { fontSize: 13, color: '#374151', marginBottom: 1 },
  branchCity: { fontSize: 12, color: '#9ca3af' },
  expanded: { paddingHorizontal: 14, paddingBottom: 14, backgroundColor: '#f9fafb' },
  itemRow: { flexDirection: 'row', alignItems: 'center', backgroundColor: '#fff', borderRadius: 10, padding: 12, marginBottom: 8, gap: 8 },
  itemName: { fontSize: 14, fontWeight: '600', color: '#111827', marginBottom: 2 },
  itemMeta: { fontSize: 12, color: '#6b7280' },
  itemTotal: { fontSize: 13, fontWeight: '700', color: '#14532d', minWidth: 70, textAlign: 'right' },
  qtyRow: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  qtyBtn: { width: 30, height: 30, borderRadius: 15, backgroundColor: '#f0fdf4', justifyContent: 'center', alignItems: 'center', borderWidth: 1, borderColor: '#bbf7d0' },
  qtyCount: { fontSize: 14, fontWeight: '700', color: '#14532d', minWidth: 18, textAlign: 'center' },

  modeToggle: { flexDirection: 'row', gap: 8, marginTop: 4, marginBottom: 12 },
  modeBtn: { flex: 1, flexDirection: 'row', justifyContent: 'center', alignItems: 'center', gap: 6, paddingVertical: 10, borderRadius: 10, backgroundColor: '#fff', borderWidth: 1.5, borderColor: '#e5e7eb' },
  modeBtnActive: { backgroundColor: '#f0fdf4', borderColor: '#16a34a' },
  modeBtnText: { fontSize: 13, fontWeight: '600', color: '#6b7280' },
  modeBtnTextActive: { color: '#14532d' },

  noAddress: { fontSize: 12, color: '#dc2626', marginBottom: 12, lineHeight: 17 },
  addressPicker: { marginBottom: 12, gap: 8 },
  addressOption: { flexDirection: 'row', alignItems: 'center', backgroundColor: '#fff', borderRadius: 10, padding: 12, borderWidth: 1.5, borderColor: '#e5e7eb' },
  addressSelected: { borderColor: '#16a34a', backgroundColor: '#f0fdf4' },
  radioOuter: { width: 20, height: 20, borderRadius: 10, borderWidth: 2, borderColor: '#16a34a', marginRight: 12, justifyContent: 'center', alignItems: 'center' },
  radioInner: { width: 10, height: 10, borderRadius: 5, backgroundColor: '#16a34a' },
  addressLabel: { fontSize: 13, fontWeight: '700', color: '#111827' },
  addressText: { fontSize: 12, color: '#6b7280', marginTop: 1 },

  footer: { paddingTop: 8 },
  breakdownRow: { flexDirection: 'row', justifyContent: 'space-between', marginBottom: 6 },
  breakdownLabel: { fontSize: 13, color: '#6b7280' },
  breakdownValue: { fontSize: 13, color: '#374151' },
  discountLabel: { fontSize: 13, color: '#16a34a', fontWeight: '600' },
  discountValue: { fontSize: 13, color: '#16a34a', fontWeight: '600' },
  freeText: { fontSize: 13, color: '#16a34a', fontWeight: '700' },
  totalRow: { flexDirection: 'row', justifyContent: 'space-between', marginBottom: 12, marginTop: 4 },
  totalLabel: { fontSize: 15, fontWeight: '600', color: '#374151' },
  totalValue: { fontSize: 19, fontWeight: '700', color: '#14532d' },
  placeBtn: { backgroundColor: '#16a34a', borderRadius: 12, paddingVertical: 14, alignItems: 'center' },
  placeBtnDisabled: { backgroundColor: '#86efac' },
  placeText: { color: '#fff', fontSize: 15, fontWeight: '700' },
});
