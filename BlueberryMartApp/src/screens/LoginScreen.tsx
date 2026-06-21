import React, { useState } from 'react';
import {
  ActivityIndicator,
  KeyboardAvoidingView,
  Platform,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from 'react-native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { Ionicons } from '@expo/vector-icons';
import { googleSignIn, GoogleCancelledError, login, WorkAccountError } from '../services/authService';
import type { RootStackParamList } from '../../App';

type Props = {
  navigation: NativeStackNavigationProp<RootStackParamList, 'Login'>;
};

export default function LoginScreen({ navigation }: Props) {
  const [email, setEmail]       = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading]   = useState(false);
  const [error, setError]       = useState<string | null>(null);

  async function handleLogin() {
    if (!email.trim() || !password.trim()) {
      setError('Email and password are required.');
      return;
    }

    setError(null);
    setLoading(true);

    try {
      const { role } = await login(email.trim().toLowerCase(), password);

      if (role === 'Shareholder') {
        navigation.replace('ShareholderTabs');
      } else {
        navigation.replace('CustomerTabs');
      }
    } catch (e) {
      setError(e instanceof WorkAccountError ? e.message : 'Invalid email or password.');
    } finally {
      setLoading(false);
    }
  }

  async function handleGoogleSignIn() {
    setError(null);
    setLoading(true);

    try {
      const { role } = await googleSignIn();
      navigation.replace(role === 'Shareholder' ? 'ShareholderTabs' : 'CustomerTabs');
    } catch (e) {
      if (e instanceof GoogleCancelledError) {
        // User dismissed the picker — not an error.
      } else if (e instanceof WorkAccountError) {
        setError(e.message);
      } else {
        setError('Could not sign in with Google. Please try again.');
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <KeyboardAvoidingView
      style={styles.container}
      behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
    >
      <View style={styles.card}>
        <Text style={styles.logo}>🫐</Text>
        <Text style={styles.title}>Blueberry Mart</Text>
        <Text style={styles.subtitle}>Sign in to continue</Text>

        <TextInput
          style={styles.input}
          placeholder="Email"
          placeholderTextColor="#9ca3af"
          autoCapitalize="none"
          keyboardType="email-address"
          value={email}
          onChangeText={setEmail}
          editable={!loading}
        />

        <TextInput
          style={styles.input}
          placeholder="Password"
          placeholderTextColor="#9ca3af"
          secureTextEntry
          value={password}
          onChangeText={setPassword}
          editable={!loading}
          onSubmitEditing={handleLogin}
        />

        {error && <Text style={styles.error}>{error}</Text>}

        <TouchableOpacity
          style={[styles.button, loading && styles.buttonDisabled]}
          onPress={handleLogin}
          disabled={loading}
          activeOpacity={0.8}
        >
          {loading
            ? <ActivityIndicator color="#fff" />
            : <Text style={styles.buttonText}>Sign In</Text>}
        </TouchableOpacity>

        <View style={styles.dividerRow}>
          <View style={styles.divider} />
          <Text style={styles.dividerText}>or</Text>
          <View style={styles.divider} />
        </View>

        <TouchableOpacity
          style={[styles.googleButton, loading && styles.googleButtonDisabled]}
          onPress={handleGoogleSignIn}
          disabled={loading}
          activeOpacity={0.8}
        >
          <Ionicons name="logo-google" size={18} color="#4285F4" />
          <Text style={styles.googleButtonText}>Continue with Google</Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={styles.linkRow}
          onPress={() => navigation.navigate('Register')}
          disabled={loading}
        >
          <Text style={styles.linkText}>
            Don&apos;t have an account? <Text style={styles.linkAccent}>Sign up</Text>
          </Text>
        </TouchableOpacity>
      </View>
    </KeyboardAvoidingView>
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
  card: {
    width: '100%',
    maxWidth: 400,
    backgroundColor: '#ffffff',
    borderRadius: 16,
    padding: 32,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.08,
    shadowRadius: 12,
    elevation: 4,
  },
  logo: {
    fontSize: 48,
    textAlign: 'center',
    marginBottom: 8,
  },
  title: {
    fontSize: 26,
    fontWeight: '700',
    color: '#14532d',
    textAlign: 'center',
  },
  subtitle: {
    fontSize: 14,
    color: '#6b7280',
    textAlign: 'center',
    marginBottom: 28,
    marginTop: 4,
  },
  input: {
    borderWidth: 1,
    borderColor: '#d1fae5',
    borderRadius: 10,
    paddingHorizontal: 16,
    paddingVertical: 12,
    fontSize: 15,
    color: '#111827',
    backgroundColor: '#f9fafb',
    marginBottom: 14,
  },
  error: {
    color: '#dc2626',
    fontSize: 13,
    marginBottom: 12,
    textAlign: 'center',
  },
  button: {
    backgroundColor: '#16a34a',
    borderRadius: 10,
    paddingVertical: 14,
    alignItems: 'center',
    marginTop: 4,
  },
  buttonDisabled: {
    backgroundColor: '#86efac',
  },
  buttonText: {
    color: '#ffffff',
    fontSize: 16,
    fontWeight: '600',
  },
  dividerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginTop: 18,
    marginBottom: 14,
  },
  divider: {
    flex: 1,
    height: 1,
    backgroundColor: '#e5e7eb',
  },
  dividerText: {
    marginHorizontal: 12,
    color: '#9ca3af',
    fontSize: 13,
  },
  googleButton: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 10,
    backgroundColor: '#ffffff',
    borderWidth: 1,
    borderColor: '#d1d5db',
    borderRadius: 10,
    paddingVertical: 13,
  },
  googleButtonDisabled: {
    opacity: 0.6,
  },
  googleButtonText: {
    color: '#374151',
    fontSize: 15,
    fontWeight: '600',
  },
  linkRow: {
    marginTop: 20,
    alignItems: 'center',
  },
  linkText: {
    fontSize: 14,
    color: '#6b7280',
  },
  linkAccent: {
    color: '#16a34a',
    fontWeight: '700',
  },
});
