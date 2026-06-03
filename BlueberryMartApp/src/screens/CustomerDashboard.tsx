import React from 'react';
import { StyleSheet, Text, TouchableOpacity, View } from 'react-native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { logout } from '../services/authService';
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
      <Text style={styles.emoji}>🛒</Text>
      <Text style={styles.title}>Customer Dashboard</Text>
      <Text style={styles.subtitle}>Browse inventory and place orders.</Text>
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
    justifyContent: 'center',
    alignItems: 'center',
    padding: 24,
  },
  emoji:       { fontSize: 56, marginBottom: 16 },
  title:       { fontSize: 24, fontWeight: '700', color: '#14532d', marginBottom: 8 },
  subtitle:    { fontSize: 15, color: '#6b7280', marginBottom: 40 },
  logoutButton: {
    borderWidth: 1,
    borderColor: '#d1fae5',
    borderRadius: 10,
    paddingVertical: 10,
    paddingHorizontal: 28,
  },
  logoutText:  { color: '#16a34a', fontWeight: '600' },
});
