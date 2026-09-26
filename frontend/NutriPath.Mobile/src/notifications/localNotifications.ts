// The LOCAL-notification parts of expo-notifications, imported file by file.
//
// The package's main entry also sets up remote (push) notifications as soon
// as it loads, and in Expo Go on Android (SDK 53+) that throws and crashes
// the app. Local scheduled notifications still work there, so the app only
// ever imports the pieces below — never 'expo-notifications' itself.

export { scheduleNotificationAsync } from 'expo-notifications/build/scheduleNotificationAsync';
export { getAllScheduledNotificationsAsync } from 'expo-notifications/build/getAllScheduledNotificationsAsync';
export { cancelScheduledNotificationAsync } from 'expo-notifications/build/cancelScheduledNotificationAsync';
export { setNotificationChannelAsync } from 'expo-notifications/build/setNotificationChannelAsync';
export { setNotificationHandler } from 'expo-notifications/build/NotificationsHandler';
export { getPermissionsAsync, requestPermissionsAsync } from 'expo-notifications/build/NotificationPermissions';
export { addNotificationResponseReceivedListener } from 'expo-notifications/build/NotificationsEmitter';
export { AndroidImportance } from 'expo-notifications/build/NotificationChannelManager.types';
export { SchedulableTriggerInputTypes } from 'expo-notifications/build/Notifications.types';
