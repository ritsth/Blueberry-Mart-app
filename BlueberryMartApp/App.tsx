import React from 'react';
import { View } from 'react-native';
import { NavigationContainer } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { StatusBar } from 'expo-status-bar';

import MaintenanceBanner from './src/components/MaintenanceBanner';

import LoginScreen       from './src/screens/LoginScreen';
import RegisterScreen    from './src/screens/RegisterScreen';
import ReviewScreen      from './src/screens/ReviewScreen';
import AddressesScreen   from './src/screens/AddressesScreen';
import AlertsScreen      from './src/screens/AlertsScreen';
import AccountTab        from './src/screens/tabs/AccountTab';
import ExploreTab        from './src/screens/tabs/ExploreTab';
import CustomerTabs      from './src/navigation/CustomerTabs';
import ShareholderTabs   from './src/navigation/ShareholderTabs';
import { CartProvider }  from './src/context/CartContext';

export type RootStackParamList = {
  Login:            undefined;
  Register:         undefined;
  CustomerTabs:     undefined;
  ShareholderTabs:  undefined;
  ReviewScreen:     { orderId: string; items: { id: string; name: string }[] };
  AddressesScreen:  undefined;
  AlertsScreen:     undefined;
  Account:          undefined;
  Explore:          { report?: any } | undefined;
};

const Stack = createNativeStackNavigator<RootStackParamList>();

export default function App() {
  return (
    <NavigationContainer>
      <StatusBar style="dark" />
      <CartProvider>
        <View style={{ flex: 1 }}>
          <MaintenanceBanner />
          <View style={{ flex: 1 }}>
            <Stack.Navigator initialRouteName="Login" screenOptions={{ headerShown: false }}>
              <Stack.Screen name="Login"           component={LoginScreen} />
              <Stack.Screen name="Register"        component={RegisterScreen} />
              <Stack.Screen name="CustomerTabs"    component={CustomerTabs} />
              <Stack.Screen name="ShareholderTabs" component={ShareholderTabs} />
              <Stack.Screen name="ReviewScreen"    component={ReviewScreen} />
              <Stack.Screen name="AddressesScreen" component={AddressesScreen} />
              <Stack.Screen name="AlertsScreen"    component={AlertsScreen} />
              <Stack.Screen name="Account"         component={AccountTab} />
              <Stack.Screen name="Explore"         component={ExploreTab} />
            </Stack.Navigator>
          </View>
        </View>
      </CartProvider>
    </NavigationContainer>
  );
}
