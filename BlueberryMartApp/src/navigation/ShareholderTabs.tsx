import React from 'react';
import { StyleSheet, View } from 'react-native';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { Ionicons } from '@expo/vector-icons';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import ShareholderHomeTab from '../screens/tabs/ShareholderHomeTab';
import CustomerShopTab from '../screens/tabs/CustomerShopTab';
import CartScreen from '../screens/tabs/CartScreen';
import BulkTab from '../screens/tabs/BulkTab';
import ActivityTab from '../screens/tabs/ActivityTab';
import { useCart } from '../context/CartContext';

const Tab = createBottomTabNavigator();

export default function ShareholderTabs() {
  const insets = useSafeAreaInsets();
  const { totalCount } = useCart();

  return (
    <Tab.Navigator
      screenOptions={({ route }) => ({
        headerShown: false,
        tabBarActiveTintColor: '#14532d',
        tabBarInactiveTintColor: '#9ca3af',
        tabBarStyle: {
          backgroundColor: '#ffffff',
          borderTopColor: '#f3f4f6',
          borderTopWidth: 1,
          height: 60 + insets.bottom,
          paddingBottom: insets.bottom + 4,
          paddingTop: 8,
        },
        tabBarLabelStyle: { fontSize: 11, fontWeight: '600' },
        tabBarIcon: ({ focused, color, size }) => {
          if (route.name === 'Cart') {
            return (
              <View style={styles.cartBtn}>
                <Ionicons name="cart" size={22} color="#ffffff" />
              </View>
            );
          }
          const icons: Record<string, [string, string]> = {
            Analytics: ['stats-chart', 'stats-chart-outline'],
            Shop: ['storefront', 'storefront-outline'],
            Bulk: ['cube', 'cube-outline'],
            Activity: ['receipt', 'receipt-outline'],
          };
          const [active, inactive] = icons[route.name] ?? ['ellipse', 'ellipse-outline'];
          return <Ionicons name={(focused ? active : inactive) as any} size={size} color={color} />;
        },
      })}
    >
      <Tab.Screen name="Analytics" component={ShareholderHomeTab} />
      <Tab.Screen name="Shop" component={CustomerShopTab} />
      <Tab.Screen
        name="Cart"
        component={CartScreen}
        options={{
          tabBarLabel: () => null,
          tabBarBadge: totalCount > 0 ? totalCount : undefined,
          tabBarBadgeStyle: { backgroundColor: '#dc2626' },
        }}
      />
      <Tab.Screen name="Bulk" component={BulkTab} />
      <Tab.Screen name="Activity" component={ActivityTab} />
    </Tab.Navigator>
  );
}

const styles = StyleSheet.create({
  cartBtn: {
    width: 46, height: 46, borderRadius: 23, backgroundColor: '#16a34a',
    justifyContent: 'center', alignItems: 'center', marginBottom: 2,
    shadowColor: '#16a34a', shadowOffset: { width: 0, height: 2 }, shadowOpacity: 0.4, shadowRadius: 6, elevation: 5,
  },
});
