import React, { useEffect, useState } from 'react';
import { Modal, StyleSheet, Text, TouchableOpacity, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import AsyncStorage from '@react-native-async-storage/async-storage';

export const TOUR_SEEN_KEY = 'customer_tour_seen_v1';

interface Step { icon: any; title: string; body: string; }

const STEPS: Step[] = [
  {
    icon: 'storefront',
    title: 'Welcome to Blueberry Mart',
    body: 'Fresh groceries from your nearby branches. Here’s a quick 20-second tour of how everything works.',
  },
  {
    icon: 'search',
    title: 'Shop & search',
    body: 'Pick a branch or search across all branches from the Shop tab. Set your delivery address from the bar up top.',
  },
  {
    icon: 'cart',
    title: 'Your cart',
    body: 'The green cart button in the middle holds your items from every branch. Add items, then check out and pay with eSewa.',
  },
  {
    icon: 'receipt',
    title: 'Track orders & alerts',
    body: 'See your orders, pay pending ones, mark them received and leave reviews in Activity. The bell up top shows back-in-stock alerts.',
  },
  {
    icon: 'chatbubble-ellipses',
    title: 'Need a hand?',
    body: 'Ask the Assistant about items, prices, or your own orders anytime. Tap “Get started” to begin shopping!',
  },
];

/**
 * First-login walkthrough for customers. Shows once (persisted in AsyncStorage);
 * renders nothing thereafter. Mounted inside CustomerTabs so it overlays the app.
 */
export default function OnboardingTour() {
  const [visible, setVisible] = useState(false);
  const [step, setStep] = useState(0);

  useEffect(() => {
    let alive = true;
    AsyncStorage.getItem(TOUR_SEEN_KEY).then(v => {
      if (alive && v == null) setVisible(true);
    });
    return () => { alive = false; };
  }, []);

  async function finish() {
    setVisible(false);
    try { await AsyncStorage.setItem(TOUR_SEEN_KEY, '1'); } catch { /* non-blocking */ }
  }

  if (!visible) return null;

  const isLast = step === STEPS.length - 1;
  const s = STEPS[step];

  return (
    <Modal visible transparent animationType="fade" onRequestClose={finish} statusBarTranslucent>
      <View style={styles.backdrop}>
        <View style={styles.card}>
          <TouchableOpacity style={styles.skip} onPress={finish} hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}>
            <Text style={styles.skipText}>Skip</Text>
          </TouchableOpacity>

          <View style={styles.iconCircle}>
            <Ionicons name={s.icon} size={34} color="#16a34a" />
          </View>
          <Text style={styles.title}>{s.title}</Text>
          <Text style={styles.body}>{s.body}</Text>

          <View style={styles.dots}>
            {STEPS.map((_, i) => (
              <View key={i} style={[styles.dot, i === step && styles.dotActive]} />
            ))}
          </View>

          <TouchableOpacity
            style={styles.cta}
            onPress={() => (isLast ? finish() : setStep(step + 1))}
            activeOpacity={0.85}
          >
            <Text style={styles.ctaText}>{isLast ? 'Get started' : 'Next'}</Text>
            {!isLast && <Ionicons name="arrow-forward" size={16} color="#fff" />}
          </TouchableOpacity>
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.55)', justifyContent: 'center', paddingHorizontal: 28 },
  card: { backgroundColor: '#ffffff', borderRadius: 20, padding: 24, alignItems: 'center' },
  skip: { position: 'absolute', top: 14, right: 16 },
  skipText: { fontSize: 13, color: '#9ca3af', fontWeight: '600' },
  iconCircle: { width: 72, height: 72, borderRadius: 36, backgroundColor: '#f0fdf4', justifyContent: 'center', alignItems: 'center', marginTop: 8, marginBottom: 16 },
  title: { fontSize: 19, fontWeight: '800', color: '#111827', marginBottom: 8, textAlign: 'center' },
  body: { fontSize: 14, color: '#6b7280', lineHeight: 21, textAlign: 'center', marginBottom: 20 },
  dots: { flexDirection: 'row', gap: 6, marginBottom: 20 },
  dot: { width: 7, height: 7, borderRadius: 4, backgroundColor: '#e5e7eb' },
  dotActive: { backgroundColor: '#16a34a', width: 18 },
  cta: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 8, backgroundColor: '#16a34a', borderRadius: 12, paddingVertical: 14, alignSelf: 'stretch' },
  ctaText: { color: '#ffffff', fontSize: 15, fontWeight: '700' },
});
