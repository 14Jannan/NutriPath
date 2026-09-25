import React from 'react';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { LogScreen } from '@/screens/LogScreen';
import { FoodSearchScreen } from '@/screens/FoodSearchScreen';
import { FoodDetailScreen } from '@/screens/FoodDetailScreen';
import { AddFoodScreen } from '@/screens/AddFoodScreen';

export type LogStackParamList = {
  LogHome: undefined;
  // `date` is the local yyyy-MM-dd day being logged into, chosen on LogHome.
  FoodSearch: { mealType: string; date: string };
  FoodDetail: { foodId: string; mealType: string; date: string };
  // For foods the catalog doesn't have; `name` pre-fills from the search.
  AddFood: { mealType: string; date: string; name?: string };
};

const Stack = createNativeStackNavigator<LogStackParamList>();

export function LogStackNavigator() {
  return (
    <Stack.Navigator screenOptions={{ headerShown: false }}>
      <Stack.Screen name="LogHome" component={LogScreen} />
      <Stack.Screen name="FoodSearch" component={FoodSearchScreen} />
      <Stack.Screen name="FoodDetail" component={FoodDetailScreen} />
      <Stack.Screen name="AddFood" component={AddFoodScreen} />
    </Stack.Navigator>
  );
}
