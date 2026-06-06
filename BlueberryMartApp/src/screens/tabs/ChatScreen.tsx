import React, { useRef, useState } from 'react';
import {
  ActivityIndicator, FlatList, KeyboardAvoidingView, Platform,
  StyleSheet, Text, TextInput, TouchableOpacity, View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import AppHeader from '../../components/AppHeader';
import { ChatTurn, sendChat } from '../../services/chatService';

const SUGGESTIONS = [
  'Is Brown Eggs in stock?',
  'How do I pay for an order?',
  'What is bulk ordering?',
];

export default function ChatScreen() {
  const insets = useSafeAreaInsets();
  const [messages, setMessages] = useState<ChatTurn[]>([]);
  const [input, setInput] = useState('');
  const [sending, setSending] = useState(false);
  const listRef = useRef<FlatList>(null);

  async function send(text?: string) {
    const content = (text ?? input).trim();
    if (!content || sending) return;
    const next: ChatTurn[] = [...messages, { role: 'user', content }];
    setMessages(next);
    setInput('');
    setSending(true);
    try {
      const res = await sendChat(next);
      setMessages(m => [...m, { role: 'assistant', content: res.reply }]);
    } catch (e: any) {
      setMessages(m => [...m, { role: 'assistant', content: e?.message ?? 'Sorry, something went wrong.' }]);
    } finally {
      setSending(false);
    }
  }

  return (
    <View style={styles.container}>
      <AppHeader />
      <KeyboardAvoidingView
        style={styles.flex}
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        keyboardVerticalOffset={Platform.OS === 'ios' ? insets.top + 56 : 0}
      >
        {messages.length === 0 ? (
          <View style={styles.welcome}>
            <View style={styles.botBadge}><Ionicons name="chatbubble-ellipses" size={26} color="#16a34a" /></View>
            <Text style={styles.welcomeTitle}>Shopping assistant</Text>
            <Text style={styles.welcomeSub}>Ask about items, availability, or help with an order.</Text>
            <View style={styles.chips}>
              {SUGGESTIONS.map(s => (
                <TouchableOpacity key={s} style={styles.chip} onPress={() => send(s)} activeOpacity={0.8}>
                  <Text style={styles.chipText}>{s}</Text>
                </TouchableOpacity>
              ))}
            </View>
          </View>
        ) : (
          <FlatList
            ref={listRef}
            data={messages}
            keyExtractor={(_, i) => String(i)}
            contentContainerStyle={styles.list}
            onContentSizeChange={() => listRef.current?.scrollToEnd({ animated: true })}
            renderItem={({ item }) => (
              <View style={[styles.bubble, item.role === 'user' ? styles.userBubble : styles.botBubble]}>
                <Text style={item.role === 'user' ? styles.userText : styles.botText}>{item.content}</Text>
              </View>
            )}
          />
        )}

        {sending && (
          <View style={styles.typing}>
            <ActivityIndicator size="small" color="#9ca3af" />
            <Text style={styles.typingText}>Assistant is typing…</Text>
          </View>
        )}

        <View style={[styles.inputRow, { paddingBottom: 10 }]}>
          <TextInput
            style={styles.input}
            value={input}
            onChangeText={setInput}
            placeholder="Ask about items or an order…"
            placeholderTextColor="#9ca3af"
            multiline
            returnKeyType="send"
            onSubmitEditing={() => send()}
            blurOnSubmit
          />
          <TouchableOpacity
            style={[styles.sendBtn, (!input.trim() || sending) && styles.sendBtnDisabled]}
            onPress={() => send()}
            disabled={!input.trim() || sending}
            activeOpacity={0.85}
          >
            <Ionicons name="arrow-up" size={20} color="#fff" />
          </TouchableOpacity>
        </View>
      </KeyboardAvoidingView>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f9fafb' },
  flex: { flex: 1 },
  welcome: { flex: 1, alignItems: 'center', justifyContent: 'center', paddingHorizontal: 32 },
  botBadge: { width: 56, height: 56, borderRadius: 28, backgroundColor: '#f0fdf4', justifyContent: 'center', alignItems: 'center', marginBottom: 14 },
  welcomeTitle: { fontSize: 18, fontWeight: '700', color: '#111827', marginBottom: 6 },
  welcomeSub: { fontSize: 13, color: '#6b7280', textAlign: 'center', marginBottom: 20, lineHeight: 19 },
  chips: { gap: 10, alignSelf: 'stretch' },
  chip: { backgroundColor: '#ffffff', borderWidth: 1, borderColor: '#e5e7eb', borderRadius: 12, paddingVertical: 12, paddingHorizontal: 16 },
  chipText: { fontSize: 14, color: '#374151', fontWeight: '600' },

  list: { padding: 16, gap: 10 },
  bubble: { maxWidth: '82%', borderRadius: 16, paddingVertical: 10, paddingHorizontal: 14 },
  userBubble: { alignSelf: 'flex-end', backgroundColor: '#14532d', borderBottomRightRadius: 4 },
  botBubble: { alignSelf: 'flex-start', backgroundColor: '#ffffff', borderBottomLeftRadius: 4, borderWidth: 1, borderColor: '#f3f4f6' },
  userText: { color: '#ffffff', fontSize: 14.5, lineHeight: 20 },
  botText: { color: '#111827', fontSize: 14.5, lineHeight: 20 },

  typing: { flexDirection: 'row', alignItems: 'center', gap: 8, paddingHorizontal: 18, paddingBottom: 6 },
  typingText: { fontSize: 12, color: '#9ca3af' },

  inputRow: { flexDirection: 'row', alignItems: 'flex-end', gap: 8, paddingHorizontal: 16, paddingTop: 8, borderTopWidth: 1, borderTopColor: '#f3f4f6', backgroundColor: '#ffffff' },
  input: {
    flex: 1, maxHeight: 110, backgroundColor: '#f3f4f6', borderRadius: 20,
    paddingHorizontal: 16, paddingTop: 10, paddingBottom: 10, fontSize: 15, color: '#111827',
  },
  sendBtn: { width: 40, height: 40, borderRadius: 20, backgroundColor: '#16a34a', justifyContent: 'center', alignItems: 'center' },
  sendBtnDisabled: { backgroundColor: '#86efac' },
});
