import React, { useCallback, useEffect, useRef, useState } from 'react';
import { View, Text, StyleSheet, TextInput, FlatList, Pressable, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect, useNavigation, useRoute, RouteProp } from '@react-navigation/native';
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
  // Bumped when the screen regains focus, so results reflect any food the
  // user just added, edited or deleted.
  const [refreshKey, setRefreshKey] = useState(0);
  const focusedBefore = useRef(false);
  useFocusEffect(
    useCallback(() => {
      if (focusedBefore.current) setRefreshKey((k) => k + 1);
      focusedBefore.current = true;
    }, [])
  );
  const [loading, setLoading] = useState(false);
  // True once a search has taken a while: the server is looking the food
  // up in USDA because the catalog had few matches.
  const [slow, setSlow] = useState(false);

  // Only shown when a search runs long, so quick searches don't flicker.
  useEffect(() => {
    if (!loading) {
      setSlow(false);
      return;
    }
    const timer = setTimeout(() => setSlow(true), 1200);
    return () => clearTimeout(timer);
  }, [loading]);

  const addOwnFood = () => navigation.navigate('AddFood', { mealType, date, name: query.trim() || undefined });

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
  }, [query, refreshKey]);

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
      {slow && <Text style={styles.slowText}>Looking in the USDA food database too...</Text>}

      <FlatList
        data={results}
        keyExtractor={(item) => item.id}
        contentContainerStyle={styles.list}
        keyboardShouldPersistTaps="handled"
        ListEmptyComponent={
          query.trim().length >= 2 && !loading ? (
            <View style={styles.emptyBlock}>
              <MaterialCommunityIcons name="food-off-outline" size={36} color={colors.outline} />
              <Text style={styles.emptyText}>No foods found for "{query}"</Text>
              <Text style={styles.emptyHint}>
                Local dishes like kottu or hoppers aren't in the USDA database. You can add your own with the
                nutrition from a label or a trusted source.
              </Text>
              <Pressable style={styles.addOwnButton} onPress={addOwnFood} accessibilityRole="button">
                <MaterialCommunityIcons name="plus" size={18} color={colors.onPrimary} />
                <Text style={styles.addOwnButtonText}>Add "{query.trim()}" as your own food</Text>
              </Pressable>
            </View>
          ) : null
        }
        ListFooterComponent={
          results.length > 0 && !loading ? (
            <Pressable style={styles.addOwnLink} onPress={addOwnFood} accessibilityRole="button">
              <MaterialCommunityIcons name="plus-circle-outline" size={18} color={colors.primary} />
              <Text style={styles.addOwnLinkText}>Can't find it? Add your own food</Text>
            </Pressable>
          ) : null
        }
        renderItem={({ item }) => (
          <Pressable
            style={styles.resultRow}
            onPress={() => navigation.navigate('FoodDetail', { foodId: item.id, mealType, date })}
          >
            <View style={{ flex: 1 }}>
              <View style={styles.nameRow}>
                <Text style={[styles.resultName, { flexShrink: 1 }]}>{item.name}</Text>
                {item.isCustom && <Text style={styles.customBadge}>Added by you</Text>}
              </View>
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
  emptyText: { ...typography.bodyMd, color: colors.onSurfaceVariant, textAlign: 'center', marginTop: spacing.sm },
  emptyBlock: { alignItems: 'center', gap: spacing.xs, marginTop: spacing.lg, paddingHorizontal: spacing.sm },
  emptyHint: { ...typography.bodySm, color: colors.onSurfaceVariant, textAlign: 'center', maxWidth: 360 },
  slowText: { ...typography.bodySm, color: colors.onSurfaceVariant, marginHorizontal: spacing.margin, marginTop: spacing.xs },
  addOwnButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    backgroundColor: colors.primary,
    paddingHorizontal: spacing.md,
    minHeight: 44,
    borderRadius: radii.pill,
    marginTop: spacing.sm,
  },
  addOwnButtonText: { ...typography.labelLg, color: colors.onPrimary },
  addOwnLink: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 6, minHeight: 44, marginTop: spacing.xs },
  addOwnLinkText: { ...typography.labelLg, color: colors.primary },
  nameRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, flexWrap: 'wrap' },
  customBadge: {
    ...typography.labelSm,
    color: colors.primary,
    backgroundColor: colors.secondaryFixed,
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderRadius: radii.pill,
    overflow: 'hidden',
  },
});
