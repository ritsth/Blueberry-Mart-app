import React, { useCallback, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  KeyboardAvoidingView,
  Platform,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from 'react-native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { useFocusEffect } from '@react-navigation/native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { getStoredToken } from '../services/authService';
import type { RootStackParamList } from '../../App';

type Props = {
  navigation: NativeStackNavigationProp<RootStackParamList, 'AddressesScreen'>;
};

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

interface Address {
  id: string;
  label: string;
  addressLine: string;
  city: string;
  phone: string | null;
  isDefault: boolean;
}

export default function AddressesScreen({ navigation }: Props) {
  const insets = useSafeAreaInsets();
  const [addresses, setAddresses] = useState<Address[]>([]);
  const [loading, setLoading]     = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [showForm, setShowForm]   = useState(false);
  const [saving, setSaving]       = useState(false);

  // Form fields
  const [label, setLabel]             = useState('');
  const [addressLine, setAddressLine] = useState('');
  const [city, setCity]               = useState('');
  const [phone, setPhone]             = useState('');

  useFocusEffect(useCallback(() => { fetchAddresses(); }, []));

  async function onRefresh() {
    setRefreshing(true);
    try { await fetchAddresses(); } finally { setRefreshing(false); }
  }

  async function fetchAddresses() {
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/addresses`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.ok) setAddresses(await res.json());
    } finally {
      setLoading(false);
    }
  }

  async function saveAddress() {
    if (!label.trim() || !addressLine.trim() || !city.trim()) {
      Alert.alert('Missing fields', 'Label, address, and city are required.');
      return;
    }
    setSaving(true);
    try {
      const token = await getStoredToken();
      const res = await fetch(`${API_BASE}/api/addresses`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
        body: JSON.stringify({
          label: label.trim(),
          addressLine: addressLine.trim(),
          city: city.trim(),
          phone: phone.trim() || null,
          isDefault: addresses.length === 0,
        }),
      });
      if (!res.ok) {
        Alert.alert('Failed', 'Could not save address.');
        return;
      }
      setLabel(''); setAddressLine(''); setCity(''); setPhone('');
      setShowForm(false);
      await fetchAddresses();
    } catch {
      Alert.alert('Error', 'Could not save address. Check your connection.');
    } finally {
      setSaving(false);
    }
  }

  async function setDefault(id: string) {
    const token = await getStoredToken();
    await fetch(`${API_BASE}/api/addresses/${id}/default`, {
      method: 'PUT',
      headers: { Authorization: `Bearer ${token}` },
    });
    fetchAddresses();
  }

  function confirmDelete(addr: Address) {
    Alert.alert('Delete Address', `Remove "${addr.label}"?`, [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Delete', style: 'destructive', onPress: async () => {
          const token = await getStoredToken();
          await fetch(`${API_BASE}/api/addresses/${addr.id}`, {
            method: 'DELETE',
            headers: { Authorization: `Bearer ${token}` },
          });
          fetchAddresses();
        },
      },
    ]);
  }

  return (
    <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <ScrollView
        style={styles.container}
        contentContainerStyle={[styles.content, { paddingTop: insets.top + 12 }]}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor="#16a34a" colors={['#16a34a']} />}
      >
        <TouchableOpacity onPress={() => navigation.goBack()} style={styles.backRow}>
          <Text style={styles.backText}>← Back</Text>
        </TouchableOpacity>
        <Text style={styles.heading}>Delivery Addresses</Text>

        {loading ? (
          <ActivityIndicator size="large" color="#16a34a" style={{ marginTop: 40 }} />
        ) : (
          <>
            {addresses.length === 0 && !showForm && (
              <Text style={styles.emptyNote}>No saved addresses yet.</Text>
            )}

            {addresses.map(addr => (
              <View key={addr.id} style={styles.addressCard}>
                <View style={styles.addressHeader}>
                  <Text style={styles.addressLabel}>{addr.label}</Text>
                  {addr.isDefault && (
                    <View style={styles.defaultBadge}><Text style={styles.defaultBadgeText}>Default</Text></View>
                  )}
                </View>
                <Text style={styles.addressText}>{addr.addressLine}</Text>
                <Text style={styles.addressCity}>{addr.city}</Text>
                {addr.phone && <Text style={styles.addressPhone}>📞 {addr.phone}</Text>}
                <View style={styles.addressActions}>
                  {!addr.isDefault && (
                    <TouchableOpacity onPress={() => setDefault(addr.id)}>
                      <Text style={styles.setDefaultText}>Set as default</Text>
                    </TouchableOpacity>
                  )}
                  <TouchableOpacity onPress={() => confirmDelete(addr)}>
                    <Text style={styles.deleteText}>Delete</Text>
                  </TouchableOpacity>
                </View>
              </View>
            ))}

            {showForm ? (
              <View style={styles.formCard}>
                <Text style={styles.formTitle}>New Address</Text>
                <TextInput style={styles.input} placeholder="Label (e.g. Home, Work)" placeholderTextColor="#9ca3af" value={label} onChangeText={setLabel} />
                <TextInput style={styles.input} placeholder="Address (street, area, landmark)" placeholderTextColor="#9ca3af" value={addressLine} onChangeText={setAddressLine} />
                <TextInput style={styles.input} placeholder="City" placeholderTextColor="#9ca3af" value={city} onChangeText={setCity} />
                <TextInput style={styles.input} placeholder="Phone (optional)" placeholderTextColor="#9ca3af" keyboardType="phone-pad" value={phone} onChangeText={setPhone} />
                <View style={styles.formButtons}>
                  <TouchableOpacity style={styles.cancelBtn} onPress={() => setShowForm(false)}>
                    <Text style={styles.cancelBtnText}>Cancel</Text>
                  </TouchableOpacity>
                  <TouchableOpacity style={[styles.saveBtn, saving && styles.saveBtnDisabled]} onPress={saveAddress} disabled={saving}>
                    {saving ? <ActivityIndicator color="#fff" /> : <Text style={styles.saveBtnText}>Save</Text>}
                  </TouchableOpacity>
                </View>
              </View>
            ) : (
              <TouchableOpacity style={styles.addButton} onPress={() => setShowForm(true)} activeOpacity={0.8}>
                <Text style={styles.addButtonText}>＋ Add New Address</Text>
              </TouchableOpacity>
            )}
          </>
        )}

        <View style={{ height: 32 }} />
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f9fafb' },
  content: { paddingHorizontal: 24, paddingBottom: 32 },
  backRow: { marginBottom: 16 },
  backText: { color: '#16a34a', fontWeight: '600', fontSize: 14 },
  heading: { fontSize: 24, fontWeight: '700', color: '#111827', marginBottom: 20 },
  emptyNote: { textAlign: 'center', color: '#9ca3af', fontSize: 13, marginVertical: 24 },
  addressCard: {
    backgroundColor: '#ffffff', borderRadius: 12, padding: 16, marginBottom: 12,
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
  },
  addressHeader: { flexDirection: 'row', alignItems: 'center', marginBottom: 6 },
  addressLabel: { fontSize: 15, fontWeight: '700', color: '#111827', marginRight: 8 },
  defaultBadge: { backgroundColor: '#f0fdf4', borderRadius: 6, paddingVertical: 2, paddingHorizontal: 8, borderWidth: 1, borderColor: '#bbf7d0' },
  defaultBadgeText: { fontSize: 10, fontWeight: '700', color: '#16a34a' },
  addressText: { fontSize: 14, color: '#374151', marginBottom: 2 },
  addressCity: { fontSize: 13, color: '#6b7280' },
  addressPhone: { fontSize: 13, color: '#6b7280', marginTop: 4 },
  addressActions: { flexDirection: 'row', gap: 20, marginTop: 12 },
  setDefaultText: { fontSize: 13, color: '#16a34a', fontWeight: '600' },
  deleteText: { fontSize: 13, color: '#dc2626', fontWeight: '600' },
  formCard: {
    backgroundColor: '#ffffff', borderRadius: 12, padding: 18, marginBottom: 12,
    borderWidth: 1, borderColor: '#bbf7d0',
  },
  formTitle: { fontSize: 16, fontWeight: '700', color: '#14532d', marginBottom: 14 },
  input: {
    backgroundColor: '#f9fafb', borderWidth: 1, borderColor: '#e5e7eb', borderRadius: 10,
    paddingHorizontal: 14, paddingVertical: 12, fontSize: 14, color: '#111827', marginBottom: 10,
  },
  formButtons: { flexDirection: 'row', gap: 10, marginTop: 4 },
  cancelBtn: { flex: 1, borderWidth: 1.5, borderColor: '#e5e7eb', borderRadius: 10, paddingVertical: 13, alignItems: 'center' },
  cancelBtnText: { color: '#374151', fontWeight: '600', fontSize: 14 },
  saveBtn: { flex: 1, backgroundColor: '#16a34a', borderRadius: 10, paddingVertical: 13, alignItems: 'center' },
  saveBtnDisabled: { backgroundColor: '#86efac' },
  saveBtnText: { color: '#ffffff', fontWeight: '700', fontSize: 14 },
  addButton: {
    backgroundColor: '#ffffff', borderRadius: 12, paddingVertical: 16, alignItems: 'center',
    borderWidth: 1.5, borderColor: '#bbf7d0', borderStyle: 'dashed',
  },
  addButtonText: { color: '#16a34a', fontWeight: '700', fontSize: 15 },
});
