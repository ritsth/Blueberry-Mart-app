import React from 'react';
import { NavigationContainer } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { StatusBar } from 'expo-status-bar';

import LoginScreen       from './src/screens/LoginScreen';
import RegisterScreen    from './src/screens/RegisterScreen';
import ReviewScreen      from './src/screens/ReviewScreen';
import AddressesScreen   from './src/screens/AddressesScreen';
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
};

const Stack = createNativeStackNavigator<RootStackParamList>();

export default function App() {
  return (
    <NavigationContainer>
      <StatusBar style="dark" />
      <CartProvider>
        <Stack.Navigator initialRouteName="Login" screenOptions={{ headerShown: false }}>
          <Stack.Screen name="Login"           component={LoginScreen} />
          <Stack.Screen name="Register"        component={RegisterScreen} />
          <Stack.Screen name="CustomerTabs"    component={CustomerTabs} />
          <Stack.Screen name="ShareholderTabs" component={ShareholderTabs} />
          <Stack.Screen name="ReviewScreen"    component={ReviewScreen} />
          <Stack.Screen name="AddressesScreen" component={AddressesScreen} />
        </Stack.Navigator>
      </CartProvider>
    </NavigationContainer>
  );
}
