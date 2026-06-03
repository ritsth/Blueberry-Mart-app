import React, { useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  Image,
  KeyboardAvoidingView,
  Platform,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from 'react-native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { RouteProp } from '@react-navigation/native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import * as ImagePicker from 'expo-image-picker';
import { getStoredToken } from '../services/authService';
import type { RootStackParamList } from '../../App';

type Props = {
  navigation: NativeStackNavigationProp<RootStackParamList, 'ReviewScreen'>;
  route:      RouteProp<RootStackParamList, 'ReviewScreen'>;
};

const API_BASE = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5027';

export default function ReviewScreen({ navigation, route }: Props) {
  const insets = useSafeAreaInsets();
  const { orderId, items } = route.params;

  const [selectedItemId, setSelectedItemId] = useState(items[0]?.id ?? '');
  const [rating, setRating]                 = useState(0);
  const [comment, setComment]               = useState('');
  const [photo, setPhoto]                   = useState<ImagePicker.ImagePickerAsset | null>(null);
  const [submitting, setSubmitting]         = useState(false);
  const [submitted, setSubmitted]           = useState(false);
  const [pointsEarned, setPointsEarned]     = useState(0);

  async function pickFromLibrary() {
    const { status } = await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (status !== 'granted') {
      Alert.alert('Permission needed', 'Please allow access to your photo library.');
      return;
    }
    const result = await ImagePicker.launchImageLibraryAsync({
      mediaTypes: ['images'],
      allowsEditing: true,
      quality: 0.8,
    });
    if (!result.canceled) setPhoto(result.assets[0]);
  }

  async function takePhoto() {
    const { status } = await ImagePicker.requestCameraPermissionsAsync();
    if (status !== 'granted') {
      Alert.alert('Permission needed', 'Please allow camera access.');
      return;
    }
    const result = await ImagePicker.launchCameraAsync({
      allowsEditing: true,
      quality: 0.8,
    });
    if (!result.canceled) setPhoto(result.assets[0]);
  }

  async function submitReview() {
    if (rating === 0) {
      Alert.alert('Rating required', 'Please select a star rating.');
      return;
    }
    if (!comment.trim()) {
      Alert.alert('Comment required', 'Please write a comment.');
      return;
    }

    setSubmitting(true);
    try {
      const token = await getStoredToken();
      const formData = new FormData();
      formData.append('orderId', orderId);
      formData.append('itemId', selectedItemId);
      formData.append('rating', String(rating));
      formData.append('comment', comment.trim());

      if (photo) {
        const ext      = photo.uri.split('.').pop() ?? 'jpg';
        const mimeType = photo.mimeType ?? `image/${ext}`;
        formData.append('image', {
          uri:  photo.uri,
          type: mimeType,
          name: photo.fileName ?? `review.${ext}`,
        } as any);
      }

      const res = await fetch(`${API_BASE}/api/reviews`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
        body: formData,
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        Alert.alert('Submission Failed', body.message ?? 'Something went wrong.');
        return;
      }

      const data = await res.json();
      setPointsEarned(data.loyaltyPointsEarned);
      setSubmitted(true);
    } catch {
      Alert.alert('Error', 'Could not submit review. Check your connection.');
    } finally {
      setSubmitting(false);
    }
  }

  if (submitted) {
    return (
      <View style={styles.successContainer}>
        <Text style={styles.successIcon}>⭐</Text>
        <Text style={styles.successTitle}>Review Submitted!</Text>
        <Text style={styles.successSub}>
          You earned <Text style={styles.successPoints}>{pointsEarned} loyalty points</Text>
          {photo ? ' for your photo review.' : ' for your review.'}
        </Text>
        <TouchableOpacity style={styles.doneButton} onPress={() => navigation.goBack()}>
          <Text style={styles.doneButtonText}>Done</Text>
        </TouchableOpacity>
      </View>
    );
  }

  return (
    <KeyboardAvoidingView
      style={{ flex: 1 }}
      behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
    >
      <ScrollView style={styles.container} contentContainerStyle={[styles.content, { paddingTop: insets.top + 12 }]} keyboardShouldPersistTaps="handled">

        {/* Header */}
        <TouchableOpacity onPress={() => navigation.goBack()} style={styles.backRow}>
          <Text style={styles.backText}>← Back</Text>
        </TouchableOpacity>
        <Text style={styles.heading}>Write a Review</Text>
        <Text style={styles.subheading}>Share your experience with this product</Text>

        {/* Item selector */}
        {items.length > 1 && (
          <View style={styles.field}>
            <Text style={styles.label}>Select Item</Text>
            {items.map(item => (
              <TouchableOpacity
                key={item.id}
                style={[styles.itemOption, selectedItemId === item.id && styles.itemOptionSelected]}
                onPress={() => setSelectedItemId(item.id)}
                activeOpacity={0.8}
              >
                <Text style={[styles.itemOptionText, selectedItemId === item.id && styles.itemOptionTextSelected]}>
                  {item.name}
                </Text>
                {selectedItemId === item.id && <Text style={styles.checkmark}>✓</Text>}
              </TouchableOpacity>
            ))}
          </View>
        )}

        {items.length === 1 && (
          <View style={styles.singleItem}>
            <Text style={styles.singleItemLabel}>Reviewing</Text>
            <Text style={styles.singleItemName}>{items[0].name}</Text>
          </View>
        )}

        {/* Star rating */}
        <View style={styles.field}>
          <Text style={styles.label}>Rating</Text>
          <View style={styles.starsRow}>
            {[1, 2, 3, 4, 5].map(star => (
              <TouchableOpacity key={star} onPress={() => setRating(star)} activeOpacity={0.7}>
                <Text style={[styles.star, star <= rating && styles.starFilled]}>★</Text>
              </TouchableOpacity>
            ))}
          </View>
        </View>

        {/* Comment */}
        <View style={styles.field}>
          <Text style={styles.label}>Comment</Text>
          <TextInput
            style={styles.commentInput}
            placeholder="What did you think about this item?"
            placeholderTextColor="#9ca3af"
            multiline
            numberOfLines={4}
            value={comment}
            onChangeText={setComment}
            textAlignVertical="top"
          />
        </View>

        {/* Photo */}
        <View style={styles.field}>
          <Text style={styles.label}>Photo <Text style={styles.optional}>(optional · +10 bonus points)</Text></Text>
          <View style={styles.photoRow}>
            <TouchableOpacity style={styles.photoBtn} onPress={pickFromLibrary} activeOpacity={0.8}>
              <Text style={styles.photoBtnText}>📷  Choose Photo</Text>
            </TouchableOpacity>
            <TouchableOpacity style={styles.photoBtn} onPress={takePhoto} activeOpacity={0.8}>
              <Text style={styles.photoBtnText}>📸  Take Photo</Text>
            </TouchableOpacity>
          </View>
          {photo && (
            <View style={styles.previewWrapper}>
              <Image source={{ uri: photo.uri }} style={styles.preview} />
              <TouchableOpacity style={styles.removePhoto} onPress={() => setPhoto(null)}>
                <Text style={styles.removePhotoText}>✕ Remove</Text>
              </TouchableOpacity>
            </View>
          )}
        </View>

        {/* Submit */}
        <TouchableOpacity
          style={[styles.submitButton, submitting && styles.submitButtonDisabled]}
          onPress={submitReview}
          disabled={submitting}
          activeOpacity={0.8}
        >
          {submitting
            ? <ActivityIndicator color="#fff" />
            : <Text style={styles.submitButtonText}>Submit Review</Text>}
        </TouchableOpacity>

      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#f0fdf4',
  },
  content: {
    paddingHorizontal: 24,
    paddingBottom: 48,
  },
  backRow: { marginBottom: 20 },
  backText: { color: '#16a34a', fontWeight: '600', fontSize: 14 },
  heading: { fontSize: 24, fontWeight: '700', color: '#14532d', marginBottom: 4 },
  subheading: { fontSize: 13, color: '#6b7280', marginBottom: 28 },
  field: { marginBottom: 24 },
  label: { fontSize: 14, fontWeight: '600', color: '#374151', marginBottom: 10 },
  optional: { fontSize: 12, fontWeight: '400', color: '#9ca3af' },
  // Item selector
  singleItem: {
    backgroundColor: '#ffffff',
    borderRadius: 10,
    padding: 14,
    marginBottom: 24,
    borderWidth: 1,
    borderColor: '#bbf7d0',
  },
  singleItemLabel: { fontSize: 11, color: '#6b7280', marginBottom: 2 },
  singleItemName: { fontSize: 15, fontWeight: '600', color: '#14532d' },
  itemOption: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    backgroundColor: '#ffffff',
    borderRadius: 10,
    padding: 14,
    marginBottom: 8,
    borderWidth: 1,
    borderColor: '#e5e7eb',
  },
  itemOptionSelected: { borderColor: '#16a34a', backgroundColor: '#f0fdf4' },
  itemOptionText: { fontSize: 14, color: '#374151' },
  itemOptionTextSelected: { fontWeight: '600', color: '#14532d' },
  checkmark: { color: '#16a34a', fontWeight: '700', fontSize: 16 },
  // Stars
  starsRow: { flexDirection: 'row', gap: 8 },
  star: { fontSize: 38, color: '#d1d5db' },
  starFilled: { color: '#f59e0b' },
  // Comment
  commentInput: {
    backgroundColor: '#ffffff',
    borderWidth: 1,
    borderColor: '#d1fae5',
    borderRadius: 10,
    padding: 14,
    fontSize: 14,
    color: '#111827',
    minHeight: 110,
  },
  // Photo
  photoRow: { flexDirection: 'row', gap: 10 },
  photoBtn: {
    flex: 1,
    backgroundColor: '#ffffff',
    borderWidth: 1,
    borderColor: '#d1fae5',
    borderRadius: 10,
    paddingVertical: 12,
    alignItems: 'center',
  },
  photoBtnText: { fontSize: 13, fontWeight: '600', color: '#374151' },
  previewWrapper: { marginTop: 12, alignItems: 'flex-start' },
  preview: {
    width: '100%',
    height: 200,
    borderRadius: 10,
    backgroundColor: '#e5e7eb',
  },
  removePhoto: { marginTop: 8 },
  removePhotoText: { fontSize: 13, color: '#dc2626', fontWeight: '600' },
  // Submit
  submitButton: {
    backgroundColor: '#16a34a',
    borderRadius: 12,
    paddingVertical: 16,
    alignItems: 'center',
    marginTop: 8,
  },
  submitButtonDisabled: { backgroundColor: '#86efac' },
  submitButtonText: { color: '#ffffff', fontSize: 16, fontWeight: '700' },
  // Success
  successContainer: {
    flex: 1,
    backgroundColor: '#f0fdf4',
    justifyContent: 'center',
    alignItems: 'center',
    padding: 32,
  },
  successIcon: { fontSize: 64, marginBottom: 16 },
  successTitle: { fontSize: 26, fontWeight: '700', color: '#14532d', marginBottom: 12 },
  successSub: { fontSize: 15, color: '#374151', textAlign: 'center', lineHeight: 22, marginBottom: 32 },
  successPoints: { fontWeight: '700', color: '#16a34a' },
  doneButton: {
    backgroundColor: '#16a34a',
    borderRadius: 12,
    paddingVertical: 14,
    paddingHorizontal: 48,
  },
  doneButtonText: { color: '#ffffff', fontSize: 16, fontWeight: '700' },
});
