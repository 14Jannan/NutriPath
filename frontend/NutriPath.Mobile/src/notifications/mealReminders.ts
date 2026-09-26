import * as Notifications from 'expo-notifications';
import { Platform } from 'react-native';
import { addDaysIso, toLocalIsoDate } from '@/utils/date';
import { MEAL_WINDOWS, MealType, windowEnd } from '@/utils/mealWindows';

// Local notifications, scheduled on the device — no server or push token.
// A reminder fires when a meal's window closes; it's cancelled as soon as
// that meal is logged, so only genuinely missed meals notify.

const CHANNEL_ID = 'meal-reminders';
const ID_PREFIX = 'meal-reminder:';
// Scheduled a week ahead, so reminders still arrive if the app isn't
// opened for a few days. Re-synced every time the app is used.
const DAYS_AHEAD = 7;

const MESSAGES: Record<MealType, { title: string; body: string }> = {
  Breakfast: {
    title: "🍳 Breakfast isn't logged yet",
    body: "Breakfast time's over! Did you eat? Log it now. Late is better than never.",
  },
  Lunch: {
    title: '🍛 Lunch not logged',
    body: "Your lunch log is looking lonely 👀 Add what you ate so today's numbers stay right.",
  },
  Snack: {
    title: "🍌 Snack time's up",
    body: 'Had a snack? Log it. Even that sneaky biscuit counts 😉',
  },
  Dinner: {
    title: "🍲 Dinner isn't logged yet",
    body: 'Log your dinner to close the day and keep your streak alive 🔥',
  },
};

// Notifications aren't available on web.
const supported = Platform.OS !== 'web';
let configured = false;

async function ensureReady(): Promise<boolean> {
  if (!supported) return false;
  if (!configured) {
    // Also show reminders while the app is open.
    Notifications.setNotificationHandler({
      handleNotification: async () => ({
        shouldPlaySound: true,
        shouldSetBadge: false,
        shouldShowBanner: true,
        shouldShowList: true,
      }),
    });
    if (Platform.OS === 'android') {
      await Notifications.setNotificationChannelAsync(CHANNEL_ID, {
        name: 'Meal reminders',
        description: "Reminds you when a meal's time has passed without a log.",
        importance: Notifications.AndroidImportance.HIGH,
      });
    }
    configured = true;
  }

  const permission = await Notifications.getPermissionsAsync();
  if (permission.granted) return true;
  if (!permission.canAskAgain) return false;
  return (await Notifications.requestPermissionsAsync()).granted;
}

export async function cancelMealReminders(): Promise<void> {
  if (!supported) return;
  const scheduled = await Notifications.getAllScheduledNotificationsAsync();
  await Promise.all(
    scheduled
      .filter((n) => n.identifier.startsWith(ID_PREFIX))
      .map((n) => Notifications.cancelScheduledNotificationAsync(n.identifier))
  );
}

/**
 * Re-plans the reminders from scratch: one per meal window still to close
 * over the next week, skipping meals already logged today.
 */
export async function syncMealReminders(loggedToday: string[], now: Date = new Date()): Promise<void> {
  try {
    if (!(await ensureReady())) return;
    await cancelMealReminders();

    const today = toLocalIsoDate(now);
    for (let offset = 0; offset < DAYS_AHEAD; offset++) {
      const date = addDaysIso(today, offset);
      for (const window of MEAL_WINDOWS) {
        if (offset === 0 && loggedToday.includes(window.mealType)) continue;
        const fireAt = windowEnd(window, date);
        if (fireAt <= now) continue;

        await Notifications.scheduleNotificationAsync({
          identifier: `${ID_PREFIX}${date}:${window.mealType}`,
          content: { ...MESSAGES[window.mealType], data: { screen: 'Log', mealType: window.mealType } },
          trigger: { type: Notifications.SchedulableTriggerInputTypes.DATE, date: fireAt, channelId: CHANNEL_ID },
        });
      }
    }
  } catch {
    // Reminders are a nicety; never let them break the screen that asked.
  }
}
