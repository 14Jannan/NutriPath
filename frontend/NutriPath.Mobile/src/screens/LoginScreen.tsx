import React, { useState } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  Pressable,
  Alert,
  KeyboardAvoidingView,
  Platform,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { TextField } from '@/components/TextField';
import { Button } from '@/components/Button';
import { colors, typography, spacing } from '@/theme';
import { useAuth } from '@/context/AuthContext';

interface LoginScreenProps {
  onGoToCreateAccount: () => void;
  onForgotPassword: () => void;
  onGoBack: () => void;
}

export function LoginScreen({
  onGoToCreateAccount,
  onForgotPassword,
  onGoBack,
}: LoginScreenProps) {
  const { loginUser } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit() {
    if (!email.trim() || !password) {
      Alert.alert('Missing information', 'Please enter your email and password.');
      return;
    }

    setIsSubmitting(true);
    try {
      await loginUser(email, password);
      // No manual navigation needed — RootNavigator watches isLoggedIn
      // and switches to Home automatically once this resolves.
    } catch (error: any) {
      const message = error.response?.data?.message ?? 'Login failed.';
      Alert.alert('Could not log in', message);
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <SafeAreaView style={styles.screen} edges={['top', 'bottom']}>
      <View style={styles.header}>
        <Pressable onPress={onGoBack} style={styles.backButton}>
          <MaterialCommunityIcons name="arrow-left" size={22} color={colors.onSurface} />
        </Pressable>
        <Text style={styles.headerTitle}>Log In</Text>
      </View>

      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        style={{ flex: 1 }}
      >
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <Text style={styles.title}>Welcome back</Text>
          <Text style={styles.subtitle}>
            Log in to continue tracking your mindful Sri Lankan meals.
          </Text>

          <TextField
            label="Email address"
            icon="email-outline"
            placeholder="e.g. kavindi@university.ac.lk"
            keyboardType="email-address"
            value={email}
            onChangeText={setEmail}
          />
          <TextField
            label="Password"
            icon="lock-outline"
            placeholder="Enter your password"
            isPassword
            value={password}
            onChangeText={setPassword}
          />

          <Pressable onPress={onForgotPassword} style={styles.forgotRow}>
            <Text style={styles.forgotText}>Forgot password?</Text>
          </Pressable>

          <View style={{ gap: spacing.sm, marginTop: spacing.sm }}>
            <Button label={isSubmitting ? 'Logging in...' : 'Log In'} onPress={handleSubmit} />
            <View style={styles.signUpRow}>
              <Text style={styles.signUpText}>Don't have an account? </Text>
              <Pressable onPress={onGoToCreateAccount}>
                <Text style={styles.signUpLink}>Create one</Text>
              </Pressable>
            </View>
          </View>
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
  content: { paddingHorizontal: spacing.margin, paddingBottom: spacing.lg, gap: spacing.sm },
  title: { ...typography.headlineMd, color: colors.onSurface, marginTop: spacing.xs },
  subtitle: { ...typography.bodyMd, color: colors.onSurfaceVariant, marginBottom: spacing.xs },
  forgotRow: { alignSelf: 'flex-end' },
  forgotText: { ...typography.labelMd, color: colors.primary },
  signUpRow: { flexDirection: 'row', justifyContent: 'center' },
  signUpText: { ...typography.bodyMd, color: colors.onSurfaceVariant },
  signUpLink: { ...typography.labelLg, color: colors.primary },
});
