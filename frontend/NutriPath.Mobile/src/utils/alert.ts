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
