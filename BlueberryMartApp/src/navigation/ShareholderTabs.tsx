import React from 'react';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { Ionicons } from '@expo/vector-icons';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import ShareholderHomeTab from '../screens/tabs/ShareholderHomeTab';
import ExploreTab         from '../screens/tabs/ExploreTab';
import BulkTab            from '../screens/tabs/BulkTab';
import ActivityTab        from '../screens/tabs/ActivityTab';
import AccountTab         from '../screens/tabs/AccountTab';

const Tab = createBottomTabNavigator();

export default function ShareholderTabs() {
  const insets = useSafeAreaInsets();
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
          height: 56 + insets.bottom,
          paddingBottom: insets.bottom + 4,
          paddingTop: 10,
        },
        tabBarLabelStyle: { fontSize: 11, fontWeight: '600' },
        tabBarIcon: ({ focused, color, size }) => {
          const icons: Record<string, [string, string]> = {
            Home:     ['home',           'home-outline'],
            Explore:  ['stats-chart',    'stats-chart-outline'],
            Bulk:     ['cube',           'cube-outline'],
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
      <Tab.Screen name="Home"     component={ShareholderHomeTab} />
      <Tab.Screen name="Explore"  component={ExploreTab} />
      <Tab.Screen name="Bulk"     component={BulkTab} />
      <Tab.Screen name="Activity" component={ActivityTab} />
      <Tab.Screen name="Account"  component={AccountTab} />
    </Tab.Navigator>
  );
}
