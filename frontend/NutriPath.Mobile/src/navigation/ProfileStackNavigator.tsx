import React from 'react';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { ProfileScreen } from '@/screens/ProfileScreen';
import { GoalsSetupScreen } from '@/screens/GoalsSetupScreen';

export type ProfileStackParamList = {
  ProfileHome: undefined;
  GoalsSetup: undefined;
};

const Stack = createNativeStackNavigator<ProfileStackParamList>();

export function ProfileStackNavigator() {
  return (
    <Stack.Navigator screenOptions={{ headerShown: false }}>
      <Stack.Screen name="ProfileHome" component={ProfileScreen} />
      <Stack.Screen name="GoalsSetup" component={GoalsSetupScreen} />
    </Stack.Navigator>
  );
}
