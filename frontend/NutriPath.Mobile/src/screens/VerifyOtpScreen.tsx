import React, { useEffect, useRef, useState } from 'react';
import { View, Text, StyleSheet, TextInput, Pressable } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Button } from '@/components/Button';
import { colors, typography, spacing, radii } from '@/theme';
import { showAlert } from '@/utils/alert';
import * as authApi from '@/api/authApi';

interface VerifyOtpScreenProps {
  email: string;
  onVerified: () => void;
  onGoBack: () => void;
}

const CODE_LENGTH = 6;

export function VerifyOtpScreen({ email, onVerified, onGoBack }: VerifyOtpScreenProps) {
  const [digits, setDigits] = useState<string[]>(Array(CODE_LENGTH).fill(''));
  const [secondsLeft, setSecondsLeft] = useState(120);
  const inputRefs = useRef<Array<TextInput | null>>([]);

  useEffect(() => {
    if (secondsLeft <= 0) return;
    const timer = setTimeout(() => setSecondsLeft((s) => s - 1), 1000);
    return () => clearTimeout(timer);
  }, [secondsLeft]);

  function handleChange(text: string, index: number) {
    // Only accept a single digit per box.
    const clean = text.replace(/[^0-9]/g, '').slice(-1);
    const next = [...digits];
    next[index] = clean;
    setDigits(next);

    if (clean && index < CODE_LENGTH - 1) {
      inputRefs.current[index + 1]?.focus();
    }
  }

  function handleKeyPress(key: string, index: number) {
    if (key === 'Backspace' && !digits[index] && index > 0) {
      inputRefs.current[index - 1]?.focus();
    }
  }

  async function handleVerify() {
    const code = digits.join('');
    if (code.length !== CODE_LENGTH) {
      showAlert('Incomplete code', 'Please enter all 6 digits.');
      return;
    }

    try {
      await authApi.verifyOtp(email, code);
      onVerified();
    } catch (error: any) {
      const message = error.response?.data?.message ?? 'Verification failed.';
      showAlert('Invalid code', message);
    }
  }

  function handleResend() {
    setSecondsLeft(120);
    setDigits(Array(CODE_LENGTH).fill(''));
    inputRefs.current[0]?.focus();
    // A dedicated resend-code endpoint doesn't exist yet — flagging the
    // gap rather than silently pretending this works.
    showAlert('Resend not yet implemented', 'A dedicated resend-code endpoint is a good next addition.');
  }

  const minutes = Math.floor(secondsLeft / 60);
  const seconds = String(secondsLeft % 60).padStart(2, '0');

  return (
    <SafeAreaView style={styles.screen} edges={['top', 'bottom']}>
      <View style={styles.header}>
        <Pressable onPress={onGoBack} style={styles.backButton}>
          <MaterialCommunityIcons name="arrow-left" size={22} color={colors.onSurface} />
        </Pressable>
        <Text style={styles.headerTitle}>Auth Onboarding · Step 2</Text>
      </View>

      <View style={styles.content}>
        <View style={styles.progressBarTrack}>
          <View style={[styles.progressBarFill, { width: '66%' }]} />
        </View>

        <View style={styles.iconCircle}>
          <MaterialCommunityIcons name="shield-check-outline" size={28} color={colors.primary} />
        </View>
        <Text style={styles.title}>Verify your email</Text>
        <Text style={styles.subtitle}>
          We just sent a 6-digit verification code to{'\n'}
          <Text style={styles.emailText}>{email || 'your email'}</Text>
        </Text>

        <View style={styles.codeRow}>
          {digits.map((digit, index) => (
            <TextInput
              key={index}
              ref={(ref) => { inputRefs.current[index] = ref; }}
              style={[styles.codeBox, digit && styles.codeBoxFilled]}
              value={digit}
              onChangeText={(text) => handleChange(text, index)}
              onKeyPress={({ nativeEvent }) => handleKeyPress(nativeEvent.key, index)}
              keyboardType="number-pad"
              maxLength={1}
              textAlign="center"
            />
          ))}
        </View>

        <Pressable onPress={handleResend} disabled={secondsLeft > 0} style={styles.resendRow}>
          <Text style={styles.resendText}>
            {secondsLeft > 0 ? (
              <>Didn't receive the code? Resend in {minutes}:{seconds}</>
            ) : (
              <Text style={styles.resendLink}>Resend code</Text>
            )}
          </Text>
        </Pressable>

        <View style={styles.securityNote}>
          <MaterialCommunityIcons name="lock-check-outline" size={16} color={colors.secondary} />
          <Text style={styles.securityText}>
            We use university authentication to securely safeguard your daily meal logs,
            micronutrient metrics, and health goals.
          </Text>
        </View>

        <Button label="Verify & Continue" onPress={handleVerify} style={{ marginTop: spacing.md }} />
      </View>
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
  content: { flex: 1, paddingHorizontal: spacing.margin, alignItems: 'center' },
  progressBarTrack: { width: '100%', height: 6, backgroundColor: colors.surfaceContainer, borderRadius: 3, marginBottom: spacing.lg },
  progressBarFill: { height: 6, backgroundColor: colors.primary, borderRadius: 3 },
  iconCircle: {
    width: 64,
    height: 64,
    borderRadius: 32,
    backgroundColor: colors.surfaceContainer,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.sm,
  },
  title: { ...typography.headlineLg, color: colors.onSurface, marginBottom: 6 },
  subtitle: { ...typography.bodyMd, color: colors.onSurfaceVariant, textAlign: 'center', marginBottom: spacing.lg },
  emailText: { color: colors.primary, fontWeight: '600' },
  codeRow: { flexDirection: 'row', gap: 8, marginBottom: spacing.md },
  codeBox: {
    width: 44,
    height: 52,
    borderRadius: radii.md,
    backgroundColor: colors.surfaceContainerLowest,
    ...typography.headlineMd,
    color: colors.onSurface,
  },
  codeBoxFilled: { backgroundColor: colors.surfaceContainer },
  resendRow: { marginBottom: spacing.lg },
  resendText: { ...typography.bodySm, color: colors.onSurfaceVariant, textAlign: 'center' },
  resendLink: { color: colors.primary, fontWeight: '600' },
  securityNote: {
    flexDirection: 'row',
    gap: 8,
    backgroundColor: colors.surfaceContainerLow,
    padding: 12,
    borderRadius: radii.md,
    alignItems: 'flex-start',
  },
  securityText: { ...typography.bodySm, color: colors.onSurfaceVariant, flex: 1 },
});