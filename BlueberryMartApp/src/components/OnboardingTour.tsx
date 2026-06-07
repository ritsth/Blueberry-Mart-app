import React, { useEffect, useState } from 'react';
import { Modal, StyleSheet, Text, TouchableOpacity, useWindowDimensions, View } from 'react-native';
import Svg, { Defs, Mask, Rect } from 'react-native-svg';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import AsyncStorage from '@react-native-async-storage/async-storage';

export const TOUR_SEEN_KEY = 'customer_tour_seen_v2';

const TAB_COUNT = 5; // Shop · Bulk · Cart · Activity · Assistant

type Target = { kind: 'header' } | { kind: 'tab'; index: number } | null;
interface Step { target: Target; title: string; body: string; }

const STEPS: Step[] = [
  { target: null, title: 'Welcome to Blueberry Mart', body: 'Fresh groceries from your nearby branches. Here’s a 20-second tour — tap Next.' },
  { target: { kind: 'header' }, title: 'Delivery, alerts & profile', body: 'Set your delivery address on the left; find notifications and your profile on the right.' },
  { target: { kind: 'tab', index: 0 }, title: 'Shop', body: 'Browse a branch or search for items across all branches.' },
  { target: { kind: 'tab', index: 2 }, title: 'Your cart', body: 'Items from every branch collect here. Check out and pay with eSewa.' },
  { target: { kind: 'tab', index: 3 }, title: 'Activity', body: 'Track your orders, pay pending ones, mark received, and leave reviews.' },
  { target: { kind: 'tab', index: 4 }, title: 'Assistant', body: 'Ask about items, prices, or your orders anytime.' },
];

/**
 * First-login spotlight tour for customers: dims the screen and cuts a highlight over
 * the actual tab / header being described, with a pointer + tooltip. Shows once
 * (persisted in AsyncStorage). Mounted in CustomerTabs so it overlays the app.
 */
export default function OnboardingTour() {
  const insets = useSafeAreaInsets();
  const { width: W, height: H } = useWindowDimensions();
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

  // Compute the highlight rect (in screen coords) for the current target.
  let hole: { x: number; y: number; w: number; h: number; cx: number } | null = null;
  if (s.target?.kind === 'header') {
    hole = { x: 8, y: insets.top + 2, w: W - 16, h: 52, cx: W / 2 };
  } else if (s.target?.kind === 'tab') {
    const tabW = W / TAB_COUNT;
    const cx = tabW * s.target.index + tabW / 2;
    const w = Math.min(tabW - 8, 64);
    hole = { x: cx - w / 2, y: tabBarTop + 3, w, h: 54, cx };
  }

  // Tooltip placement: below the header, above the tab bar, or centered (welcome).
  const tipStyle = !hole
    ? { top: H * 0.3, left: 24, right: 24 }
    : s.target?.kind === 'header'
      ? { top: hole.y + hole.h + 16, left: 20, right: 20 }
      : { bottom: tabBarH + 20, left: 20, right: 20 };

  return (
    <Modal visible transparent animationType="fade" statusBarTranslucent onRequestClose={finish}>
      <View style={StyleSheet.absoluteFill}>
        {/* Dim everything except the highlight hole */}
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

        {/* Down-pointer toward a highlighted tab */}
        {hole && s.target?.kind === 'tab' && (
          <View style={[styles.pointerDown, { bottom: tabBarH + 10, left: hole.cx - 9 }]} />
        )}

        {/* Tooltip card */}
        <View style={[styles.tip, tipStyle]}>
          <TouchableOpacity style={styles.skip} onPress={finish} hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}>
            <Text style={styles.skipText}>Skip</Text>
          </TouchableOpacity>
          <Text style={styles.title}>{s.title}</Text>
          <Text style={styles.body}>{s.body}</Text>
          <View style={styles.footer}>
            <View style={styles.dots}>
              {STEPS.map((_, i) => <View key={i} style={[styles.dot, i === step && styles.dotActive]} />)}
            </View>
            <TouchableOpacity style={styles.cta} onPress={() => (isLast ? finish() : setStep(step + 1))} activeOpacity={0.85}>
              <Text style={styles.ctaText}>{isLast ? 'Get started' : 'Next'}</Text>
            </TouchableOpacity>
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
  dots: { flexDirection: 'row', gap: 6 },
  dot: { width: 7, height: 7, borderRadius: 4, backgroundColor: '#e5e7eb' },
  dotActive: { backgroundColor: '#16a34a', width: 18 },
  cta: { backgroundColor: '#16a34a', borderRadius: 10, paddingVertical: 10, paddingHorizontal: 22 },
  ctaText: { color: '#ffffff', fontSize: 14, fontWeight: '700' },
  pointerDown: {
    position: 'absolute', width: 0, height: 0,
    borderLeftWidth: 9, borderRightWidth: 9, borderTopWidth: 10,
    borderLeftColor: 'transparent', borderRightColor: 'transparent', borderTopColor: '#ffffff',
  },
});
