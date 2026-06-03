import React from 'react';
import {
  FlatList,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { logout } from '../services/authService';
import type { RootStackParamList } from '../../App';

type Props = {
  navigation: NativeStackNavigationProp<RootStackParamList, 'CustomerDashboard'>;
};

const PLACEHOLDER_BRANCHES = [
  { id: '1', name: 'Blueberry Mart Downtown', city: 'Kathmandu' },
  { id: '2', name: 'Blueberry Mart Suburbs',  city: 'Lalitpur'  },
];

export default function CustomerDashboard({ navigation }: Props) {
  async function handleLogout() {
    await logout();
    navigation.replace('Login');
  }

  return (
    <View style={styles.container}>
      <Text style={styles.heading}>Welcome to the Grocery Store</Text>
      <Text style={styles.subheading}>Select a branch to start shopping</Text>

      <FlatList
        data={PLACEHOLDER_BRANCHES}
        keyExtractor={item => item.id}
        contentContainerStyle={styles.list}
        renderItem={({ item }) => (
          <View style={styles.branchCard}>
            <Text style={styles.branchName}>{item.name}</Text>
            <Text style={styles.branchCity}>{item.city}</Text>
          </View>
        )}
      />

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
  heading: {
    fontSize: 26,
    fontWeight: '700',
    color: '#14532d',
    marginBottom: 4,
  },
  subheading: {
    fontSize: 14,
    color: '#6b7280',
    marginBottom: 28,
  },
  list: {
    gap: 12,
  },
  branchCard: {
    backgroundColor: '#ffffff',
    borderRadius: 12,
    padding: 18,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 6,
    elevation: 2,
  },
  branchName: {
    fontSize: 16,
    fontWeight: '600',
    color: '#14532d',
    marginBottom: 2,
  },
  branchCity: {
    fontSize: 13,
    color: '#6b7280',
  },
  logoutButton: {
    marginTop: 32,
    marginBottom: 40,
    borderWidth: 1,
    borderColor: '#d1fae5',
    borderRadius: 10,
    paddingVertical: 12,
    alignItems: 'center',
  },
  logoutText: {
    color: '#16a34a',
    fontWeight: '600',
  },
});
