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
// Set once our own Android channel exists; until then reminders use the
// library's default channel.
let channelId: string | undefined;

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
      try {
        await Notifications.setNotificationChannelAsync(CHANNEL_ID, {
          name: 'Meal reminders',
          description: "Reminds you when a meal's time has passed without a log.",
          importance: Notifications.AndroidImportance.HIGH,
        });
        channelId = CHANNEL_ID;
      } catch {
        // Expo Go on Android fails here (a bug in its scoped channel
        // manager). The channel only names the reminders in the phone's
        // settings, so fall back to the default channel instead of failing.
        channelId = undefined;
      }
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

const reminderId = (date: string, mealType: string) => `${ID_PREFIX}${date}:${mealType}`;

// Every reminder id this app could have scheduled around `now`: yesterday
// (in case the day just changed) through the planning horizon. Cancelling
// by id avoids listing all scheduled notifications, which in Expo Go also
// returns other projects' ones and can fail on those.
function knownReminderIds(now: Date): string[] {
  const today = toLocalIsoDate(now);
  const ids: string[] = [];
  for (let offset = -1; offset <= DAYS_AHEAD; offset++) {
    const date = addDaysIso(today, offset);
    for (const window of MEAL_WINDOWS) ids.push(reminderId(date, window.mealType));
  }
  return ids;
}

// Syncs are triggered from several places at once (app start, the Log
// screen, returning to the app), so they run one after another.
let queue: Promise<void> = Promise.resolve();
function serialized(task: () => Promise<void>): Promise<void> {
  const run = queue.then(task, task);
  queue = run.catch(() => {});
  return run;
}

export function cancelMealReminders(now: Date = new Date()): Promise<void> {
  if (!supported) return Promise.resolve();
  return serialized(async () => {
    for (const id of knownReminderIds(now)) {
      await Notifications.cancelScheduledNotificationAsync(id).catch(() => {}); // not scheduled: fine
    }
  });
}

/**
 * Brings the reminders up to date: one per meal window still to close over
 * the next week, except meals already logged today. Scheduling an id that
 * already exists replaces it, so nothing needs clearing first.
 */
export function syncMealReminders(loggedToday: string[], now: Date = new Date()): Promise<void> {
  return serialized(async () => {
    try {
      if (!(await ensureReady())) return;
    } catch (error) {
      warn("couldn't get notification permission", error);
      return;
    }

    const today = toLocalIsoDate(now);
    for (let offset = 0; offset < DAYS_AHEAD; offset++) {
      const date = addDaysIso(today, offset);
      for (const window of MEAL_WINDOWS) {
        const id = reminderId(date, window.mealType);
        const fireAt = windowEnd(window, date);
        try {
          if (offset === 0 && (loggedToday.includes(window.mealType) || fireAt <= now)) {
            await Notifications.cancelScheduledNotificationAsync(id); // logged, or already past
            continue;
          }
          await Notifications.scheduleNotificationAsync({
            identifier: id,
            content: { ...MESSAGES[window.mealType], data: { screen: 'Log', mealType: window.mealType } },
            trigger: { type: Notifications.SchedulableTriggerInputTypes.DATE, date: fireAt, channelId },
          });
        } catch (error) {
          // One bad reminder shouldn't stop the rest.
          warn(`couldn't schedule ${window.mealType} on ${date}`, error);
        }
      }
    }
  });
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

/**
 * A sample reminder a few seconds from now, to check they come through.
 * Returns null on success, or why it failed.
 */
export async function sendTestReminder(): Promise<string | null> {
  try {
    if (!(await ensureReady())) return 'Notifications are not allowed for this app.';
    await Notifications.scheduleNotificationAsync({
      content: { ...MESSAGES.Lunch, data: { screen: 'Log', test: true } },
      trigger: {
        type: Notifications.SchedulableTriggerInputTypes.DATE,
        date: new Date(Date.now() + 5_000),
        channelId,
      },
    });
    return null;
  } catch (error) {
    warn("couldn't send the test", error);
    return error instanceof Error ? error.message : String(error);
  }
}
