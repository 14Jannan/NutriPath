import React, { useCallback, useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { showAlert } from '@/utils/alert';
import { MEAL_WINDOWS, describeWindow } from '@/utils/mealWindows';
import {
  ReminderStatus,
  enableMealReminders,
  getReminderStatus,
  sendTestReminder,
} from '@/notifications/mealReminders';
import { colors, spacing, typography } from '@/theme';

const STATUS_TEXT: Record<ReminderStatus, string> = {
  on: "On. You'll get a reminder when a meal's time ends without a log.",
  off: 'Off. Turn them on to get a nudge when you forget to log a meal.',
  blocked: 'Notifications are blocked for this app. Turn them on in your phone settings.',
  unsupported: "Reminders aren't available on this device.",
};

/** Profile section showing the meal times and whether reminders can arrive. */
export function MealRemindersCard() {
  const [status, setStatus] = useState<ReminderStatus | null>(null);

  // Re-checked on focus: the user may have changed it in phone settings.
  useFocusEffect(
    useCallback(() => {
      getReminderStatus().then(setStatus);
    }, [])
  );

  async function turnOn() {
    setStatus(await enableMealReminders());
  }

  async function test() {
    if (await sendTestReminder()) showAlert('Test reminder on its way', 'It should pop up in about 5 seconds.', 'success');
    else showAlert("Couldn't send a test", 'Check that notifications are allowed for this app.');
  }

  return (
    <Card style={{ marginTop: spacing.sm }}>
      <View style={styles.header}>
        <MaterialCommunityIcons
          name={status === 'on' ? 'bell-ring-outline' : 'bell-off-outline'}
          size={20}
          color={status === 'on' ? colors.primary : colors.outline}
        />
        <Text style={styles.title}>Meal Reminders</Text>
      </View>
      {status && <Text style={styles.status}>{STATUS_TEXT[status]}</Text>}

      {MEAL_WINDOWS.map((w) => (
        <View key={w.mealType} style={styles.row}>
          <Text style={styles.meal}>
            {w.emoji} {w.mealType}
          </Text>
          <Text style={styles.window}>{describeWindow(w)}</Text>
        </View>
      ))}

      {status === 'on' && (
        <Button label="Send a Test Reminder" variant="secondary" onPress={test} style={{ marginTop: spacing.sm }} />
      )}
      {(status === 'off' || status === 'blocked') && (
        <Button
          label={status === 'blocked' ? 'Open Settings' : 'Turn On Reminders'}
          onPress={turnOn}
          style={{ marginTop: spacing.sm }}
        />
      )}
    </Card>
  );
}

const styles = StyleSheet.create({
  header: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  title: { ...typography.labelLg, color: colors.onSurface },
  status: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 4, marginBottom: spacing.xs },
  row: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 3 },
  meal: { ...typography.bodyMd, color: colors.onSurface },
  window: { ...typography.bodySm, color: colors.onSurfaceVariant },
});
