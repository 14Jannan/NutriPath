import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { colors, typography, spacing } from '@/theme';

export function LogScreen() {
  return (
    <SafeAreaView style={styles.screen}>
      <View style={styles.content}>
        <MaterialCommunityIcons name="book-open-variant" size={40} color={colors.outline} />
        <Text style={styles.title}>Meal logging</Text>
        <Text style={styles.body}>
          Food search and meal logging are built in Phases 9 and 10, once real food data (Phase 4 —
          already working) has a search endpoint in front of it.
        </Text>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  content: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.margin, gap: spacing.sm },
  title: { ...typography.headlineMd, color: colors.onSurface },
  body: { ...typography.bodyMd, color: colors.onSurfaceVariant, textAlign: 'center' },
});
