import React, { useEffect, useState } from 'react';
import { Modal, StyleSheet, Text, TouchableOpacity, useWindowDimensions, View } from 'react-native';
import Svg, { Defs, Mask, Rect } from 'react-native-svg';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { useTour } from '../context/TourContext';

export const TOUR_SEEN_KEY = 'customer_tour_seen_v3';

const TAB_COUNT = 5; // Shop · Bulk · Cart · Activity · Assistant
const TAB_INDEX: Record<string, number> = { Shop: 0, Bulk: 1, Cart: 2, Activity: 3, Assistant: 4 };

type Hole = { x: number; y: number; w: number; h: number; cx: number };
interface Step { target: string | null; place: 'top' | 'bottom' | 'center'; title: string; body: string; }

const STEPS: Step[] = [
  { target: null, place: 'center', title: 'Welcome to Blueberry Mart', body: 'Fresh groceries from your nearby branches. Here’s a quick tour — tap Next.' },
  { target: 'header', place: 'bottom', title: 'Delivery, alerts & profile', body: 'Set your delivery address on the left; find notifications and your profile on the right.' },
  { target: 'Shop', place: 'top', title: 'Shop', body: 'Browse a branch or search for items across all branches.' },
  { target: 'Bulk', place: 'top', title: 'Bulk orders', body: 'Buy business quantities (25kg rice, 20L oil…) at member pricing. Join Blueberry Plus to unlock bulk ordering and free delivery.' },
  { target: 'Cart', place: 'top', title: 'Your cart', body: 'Items from every branch collect here. Check out and pay with eSewa.' },
  { target: 'Activity', place: 'top', title: 'Activity', body: 'Track orders, pay pending ones, mark received, and leave reviews.' },
  { target: 'Assistant', place: 'top', title: 'Assistant', body: 'Ask about items, prices, or your orders anytime.' },
];

/**
 * First-login spotlight tour for customers: dims the screen and cuts a highlight over
 * the real element. Uses measured rects reported to the TourContext (header + tab
 * buttons) and falls back to computed positions if a measurement isn't in yet.
 * Shows once (persisted in AsyncStorage); replayable from Account.
 */
export default function OnboardingTour() {
  const insets = useSafeAreaInsets();
  const { width: W, height: H } = useWindowDimensions();
  const tour = useTour();
  const [visible, setVisible] = useState(false);
  const [step, setStep] = useState(0);

  useEffect(() => {
    let alive = true;
    AsyncStorage.getItem(TOUR_SEEN_KEY).then(v => { if (alive && v == null) setVisible(true); });
    return () => { alive = false; };
  }, []);

  async function finish() {
    setVisible(false);
    try { await AsyncStorage.setItem(TOUR_SEEN_KEY, '1'); } catch { /* non-blocking */ }
  }

  if (!visible) return null;

  const s = STEPS[step];
  const isLast = step === STEPS.length - 1;
  const tabBarH = 60 + insets.bottom;
  const tabBarTop = H - tabBarH;

  function holeFor(target: string | null): Hole | null {
    if (!target) return null;
    const m = tour?.rects[target];
    if (m && m.w > 0) {
      const pad = 6; // breathing room around the measured element
      return { x: m.x - pad, y: m.y - pad, w: m.w + pad * 2, h: m.h + pad * 2, cx: m.x + m.w / 2 };
    }
    // computed fallback (used until a measurement arrives, e.g. the Cart tab)
    if (target === 'header') return { x: 8, y: insets.top + 2, w: W - 16, h: 52, cx: W / 2 };
    const idx = TAB_INDEX[target];
    if (idx != null) {
      const tabW = W / TAB_COUNT;
      const cx = tabW * idx + tabW / 2;
      const w = Math.min(tabW - 8, 64);
      return { x: cx - w / 2, y: tabBarTop + 3, w, h: 54, cx };
    }
    return null;
  }

  const hole = holeFor(s.target);
  const tip = !hole || s.place === 'center'
    ? { top: H * 0.3, left: 24, right: 24 }
    : s.place === 'bottom'
      ? { top: hole.y + hole.h + 14, left: 20, right: 20 }
      : { bottom: (H - hole.y) + 14, left: 20, right: 20 };

  return (
    <Modal visible transparent animationType="fade" statusBarTranslucent onRequestClose={finish}>
      <View style={StyleSheet.absoluteFill}>
        <Svg width={W} height={H} style={StyleSheet.absoluteFill}>
          <Defs>
            <Mask id="spot">
              <Rect x={0} y={0} width={W} height={H} fill="white" />
              {hole && <Rect x={hole.x} y={hole.y} width={hole.w} height={hole.h} rx={14} fill="black" />}
            </Mask>
          </Defs>
          <Rect x={0} y={0} width={W} height={H} fill="rgba(0,0,0,0.62)" mask="url(#spot)" />
          {hole && <Rect x={hole.x} y={hole.y} width={hole.w} height={hole.h} rx={14} fill="none" stroke="#16a34a" strokeWidth={2} />}
        </Svg>

        {hole && s.place === 'top' && (
          <View style={[styles.pointerDown, { top: hole.y - 10, left: hole.cx - 9 }]} />
        )}

        <View style={[styles.tip, tip]}>
          <TouchableOpacity style={styles.skip} onPress={finish} hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}>
            <Text style={styles.skipText}>Skip</Text>
          </TouchableOpacity>
          <Text style={styles.title}>{s.title}</Text>
          <Text style={styles.body}>{s.body}</Text>
          <View style={styles.footer}>
            <View style={styles.dots}>
              {STEPS.map((_, i) => <View key={i} style={[styles.dot, i === step && styles.dotActive]} />)}
            </View>
            <View style={styles.btnRow}>
              {step > 0 && (
                <TouchableOpacity style={styles.back} onPress={() => setStep(step - 1)} activeOpacity={0.7}>
                  <Text style={styles.backText}>Back</Text>
                </TouchableOpacity>
              )}
              <TouchableOpacity style={styles.cta} onPress={() => (isLast ? finish() : setStep(step + 1))} activeOpacity={0.85}>
                <Text style={styles.ctaText}>{isLast ? 'Get started' : 'Next'}</Text>
              </TouchableOpacity>
            </View>
          </View>
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  tip: {
    position: 'absolute', backgroundColor: '#ffffff', borderRadius: 16, padding: 18,
    shadowColor: '#000', shadowOffset: { width: 0, height: 4 }, shadowOpacity: 0.2, shadowRadius: 12, elevation: 8,
  },
  skip: { position: 'absolute', top: 12, right: 14 },
  skipText: { fontSize: 13, color: '#9ca3af', fontWeight: '600' },
  title: { fontSize: 17, fontWeight: '800', color: '#111827', marginBottom: 6, marginRight: 40 },
  body: { fontSize: 14, color: '#6b7280', lineHeight: 20, marginBottom: 16 },
  footer: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  dots: { flexDirection: 'row', gap: 6, flexShrink: 1 },
  dot: { width: 7, height: 7, borderRadius: 4, backgroundColor: '#e5e7eb' },
  dotActive: { backgroundColor: '#16a34a', width: 18 },
  btnRow: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  back: { borderRadius: 10, paddingVertical: 10, paddingHorizontal: 16, borderWidth: 1, borderColor: '#e5e7eb' },
  backText: { color: '#6b7280', fontSize: 14, fontWeight: '700' },
  cta: { backgroundColor: '#16a34a', borderRadius: 10, paddingVertical: 10, paddingHorizontal: 22 },
  ctaText: { color: '#ffffff', fontSize: 14, fontWeight: '700' },
  pointerDown: {
    position: 'absolute', width: 0, height: 0,
    borderLeftWidth: 9, borderRightWidth: 9, borderTopWidth: 10,
    borderLeftColor: 'transparent', borderRightColor: 'transparent', borderTopColor: '#ffffff',
  },
});
