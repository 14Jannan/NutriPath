import React from 'react';
import { NavigationContainer } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { WelcomeScreen } from '@/screens/WelcomeScreen';
import { CreateAccountScreen } from '@/screens/CreateAccountScreen';
import { LoginScreen } from '@/screens/LoginScreen';

export type RootStackParamList = {
  Welcome: undefined;
  CreateAccount: undefined;
  Login: undefined;
  VerifyOtp: undefined;   // built in the next screen batch
  ForgotPassword: undefined; // built in the next screen batch
};

const Stack = createNativeStackNavigator<RootStackParamList>();

export function RootNavigator() {
  return (
    <NavigationContainer>
      <Stack.Navigator screenOptions={{ headerShown: false }}>
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
              onAccountCreated={() => navigation.navigate('VerifyOtp')}
              onGoToLogin={() => navigation.navigate('Login')}
              onGoBack={() => navigation.goBack()}
            />
          )}
        </Stack.Screen>

        <Stack.Screen name="Login">
          {({ navigation }) => (
            <LoginScreen
              onLoggedIn={() => {
                /* Phase 5: navigate into the main tab bar instead */
              }}
              onGoToCreateAccount={() => navigation.navigate('CreateAccount')}
              onForgotPassword={() => navigation.navigate('ForgotPassword')}
              onGoBack={() => navigation.goBack()}
            />
          )}
        </Stack.Screen>
      </Stack.Navigator>
    </NavigationContainer>
  );
}