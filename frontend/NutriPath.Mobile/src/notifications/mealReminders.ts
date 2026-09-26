import * as Notifications from './localNotifications';
import { Linking, Platform } from 'react-native';
import { getDailyMeals } from '@/api/mealsApi';
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

// Reminder failures are silent for users, but visible in Metro while developing.
function warn(what: string, error: unknown) {
  if (__DEV__) console.warn(`Meal reminders: ${what}`, error);
}

async function configure() {
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
}

async function ensureReady(): Promise<boolean> {
  if (!supported) return false;
  await configure();
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
  } catch (error) {
    // Reminders are a nicety; never let them break the screen that asked.
    warn("couldn't schedule", error);
  }
}

/** Which meals have food logged today. */
export async function loggedMealTypesToday(): Promise<string[]> {
  const daily = await getDailyMeals();
  return daily.meals.filter((m) => m.items.length > 0).map((m) => m.mealType);
}

/** Re-plans reminders from the server's view of today's log. */
export async function resyncMealReminders(): Promise<void> {
  try {
    await syncMealReminders(await loggedMealTypesToday());
  } catch (error) {
    warn("couldn't load today's meals", error); // offline: keep the last plan
  }
}

export type ReminderStatus = 'on' | 'off' | 'blocked' | 'unsupported';

/**
 * 'off' means the app can still ask for permission; 'blocked' means the
 * user said no for good, so only the phone's settings can turn it on.
 */
export async function getReminderStatus(): Promise<ReminderStatus> {
  if (!supported) return 'unsupported';
  try {
    const permission = await Notifications.getPermissionsAsync();
    if (permission.granted) return 'on';
    return permission.canAskAgain ? 'off' : 'blocked';
  } catch (error) {
    warn("couldn't read permission", error);
    return 'unsupported';
  }
}

/** Asks for permission (or opens settings if blocked), then schedules. */
export async function enableMealReminders(): Promise<ReminderStatus> {
  const status = await getReminderStatus();
  if (status === 'blocked') {
    await Linking.openSettings();
    return status;
  }
  if (status === 'unsupported') return status;
  await resyncMealReminders(); // asks for permission if needed
  return getReminderStatus();
}

/** A sample reminder a few seconds from now, to check they come through. */
export async function sendTestReminder(): Promise<boolean> {
  try {
    if (!(await ensureReady())) return false;
    await Notifications.scheduleNotificationAsync({
      content: { ...MESSAGES.Lunch, data: { screen: 'Log', test: true } },
      trigger: {
        type: Notifications.SchedulableTriggerInputTypes.DATE,
        date: new Date(Date.now() + 5_000),
        channelId: CHANNEL_ID,
      },
    });
    return true;
  } catch (error) {
    warn("couldn't send the test", error);
    return false;
  }
}
