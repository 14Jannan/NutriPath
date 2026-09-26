import { useEffect } from 'react';
import { AppState, Platform } from 'react-native';
import * as Notifications from 'expo-notifications';
import { getDailyMeals } from '@/api/mealsApi';
import { syncMealReminders } from './mealReminders';

/** Which meals have food logged today. */
export async function loggedMealTypesToday(): Promise<string[]> {
  const daily = await getDailyMeals();
  return daily.meals.filter((m) => m.items.length > 0).map((m) => m.mealType);
}

async function resync() {
  try {
    await syncMealReminders(await loggedMealTypesToday());
  } catch {
    // Offline: keep whatever was scheduled last time.
  }
}

/**
 * Keeps meal reminders current while the user is logged in: re-planned on
 * launch and whenever the app returns to the foreground (meals may have
 * been logged elsewhere, or the day may have changed). Tapping a reminder
 * calls onOpen, which takes the user to the log.
 */
export function useMealReminders(onOpen: () => void) {
  useEffect(() => {
    if (Platform.OS === 'web') return;
    void resync();

    const appState = AppState.addEventListener('change', (state) => {
      if (state === 'active') void resync();
    });
    const taps = Notifications.addNotificationResponseReceivedListener((response) => {
      if (response.notification.request.identifier.startsWith('meal-reminder:')) onOpen();
    });
    return () => {
      appState.remove();
      taps.remove();
    };
  }, [onOpen]);
}
