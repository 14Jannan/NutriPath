import { Alert, Platform } from 'react-native';

// React Native Web implements Alert.alert as a no-op, so on the web
// target every validation/error message would silently disappear.
// Fall back to the browser's own alert dialog there.
export function showAlert(title: string, message?: string) {
  if (Platform.OS === 'web') {
    window.alert(message ? `${title}\n\n${message}` : title);
  } else {
    Alert.alert(title, message);
  }
}

// Same web problem for confirmations: Alert.alert's buttons never appear
// on the web target, so the browser's confirm dialog is used there.
export function confirmAction(title: string, message: string, confirmLabel: string, onConfirm: () => void) {
  if (Platform.OS === 'web') {
    if (window.confirm(`${title}\n\n${message}`)) onConfirm();
  } else {
    Alert.alert(title, message, [
      { text: 'Cancel', style: 'cancel' },
      { text: confirmLabel, style: 'destructive', onPress: onConfirm },
    ]);
  }
}
