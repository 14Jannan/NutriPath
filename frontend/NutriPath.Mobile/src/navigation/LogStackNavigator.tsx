import React from 'react';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { LogScreen } from '@/screens/LogScreen';
import { FoodSearchScreen } from '@/screens/FoodSearchScreen';
import { FoodDetailScreen } from '@/screens/FoodDetailScreen';

export type LogStackParamList = {
  LogHome: undefined;
  FoodSearch: { mealType: string };
  FoodDetail: { foodId: string; mealType: string };
};

const Stack = createNativeStackNavigator<LogStackParamList>();

export function LogStackNavigator() {
  return (
    <Stack.Navigator screenOptions={{ headerShown: false }}>
      <Stack.Screen name="LogHome" component={LogScreen} />
      <Stack.Screen name="FoodSearch" component={FoodSearchScreen} />
      <Stack.Screen name="FoodDetail" component={FoodDetailScreen} />
    </Stack.Navigator>
  );
}
