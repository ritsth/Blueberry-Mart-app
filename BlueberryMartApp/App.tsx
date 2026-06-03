import React from 'react';
import { NavigationContainer } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { StatusBar } from 'expo-status-bar';

import LoginScreen          from './src/screens/LoginScreen';
import CustomerDashboard    from './src/screens/CustomerDashboard';
import ShareholderDashboard from './src/screens/ShareholderDashboard';
import ReviewScreen         from './src/screens/ReviewScreen';

export type RootStackParamList = {
  Login:                undefined;
  CustomerDashboard:    undefined;
  ShareholderDashboard: undefined;
  ReviewScreen:         { orderId: string; items: { id: string; name: string }[] };
};

const Stack = createNativeStackNavigator<RootStackParamList>();

export default function App() {
  return (
    <NavigationContainer>
      <StatusBar style="dark" />
      <Stack.Navigator
        initialRouteName="Login"
        screenOptions={{ headerShown: false }}
      >
        <Stack.Screen name="Login"                component={LoginScreen} />
        <Stack.Screen name="CustomerDashboard"    component={CustomerDashboard} />
        <Stack.Screen name="ShareholderDashboard" component={ShareholderDashboard} />
        <Stack.Screen name="ReviewScreen"         component={ReviewScreen} />
      </Stack.Navigator>
    </NavigationContainer>
  );
}
