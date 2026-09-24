import React from 'react';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { TodayDashboardScreen } from '@/screens/TodayDashboardScreen';
import { LogStackNavigator } from '@/navigation/LogStackNavigator';
import { WeeklyScoreScreen } from '@/screens/WeeklyScoreScreen';
import { AssistantScreen } from '@/screens/AssistantScreen';
import { ProfileScreen } from '@/screens/ProfileScreen';
import { colors } from '@/theme';

export type MainTabParamList = {
  Today: undefined;
  Log: undefined;
  Score: undefined;
  Assistant: undefined;
  Profile: undefined;
};

const Tab = createBottomTabNavigator<MainTabParamList>();

const ICONS: Record<keyof MainTabParamList, keyof typeof MaterialCommunityIcons.glyphMap> = {
  Today: 'calendar-today',
  Log: 'book-open-variant',
  Score: 'chart-arc',
  Assistant: 'robot-happy-outline',
  Profile: 'account-circle-outline',
};

export function MainTabNavigator() {
  return (
    <Tab.Navigator
      screenOptions={({ route }) => ({
        headerShown: false,
        tabBarActiveTintColor: colors.primary,
        tabBarInactiveTintColor: colors.outline,
        tabBarIcon: ({ color, size }) => (
          <MaterialCommunityIcons name={ICONS[route.name]} size={size} color={color} />
        ),
      })}
    >
      <Tab.Screen name="Today" component={TodayDashboardScreen} />
      <Tab.Screen name="Log" component={LogStackNavigator} />
      <Tab.Screen name="Score" component={WeeklyScoreScreen} />
      <Tab.Screen name="Assistant" component={AssistantScreen} />
      <Tab.Screen name="Profile" component={ProfileScreen} />
    </Tab.Navigator>
  );
}
