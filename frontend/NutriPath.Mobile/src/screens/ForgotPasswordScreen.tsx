import React, { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Pressable, Alert, KeyboardAvoidingView, Platform } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { TextField } from '@/components/TextField';
import { Button } from '@/components/Button';
import { colors, typography, spacing, radii } from '@/theme';
import * as authApi from '@/api/authApi';

interface ForgotPasswordScreenProps {
  onCodeSent: (email: string) => void;
  onGoBack: () => void;
}

export function ForgotPasswordScreen({ onCodeSent, onGoBack }: ForgotPasswordScreenProps) {
  const [email, setEmail] = useState('');
  const [codeRequested, setCodeRequested] = useState(false);
  const [resetCode, setResetCode] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');

  async function handleSendCode() {
    if (!email.trim()) {
      Alert.alert('Email required', 'Please enter your registered email.');
      return;
    }
    try {
      await authApi.forgotPassword(email);
      setCodeRequested(true);
    } catch {
      // Deliberately vague, matching the backend's own "don't reveal
      // whether an email exists" principle.
      setCodeRequested(true);
    }
  }

  async function handleUpdatePassword() {
    if (newPassword.length < 8) {
      Alert.alert('Password too short', 'Use at least 8 characters.');
      return;
    }
    if (newPassword !== confirmPassword) {
      Alert.alert("Passwords don't match", 'Please re-enter matching passwords.');
      return;
    }

    try {
      await authApi.resetPassword(email, resetCode, newPassword);
      onCodeSent(email);
    } catch (error: any) {
      const message = error.response?.data?.message ?? 'Could not reset password.';
      Alert.alert('Reset failed', message);
    }
  }

  return (
    <SafeAreaView style={styles.screen} edges={['top', 'bottom']}>
      <View style={styles.header}>
        <Pressable onPress={onGoBack} style={styles.backButton}>
          <MaterialCommunityIcons name="arrow-left" size={22} color={colors.onSurface} />
        </Pressable>
        <Text style={styles.headerTitle}>Reset your password</Text>
      </View>

      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={{ flex: 1 }}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          {!codeRequested ? (
            <>
              <Text style={styles.subtitle}>
                Enter the email linked with your NutriPath account and we'll send a password
                reset link or 6-digit code.
              </Text>
              <TextField
                label="Registered Email"
                icon="email-outline"
                placeholder="e.g. kavindi@university.ac.lk"
                keyboardType="email-address"
                value={email}
                onChangeText={setEmail}
              />
              <Button label="Send Reset Code" onPress={handleSendCode} style={{ marginTop: spacing.sm }} />
            </>
          ) : (
            <>
              <View style={styles.confirmBanner}>
                <MaterialCommunityIcons name="check-circle-outline" size={18} color={colors.primary} />
                <Text style={styles.confirmText}>
                  A reset code was sent to {email}. Enter your new password below.
                </Text>
              </View>
              <TextField
                label="Reset Code"
                icon="shield-key-outline"
                placeholder="6-digit code"
                keyboardType="number-pad"
                value={resetCode}
                onChangeText={setResetCode}
              />
              <TextField
                label="New Password"
                icon="lock-outline"
                placeholder="At least 8 characters"
                isPassword
                value={newPassword}
                onChangeText={setNewPassword}
              />
              <TextField
                label="Confirm New Password"
                icon="lock-outline"
                placeholder="Re-enter new password"
                isPassword
                value={confirmPassword}
                onChangeText={setConfirmPassword}
              />
              <Button label="Update Password & Log In" onPress={handleUpdatePassword} style={{ marginTop: spacing.sm }} />
            </>
          )}
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingHorizontal: spacing.margin,
    height: 56,
  },
  backButton: { width: 40, height: 40, alignItems: 'center', justifyContent: 'center' },
  headerTitle: { ...typography.headlineMd, fontSize: 16, color: colors.onSurface },
  content: { paddingHorizontal: spacing.margin, paddingTop: spacing.sm, gap: spacing.sm },
  subtitle: { ...typography.bodyMd, color: colors.onSurfaceVariant, marginBottom: spacing.xs },
  confirmBanner: {
    flexDirection: 'row',
    gap: 8,
    backgroundColor: colors.surfaceContainerLow,
    padding: 12,
    borderRadius: radii.md,
    alignItems: 'flex-start',
    marginBottom: spacing.xs,
  },
  confirmText: { ...typography.bodySm, color: colors.onSurfaceVariant, flex: 1 },
});