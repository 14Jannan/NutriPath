import React from 'react';
import { View, Text } from 'react-native';
import { NavigationContainer } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { WelcomeScreen } from '@/screens/WelcomeScreen';
import { CreateAccountScreen } from '@/screens/CreateAccountScreen';
import { LoginScreen } from '@/screens/LoginScreen';
import { VerifyOtpScreen } from '@/screens/VerifyOtpScreen';
import { ForgotPasswordScreen } from '@/screens/ForgotPasswordScreen';
import { HomeScreen } from '@/screens/HomeScreen';
import { useAuth } from '@/context/AuthContext';
import { colors } from '@/theme';

export type RootStackParamList = {
  Welcome: undefined;
  CreateAccount: undefined;
  Login: undefined;
  VerifyOtp: { email: string };
  ForgotPassword: undefined;
  Home: undefined;
};

const Stack = createNativeStackNavigator<RootStackParamList>();

export function RootNavigator() {
  const { isLoggedIn, isLoading } = useAuth();

  if (isLoading) {
    // Brief moment while we check SecureStore for an existing session.
    return <View style={{ flex: 1, backgroundColor: colors.surface }} />;
  }

  return (
    <NavigationContainer>
      <Stack.Navigator screenOptions={{ headerShown: false }}>
        {isLoggedIn ? (
          <Stack.Screen name="Home" component={HomeScreen} />
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