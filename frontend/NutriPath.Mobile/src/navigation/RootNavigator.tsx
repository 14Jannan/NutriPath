import React from 'react';
import { View, Text, ActivityIndicator, StyleSheet } from 'react-native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { NavigationContainer } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { WelcomeScreen } from '@/screens/WelcomeScreen';
import { CreateAccountScreen } from '@/screens/CreateAccountScreen';
import { LoginScreen } from '@/screens/LoginScreen';
import { VerifyOtpScreen } from '@/screens/VerifyOtpScreen';
import { ForgotPasswordScreen } from '@/screens/ForgotPasswordScreen';
import { MainTabNavigator } from '@/navigation/MainTabNavigator';
import { OnboardingScreen } from '@/screens/OnboardingScreen';
import { Button } from '@/components/Button';
import { useAuth } from '@/context/AuthContext';
import { colors, spacing, typography } from '@/theme';

export type RootStackParamList = {
  Welcome: undefined;
  CreateAccount: undefined;
  Login: undefined;
  VerifyOtp: { email: string };
  ForgotPassword: undefined;
  Onboarding: undefined;
  Home: undefined;
};

const Stack = createNativeStackNavigator<RootStackParamList>();

/** Shown if we couldn't check whether setup is finished (e.g. offline). */
function ProfileCheckFailed() {
  const { refreshProfileStatus, logoutUser } = useAuth();
  return (
    <View style={styles.center}>
      <MaterialCommunityIcons name="wifi-off" size={40} color={colors.outline} />
      <Text style={styles.title}>Can't reach NutriPath</Text>
      <Text style={styles.body}>Check your connection and that the backend is running, then try again.</Text>
      <Button label="Try again" onPress={refreshProfileStatus} style={{ marginTop: spacing.md, maxWidth: 320 }} />
      <Button label="Log out" variant="secondary" onPress={logoutUser} style={{ marginTop: spacing.sm, maxWidth: 320 }} />
    </View>
  );
}

export function RootNavigator() {
  const { isLoggedIn, isLoading, profileStatus } = useAuth();

  if (isLoading || (isLoggedIn && profileStatus === 'checking')) {
    // Brief moment while we check for a saved session and, once logged
    // in, whether the required profile setup has been completed.
    return (
      <View style={styles.center}>
        <ActivityIndicator color={colors.primary} size="large" /> 
      </View>
    );
  }

  if (isLoggedIn && profileStatus === 'error') {
    return <ProfileCheckFailed />;
  }

  return (
    <NavigationContainer>
      <Stack.Navigator screenOptions={{ headerShown: false }}>
        {isLoggedIn ? (
          // Setup is mandatory: until the profile is saved, the setup
          // wizard is the only screen, so there's nothing to skip to.
          profileStatus === 'incomplete' ? (
            <Stack.Screen name="Onboarding" component={OnboardingScreen} />
          ) : (
            <Stack.Screen name="Home" component={MainTabNavigator} />
          )
        ) : (
          <>
            <Stack.Screen name="Welcome">
              {({ navigation }) => (
                <WelcomeScreen
                  onGetStarted={() => navigation.navigate('CreateAccount')}
                  onLogIn={() => navigation.navigate('Login')}
                />
              )}
            </Stack.Screen>
            <Stack.Screen name="CreateAccount">
              {({ navigation }) => (
                <CreateAccountScreen
                  onAccountCreated={(email) => navigation.navigate('VerifyOtp', { email })}
                  onGoToLogin={() => navigation.navigate('Login')}
                  onGoBack={() => navigation.goBack()}
                />
              )}
            </Stack.Screen>
            <Stack.Screen name="Login">
              {({ navigation }) => (
                <LoginScreen
                  onGoToCreateAccount={() => navigation.navigate('CreateAccount')}
                  onForgotPassword={() => navigation.navigate('ForgotPassword')}
                  onGoBack={() => navigation.goBack()}
                />
              )}
            </Stack.Screen>
            <Stack.Screen name="VerifyOtp">
              {({ navigation, route }) => (
                <VerifyOtpScreen
                  email={route.params.email}
                  onVerified={() => navigation.navigate('Login')}
                  onGoBack={() => navigation.goBack()}
                />
              )}
            </Stack.Screen>
            <Stack.Screen name="ForgotPassword">
              {({ navigation }) => (
                <ForgotPasswordScreen
                  onCodeSent={() => navigation.navigate('Login')}
                  onGoBack={() => navigation.goBack()}
                />
              )}
            </Stack.Screen>
          </>
        )}
      </Stack.Navigator>
    </NavigationContainer>
  );
}

const styles = StyleSheet.create({
  center: {
    flex: 1,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
    padding: spacing.margin,
    gap: spacing.xs,
  },
  title: { ...typography.headlineMd, color: colors.onSurface, textAlign: 'center', marginTop: spacing.sm },
  body: { ...typography.bodyMd, color: colors.onSurfaceVariant, textAlign: 'center', maxWidth: 360 },
});
