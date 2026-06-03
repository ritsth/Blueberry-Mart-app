import React from 'react';
import { StyleSheet, Text, TouchableOpacity, View } from 'react-native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { logout } from '../services/authService';
import ShoppingView from '../components/ShoppingView';
import type { RootStackParamList } from '../../App';

type Props = {
  navigation: NativeStackNavigationProp<RootStackParamList, 'CustomerDashboard'>;
};

export default function CustomerDashboard({ navigation }: Props) {
  async function handleLogout() {
    await logout();
    navigation.replace('Login');
  }

  return (
    <View style={styles.container}>
      <ShoppingView />
      <TouchableOpacity style={styles.logoutButton} onPress={handleLogout}>
        <Text style={styles.logoutText}>Sign Out</Text>
      </TouchableOpacity>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#f0fdf4',
    paddingTop: 64,
    paddingHorizontal: 24,
  },
  logoutButton: {
    borderWidth: 1,
    borderColor: '#d1fae5',
    borderRadius: 10,
    paddingVertical: 12,
    alignItems: 'center',
    marginBottom: 40,
    marginTop: 8,
  },
  logoutText: { color: '#16a34a', fontWeight: '600' },
});
