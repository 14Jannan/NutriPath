import React, { useEffect, useState } from 'react';
import { View, Text, StyleSheet, TextInput, FlatList, Pressable, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useNavigation, useRoute, RouteProp } from '@react-navigation/native';
import { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { searchFoods, FoodSearchResult } from '@/api/foodsApi';
import { LogStackParamList } from '@/navigation/LogStackNavigator';
import { colors, typography, spacing, radii } from '@/theme';

export function FoodSearchScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<LogStackParamList>>();
  const route = useRoute<RouteProp<LogStackParamList, 'FoodSearch'>>();
  const { mealType, date } = route.params;

  const [query, setQuery] = useState('');
  const [results, setResults] = useState<FoodSearchResult[]>([]);
  const [loading, setLoading] = useState(false);

  // Debouncing: without this, every single keystroke would fire a network
  // request — typing "chicken" would trigger 7 separate searches, most of
  // them wasted the instant the next letter is typed. This waits 400ms
  // after the user STOPS typing before actually searching.
  useEffect(() => {
    if (query.trim().length < 2) {
      setResults([]);
      setLoading(false);
      return;
    }

    // Set by the cleanup below, so a slow response for an old query
    // can't overwrite the results for what the user has typed since.
    let cancelled = false;

    setLoading(true);
    const timeoutId = setTimeout(() => {
      searchFoods(query.trim())
        .then((data) => {
          if (!cancelled) setResults(data);
        })
        .catch(() => {
          if (!cancelled) setResults([]);
        })
        .finally(() => {
          if (!cancelled) setLoading(false);
        });
    }, 400);

    // Cleanup: if the user types another letter before 400ms passes,
    // this cancels the PREVIOUS pending search before scheduling a new
    // one — otherwise old, now-outdated searches could still fire late.
    return () => {
      cancelled = true;
      clearTimeout(timeoutId);
    };
  }, [query]);

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <View style={styles.header}>
        <Pressable onPress={() => navigation.goBack()} style={styles.backButton}>
          <MaterialCommunityIcons name="arrow-left" size={22} color={colors.onSurface} />
        </Pressable>
        <Text style={styles.headerTitle}>Add food to {mealType}</Text>
      </View>

      <View style={styles.searchBar}>
        <MaterialCommunityIcons name="magnify" size={20} color={colors.onSurfaceVariant} />
        <TextInput
          style={styles.searchInput}
          placeholder="Search foods (e.g. rice, dhal, chicken)"
          placeholderTextColor={colors.outline}
          value={query}
          onChangeText={setQuery}
          autoFocus
        />
        {loading && <ActivityIndicator size="small" color={colors.primary} />}
      </View>

      <FlatList
        data={results}
        keyExtractor={(item) => item.id}
        contentContainerStyle={styles.list}
        keyboardShouldPersistTaps="handled"
        ListEmptyComponent={
          query.trim().length >= 2 && !loading ? (
            <Text style={styles.emptyText}>No foods found for "{query}"</Text>
          ) : null
        }
        renderItem={({ item }) => (
          <Pressable
            style={styles.resultRow}
            onPress={() => navigation.navigate('FoodDetail', { foodId: item.id, mealType, date })}
          >
            <View style={{ flex: 1 }}>
              <Text style={styles.resultName}>{item.name}</Text>
              <Text style={styles.resultDetail}>
                {Math.round(item.calories)} kcal · {item.proteinGrams}g protein per {item.servingSizeGrams}g
              </Text>
            </View>
            <MaterialCommunityIcons name="chevron-right" size={20} color={colors.outline} />
          </Pressable>
        )}
      />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  header: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingHorizontal: spacing.margin, height: 56 },
  backButton: { width: 40, height: 40, alignItems: 'center', justifyContent: 'center' },
  headerTitle: { ...typography.headlineMd, fontSize: 16, color: colors.onSurface },
  searchBar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    marginHorizontal: spacing.margin,
    paddingHorizontal: spacing.sm,
    height: 44,
    backgroundColor: colors.surfaceContainerLowest,
    borderRadius: radii.md,
  },
  searchInput: { flex: 1, ...typography.bodyMd, color: colors.onSurface },
  list: { padding: spacing.margin, gap: spacing.xs },
  resultRow: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: colors.surfaceContainerLowest,
    padding: spacing.sm,
    borderRadius: radii.md,
    marginBottom: spacing.xs,
  },
  resultName: { ...typography.labelLg, color: colors.onSurface },
  resultDetail: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 2 },
  emptyText: { ...typography.bodyMd, color: colors.onSurfaceVariant, textAlign: 'center', marginTop: spacing.lg },
});
