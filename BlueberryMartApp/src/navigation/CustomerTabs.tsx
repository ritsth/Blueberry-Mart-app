import React from 'react';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { Ionicons } from '@expo/vector-icons';
import CustomerShopTab from '../screens/tabs/CustomerShopTab';
import ActivityTab     from '../screens/tabs/ActivityTab';
import AccountTab      from '../screens/tabs/AccountTab';

const Tab = createBottomTabNavigator();

export default function CustomerTabs() {
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
          height: 80,
          paddingBottom: 18,
          paddingTop: 10,
        },
        tabBarLabelStyle: { fontSize: 11, fontWeight: '600' },
        tabBarIcon: ({ focused, color, size }) => {
          const icons: Record<string, [string, string]> = {
            Shop:     ['storefront',     'storefront-outline'],
            Activity: ['receipt',        'receipt-outline'],
            Account:  ['person-circle',  'person-circle-outline'],
          };
          const [active, inactive] = icons[route.name] ?? ['ellipse', 'ellipse-outline'];
          return (
            <Ionicons
              name={(focused ? active : inactive) as any}
              size={size}
              color={color}
            />
          );
        },
      })}
    >
      <Tab.Screen name="Shop"     component={CustomerShopTab} />
      <Tab.Screen name="Activity" component={ActivityTab} />
      <Tab.Screen name="Account"  component={AccountTab} />
    </Tab.Navigator>
  );
}
