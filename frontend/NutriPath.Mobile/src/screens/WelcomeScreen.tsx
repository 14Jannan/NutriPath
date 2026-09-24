import React from 'react';
import { View, Text, Image, StyleSheet, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Button } from '@/components/Button';
import { colors, typography, spacing, radii } from '@/theme';


interface WelcomeScreenProps {
  onGetStarted: () => void;
  onLogIn: () => void;
}

export function WelcomeScreen({ onGetStarted, onLogIn }: WelcomeScreenProps) {
  return (
    <SafeAreaView style={styles.screen} edges={['top', 'bottom']}>
      <ScrollView contentContainerStyle={styles.content}>
        {/* Top utility bar */}
        <View style={styles.topBar}>
          <View style={styles.ayubowanPill}>
            <MaterialCommunityIcons name="flower" size={16} color={colors.primary} />
            <Text style={styles.ayubowanText}>AYUBOWAN</Text>
          </View>
        </View>

        {/* Brand badge */}
        <View style={styles.brandBlock}>
          <View style={styles.brandRow}>
            <View style={styles.brandIcon}>
              <MaterialCommunityIcons name="leaf" size={20} color={colors.secondaryFixed} />
            </View>
            <Text style={styles.brandTitle}>NutriPath</Text>
          </View>
          <Text style={styles.brandTagline}>Mindful Eating, Sri Lankan Flavours</Text>
        </View>

        {/* Hero image card */}
        <View style={styles.heroCard}>
          <View style={styles.heroImageWrap}>
            <Image source={require('../../assets/images/welcome-hero.jpg')} style={styles.heroImage} resizeMode="cover" />
            <View style={styles.freshTag}>
              <View style={styles.pulseDot} />
              <Text style={styles.freshTagText}>Island Fresh Insights</Text>
            </View>
            <View style={styles.portionTag}>
              <MaterialCommunityIcons name="fire" size={14} color={colors.onSecondaryFixed} />
              <Text style={styles.portionTagText}>Smart Portion</Text>
            </View>
          </View>

          {/* Live preview pills */}
          <View style={styles.pillRow}>
            <View style={styles.previewPill}>
              <MaterialCommunityIcons name="rice" size={20} color={colors.secondary} />
              <View style={{ flexShrink: 1 }}>
                <Text style={styles.pillLabel}>Samba & Parippu</Text>
                <Text style={styles.pillValue}>380 kcal · 14g Pro</Text>
              </View>
            </View>
            <View style={styles.previewPill}>
              <MaterialCommunityIcons name="check-decagram" size={20} color={colors.secondary} />
              <View style={{ flexShrink: 1 }}>
                <Text style={styles.pillLabel}>Nutrient Index</Text>
                <Text style={[styles.pillValue, { color: colors.primary }]}>94/100 Balanced</Text>
              </View>
            </View>
          </View>
        </View>

        {/* Narrative header */}
        <View style={styles.narrative}>
          <Text style={styles.narrativeTitle}>Nourish Your Body With Cultural Comfort</Text>
          <Text style={styles.narrativeBody}>
            Log dhal, string hoppers, fish ambul thiyal & rice effortlessly with real-time
            calorie & macro scoring.
          </Text>
        </View>

        {/* Feature badges */}
        <View style={styles.featureRow}>
          <View style={styles.featureBadge}>
            <Text>🍛</Text>
            <Text style={styles.featureText}>2,000+ Native Recipes</Text>
          </View>
          <View style={styles.featureBadge}>
            <Text>✨</Text>
            <Text style={styles.featureText}>AI Coach Explanations</Text>
          </View>
          <View style={styles.featureBadge}>
            <Text>🎯</Text>
            <Text style={styles.featureText}>100pt Balance Score</Text>
          </View>
        </View>

        {/* Actions */}
        <View style={styles.actions}>
          <Button label="Get Started Free" onPress={onGetStarted} />
          <Button label="I Already Have an Account" variant="secondary" onPress={onLogIn} />
        </View>

        <View style={styles.disclaimerRow}>
          <MaterialCommunityIcons name="information-outline" size={14} color={colors.outline} />
          <Text style={styles.disclaimerText}>General wellness guidance, not medical advice.</Text>
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  content: { paddingHorizontal: spacing.margin, paddingBottom: spacing.lg },
  topBar: { flexDirection: 'row', justifyContent: 'flex-end', paddingVertical: spacing.sm },
  ayubowanPill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    backgroundColor: colors.surfaceContainer,
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: radii.pill,
  },
  ayubowanText: { ...typography.labelSm, color: colors.secondary, letterSpacing: 1 },
  brandBlock: { alignItems: 'center', marginBottom: spacing.sm },
  brandRow: { flexDirection: 'row', alignItems: 'center', gap: 8, marginBottom: 4 },
  brandIcon: {
    width: 32,
    height: 32,
    borderRadius: 16,
    backgroundColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  brandTitle: { ...typography.headlineLg, color: colors.primary },
  brandTagline: { ...typography.labelMd, color: colors.secondary },
  heroCard: {
    backgroundColor: colors.surfaceContainerLowest,
    borderRadius: radii.lg,
    overflow: 'hidden',
    marginBottom: spacing.md,
    shadowColor: '#000',
    shadowOpacity: 0.08,
    shadowRadius: 10,
    shadowOffset: { width: 0, height: 4 },
    elevation: 3,
  },
  heroImageWrap: { width: '100%', aspectRatio: 4 / 3, backgroundColor: colors.surfaceContainerLow },
  heroImage: { width: '100%', height: '100%' },
  freshTag: {
    position: 'absolute',
    bottom: 12,
    left: 12,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    backgroundColor: 'rgba(255,255,255,0.9)',
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: radii.pill,
  },
  pulseDot: { width: 8, height: 8, borderRadius: 4, backgroundColor: colors.emerald },
  freshTagText: { ...typography.labelSm, color: colors.onSurface },
  portionTag: {
    position: 'absolute',
    top: 12,
    right: 12,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: 'rgba(179,239,217,0.9)',
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: radii.pill,
  },
  portionTagText: { ...typography.labelSm, color: colors.onSecondaryFixed, fontWeight: '600' },
  pillRow: { flexDirection: 'row', gap: spacing.sm, padding: spacing.sm },
  previewPill: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    backgroundColor: colors.surfaceContainerLow,
    padding: 10,
    borderRadius: radii.md,
  },
  pillLabel: { ...typography.labelSm, color: colors.secondary },
  pillValue: { ...typography.labelMd, color: colors.onSurface },
  narrative: { alignItems: 'center', marginBottom: spacing.md, paddingHorizontal: spacing.xs },
  narrativeTitle: {
    ...typography.headlineMd,
    color: colors.onSurface,
    textAlign: 'center',
    marginBottom: 8,
  },
  narrativeBody: { ...typography.bodyMd, color: colors.onSurfaceVariant, textAlign: 'center' },
  featureRow: { flexDirection: 'row', flexWrap: 'wrap', justifyContent: 'center', gap: 8, marginBottom: spacing.lg },
  featureBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    backgroundColor: colors.surfaceContainerLow,
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: radii.pill,
  },
  featureText: { ...typography.labelSm, color: colors.secondary },
  actions: { gap: spacing.sm, marginBottom: spacing.sm },
  disclaimerRow: { flexDirection: 'row', justifyContent: 'center', alignItems: 'center', gap: 6 },
  disclaimerText: { ...typography.bodySm, color: colors.outline },
});