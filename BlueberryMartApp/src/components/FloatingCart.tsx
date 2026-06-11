import React from 'react';
import { StyleSheet, Text, TouchableOpacity, View } from 'react-native';
import { useNavigation } from '@react-navigation/native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { useCart } from '../context/CartContext';

/**
 * A floating green cart button pinned to the bottom of the screen. Used on screens that sit
 * outside the tab navigator (e.g. a branch's inventory) where the tab bar's cart button is
 * hidden. Tapping it jumps to the Cart tab of whichever tabs navigator opened this screen.
 * Hidden while the cart is empty.
 */
export default function FloatingCart() {
  const navigation = useNavigation<any>();
  const insets = useSafeAreaInsets();
  const { totalCount } = useCart();

  if (totalCount <= 0) return null;

  function openCart() {
    // This screen lives directly in the root stack, so its state lists the tabs navigator
    // (Customer or Shareholder) sitting underneath — route the cart tap to the right one.
    const root = navigation.getState?.();
    const tabs = root?.routes?.find(
      (r: { name: string }) => r.name === 'CustomerTabs' || r.name === 'ShareholderTabs',
    );
    navigation.navigate(tabs?.name ?? 'CustomerTabs', { screen: 'Cart' });
  }

  return (
    <TouchableOpacity
      style={[styles.fab, { bottom: insets.bottom + 16 }]}
      onPress={openCart}
      activeOpacity={0.85}
    >
      <Ionicons name="cart" size={24} color="#ffffff" />
      <View style={styles.badge}>
        <Text style={styles.badgeText}>{totalCount > 99 ? '99+' : totalCount}</Text>
      </View>
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  fab: {
    position: 'absolute',
    alignSelf: 'center',
    width: 60,
    height: 60,
    borderRadius: 30,
    backgroundColor: '#16a34a',
    justifyContent: 'center',
    alignItems: 'center',
    shadowColor: '#16a34a',
    shadowOffset: { width: 0, height: 3 },
    shadowOpacity: 0.4,
    shadowRadius: 8,
    elevation: 6,
  },
  badge: {
    position: 'absolute',
    top: -2,
    right: -2,
    minWidth: 22,
    height: 22,
    borderRadius: 11,
    paddingHorizontal: 6,
    backgroundColor: '#dc2626',
    borderWidth: 2,
    borderColor: '#ffffff',
    justifyContent: 'center',
    alignItems: 'center',
  },
  badgeText: { color: '#ffffff', fontSize: 11, fontWeight: '700' },
});
