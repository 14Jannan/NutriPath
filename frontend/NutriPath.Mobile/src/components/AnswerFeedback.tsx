import React, { useState } from 'react';
import { Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { sendFeedback, type FeedbackReason } from '@/api/aiApi';
import { describeApiError } from '@/api/client';
import { showAlert } from '@/utils/alert';
import { scriptAwareStyle } from '@/utils/script';
import { colors, radii, spacing, typography } from '@/theme';

const REASONS: FeedbackReason[] = ['Incorrect', 'Unsafe', 'WrongLanguage', 'NotRelevant', 'Other'];

/** Thumbs up/down under an assistant answer; "not helpful" asks what was wrong. */
export function AnswerFeedback({ messageId }: { messageId: string }) {
  const { t } = useTranslation();
  const [sent, setSent] = useState<'up' | 'down' | null>(null);
  const [asking, setAsking] = useState(false);
  const [reason, setReason] = useState<FeedbackReason | null>(null);
  const [comment, setComment] = useState('');
  const [busy, setBusy] = useState(false);

  async function submit(helpful: boolean) {
    setBusy(true);
    try {
      await sendFeedback(messageId, helpful, helpful ? undefined : reason ?? undefined, helpful ? undefined : comment);
      setSent(helpful ? 'up' : 'down');
      setAsking(false);
      showAlert(t('chat.feedback.thanks'), undefined, 'success');
    } catch (error) {
      showAlert(t('chat.feedback.error'), describeApiError(error));
    } finally {
      setBusy(false);
    }
  }

  if (sent) {
    return (
      <View style={styles.row} accessibilityLiveRegion="polite">
        <MaterialCommunityIcons name={sent === 'up' ? 'thumb-up' : 'thumb-down'} size={14} color={colors.primary} />
        <Text style={[styles.thanks, scriptAwareStyle(t('chat.feedback.thanks'), styles.thanks)]}>{t('chat.feedback.thanks')}</Text>
      </View>
    );
  }

  return (
    <View>
      {!asking && (
        <View style={styles.row}>
          <Pressable accessibilityRole="button" accessibilityLabel={t('chat.feedback.helpful')} hitSlop={8} disabled={busy} onPress={() => void submit(true)} style={styles.icon}>
            <MaterialCommunityIcons name="thumb-up-outline" size={16} color={colors.outline} />
          </Pressable>
          <Pressable accessibilityRole="button" accessibilityLabel={t('chat.feedback.notHelpful')} hitSlop={8} disabled={busy} onPress={() => setAsking(true)} style={styles.icon}>
            <MaterialCommunityIcons name="thumb-down-outline" size={16} color={colors.outline} />
          </Pressable>
        </View>
      )}
      {asking && (
        <View style={styles.panel}>
          <Text style={[styles.question, scriptAwareStyle(t('chat.feedback.whatWrong'), styles.question)]}>{t('chat.feedback.whatWrong')}</Text>
          <View style={styles.chips}>
            {REASONS.map((r) => {
              const label = t(`chat.feedback.reasons.${r}`);
              const selected = reason === r;
              return (
                <Pressable
                  key={r}
                  accessibilityRole="radio"
                  accessibilityState={{ selected }}
                  onPress={() => setReason(r)}
                  style={[styles.chip, selected && styles.chipSelected]}
                >
                  <Text style={[styles.chipText, selected && styles.chipTextSelected, scriptAwareStyle(label, styles.chipText)]}>{label}</Text>
                </Pressable>
              );
            })}
          </View>
          <TextInput
            value={comment}
            onChangeText={setComment}
            maxLength={1000}
            multiline
            placeholder={t('chat.feedback.commentPlaceholder')}
            placeholderTextColor={colors.outline}
            style={styles.input}
            accessibilityLabel={t('chat.feedback.commentPlaceholder')}
          />
          <Text style={[styles.note, scriptAwareStyle(t('chat.feedback.shareNote'), styles.note)]}>{t('chat.feedback.shareNote')}</Text>
          <View style={styles.actions}>
            <Pressable accessibilityRole="button" onPress={() => setAsking(false)} style={styles.secondary}>
              <Text style={styles.secondaryText}>{t('chat.feedback.cancel')}</Text>
            </Pressable>
            <Pressable
              accessibilityRole="button"
              accessibilityState={{ disabled: !reason || busy }}
              disabled={!reason || busy}
              onPress={() => void submit(false)}
              style={[styles.primary, (!reason || busy) && styles.disabled]}
            >
              <Text style={styles.primaryText}>{t('chat.feedback.send')}</Text>
            </Pressable>
          </View>
        </View>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, marginTop: spacing.xs },
  icon: { padding: 2 },
  thanks: { ...typography.labelSm, color: colors.primary },
  panel: { marginTop: spacing.sm, gap: spacing.sm, padding: spacing.sm, borderRadius: radii.md, backgroundColor: colors.surfaceContainerLow },
  question: { ...typography.labelLg, color: colors.onSurface },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  chip: { paddingHorizontal: spacing.sm, paddingVertical: 6, borderRadius: radii.pill, borderWidth: 1, borderColor: colors.outline },
  chipSelected: { backgroundColor: colors.primary, borderColor: colors.primary },
  chipText: { ...typography.labelMd, color: colors.onSurface },
  chipTextSelected: { color: colors.onPrimary },
  input: {
    ...typography.bodyMd, color: colors.onSurface, backgroundColor: colors.surfaceContainerLowest, borderRadius: radii.md,
    paddingHorizontal: spacing.sm, paddingVertical: spacing.xs, minHeight: 40, textAlignVertical: 'top',
  },
  note: { ...typography.bodySm, color: colors.onSurfaceVariant },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: spacing.sm },
  secondary: { paddingHorizontal: spacing.md, paddingVertical: spacing.xs },
  secondaryText: { ...typography.labelLg, color: colors.onSurfaceVariant },
  primary: { paddingHorizontal: spacing.md, paddingVertical: spacing.xs, borderRadius: radii.pill, backgroundColor: colors.primary },
  primaryText: { ...typography.labelLg, color: colors.onPrimary },
  disabled: { opacity: 0.5 },
});
