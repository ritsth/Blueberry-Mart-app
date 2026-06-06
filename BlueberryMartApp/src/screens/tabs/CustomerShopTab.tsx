import React from 'react';
import { StyleSheet, View } from 'react-native';
import AppHeader from '../../components/AppHeader';
import ShoppingView from '../../components/ShoppingView';

export default function CustomerShopTab() {
  return (
    <View style={styles.container}>
      <AppHeader />
      <View style={styles.body}>
        <ShoppingView />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f9fafb' },
  body: { flex: 1, paddingHorizontal: 24, paddingTop: 16 },
});
