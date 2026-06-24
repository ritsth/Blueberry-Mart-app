import React, { useEffect, useRef, useState } from 'react';
import {
  ActivityIndicator,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { RouteProp } from '@react-navigation/native';
import { isEmailVerified, resendVerification } from '../services/authService';
import type { RootStackParamList } from '../../App';

type Props = {
  navigation: NativeStackNavigationProp<RootStackParamList, 'CheckEmail'>;
  route: RouteProp<RootStackParamList, 'CheckEmail'>;
};

export default function CheckEmailScreen({ navigation, route }: Props) {
  const { email } = route.params;
  const [sending, setSending] = useState(false);
  const [sent, setSent] = useState(false);
  const advanced = useRef(false);

  // Verification happens in the browser, so poll the backend; the moment the link is opened,
  // send the user to sign-in (email pre-filled) instead of making them navigate back manually.
  useEffect(() => {
    const interval = setInterval(async () => {
      if (advanced.current) return;
      if (await isEmailVerified(email)) {
        advanced.current = true;
        clearInterval(interval);
        navigation.replace('Login', { verifiedEmail: email });
      }
    }, 3000);
    return () => clearInterval(interval);
  }, [email, navigation]);

  async function handleResend() {
    setSending(true);
    setSent(false);
    try {
      await resendVerification(email);
      setSent(true);
    } finally {
      setSending(false);
    }
  }

  return (
    <View style={styles.container}>
      <View style={styles.card}>
        <Text style={styles.logo}>📬</Text>
        <Text style={styles.title}>Check your email</Text>
        <Text style={styles.body}>
          We sent a verification link to{'\n'}
          <Text style={styles.email}>{email}</Text>.
        </Text>
        <Text style={styles.body}>
          Open it to confirm your email — we&apos;ll take you to sign in automatically once you do.
        </Text>

        {sent && (
          <Text style={styles.sent}>✓ Sent! It can take a minute to arrive — check spam too.</Text>
        )}

        <TouchableOpacity
          style={[styles.button, sending && styles.buttonDisabled]}
          onPress={handleResend}
          disabled={sending}
          activeOpacity={0.8}
        >
          {sending
            ? <ActivityIndicator color="#fff" />
            : <Text style={styles.buttonText}>Resend link</Text>}
        </TouchableOpacity>

        <TouchableOpacity
          style={styles.linkRow}
          onPress={() => navigation.navigate('Login')}
        >
          <Text style={styles.linkText}>
            Already verified? <Text style={styles.linkAccent}>Back to login</Text>
          </Text>
        </TouchableOpacity>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f0fdf4', justifyContent: 'center', alignItems: 'center', padding: 24 },
  card: {
    width: '100%', maxWidth: 400, backgroundColor: '#ffffff', borderRadius: 16, padding: 32,
    shadowColor: '#000', shadowOffset: { width: 0, height: 2 }, shadowOpacity: 0.08, shadowRadius: 12, elevation: 4,
  },
  logo: { fontSize: 48, textAlign: 'center', marginBottom: 8 },
  title: { fontSize: 24, fontWeight: '700', color: '#14532d', textAlign: 'center', marginBottom: 16 },
  body: { fontSize: 14, color: '#374151', textAlign: 'center', lineHeight: 22, marginBottom: 12 },
  email: { fontWeight: '700', color: '#14532d' },
  sent: { fontSize: 13, color: '#16a34a', textAlign: 'center', marginTop: 4, marginBottom: 8 },
  button: { backgroundColor: '#16a34a', borderRadius: 10, paddingVertical: 14, alignItems: 'center', marginTop: 12 },
  buttonDisabled: { backgroundColor: '#86efac' },
  buttonText: { color: '#ffffff', fontSize: 16, fontWeight: '600' },
  linkRow: { marginTop: 20, alignItems: 'center' },
  linkText: { fontSize: 14, color: '#6b7280' },
  linkAccent: { color: '#16a34a', fontWeight: '700' },
});
