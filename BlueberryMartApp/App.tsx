import React from 'react';
import { NavigationContainer } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { StatusBar } from 'expo-status-bar';

import LoginScreen       from './src/screens/LoginScreen';
import ReviewScreen      from './src/screens/ReviewScreen';
import CustomerTabs      from './src/navigation/CustomerTabs';
import ShareholderTabs   from './src/navigation/ShareholderTabs';

export type RootStackParamList = {
  Login:            undefined;
  CustomerTabs:     undefined;
  ShareholderTabs:  undefined;
  ReviewScreen:     { orderId: string; items: { id: string; name: string }[] };
};

const Stack = createNativeStackNavigator<RootStackParamList>();

export default function App() {
  return (
    <NavigationContainer>
      <StatusBar style="dark" />
      <Stack.Navigator initialRouteName="Login" screenOptions={{ headerShown: false }}>
        <Stack.Screen name="Login"           component={LoginScreen} />
        <Stack.Screen name="CustomerTabs"    component={CustomerTabs} />
        <Stack.Screen name="ShareholderTabs" component={ShareholderTabs} />
        <Stack.Screen name="ReviewScreen"    component={ReviewScreen} />
      </Stack.Navigator>
    </NavigationContainer>
  );
}
