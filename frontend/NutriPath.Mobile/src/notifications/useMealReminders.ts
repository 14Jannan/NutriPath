import { useEffect } from 'react';
import { AppState, Platform } from 'react-native';
import * as Notifications from './localNotifications';
import { resyncMealReminders } from './mealReminders';

/**
 * Keeps meal reminders current while the user is logged in: re-planned on
 * launch and whenever the app returns to the foreground (meals may have
 * been logged elsewhere, or the day may have changed). Tapping a reminder
 * calls onOpen, which takes the user to the log.
 */
export function useMealReminders(onOpen: () => void) {
  useEffect(() => {
    if (Platform.OS === 'web') return;
    void resyncMealReminders();

    const appState = AppState.addEventListener('change', (state) => {
      if (state === 'active') void resyncMealReminders();
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
