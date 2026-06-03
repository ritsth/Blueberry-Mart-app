import React from 'react';
import { StyleSheet, View } from 'react-native';
import ShoppingView from '../../components/ShoppingView';

export default function CustomerShopTab() {
  return (
    <View style={styles.container}>
      <ShoppingView />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#f9fafb',
    paddingTop: 56,
    paddingHorizontal: 24,
  },
});
