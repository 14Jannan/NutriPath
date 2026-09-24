import { confirm, notify, NotificationType } from '@/notifications/notify';

// Thin wrappers kept so screens stay simple. Both render inside the app
// (see NotificationHost) instead of as native/browser popups, so they look
// the same on phone and web — no more "localhost says...".

/** Shows an in-app notification at the top of the screen. Errors by default. */
export function showAlert(title: string, message?: string, type: NotificationType = 'error') {
  notify(type, title, message);
}

/** Asks for confirmation in an in-app bottom sheet; runs onConfirm only if confirmed. */
export function confirmAction(
  title: string,
  message: string,
  confirmLabel: string,
  onConfirm: () => void,
  destructive = false
) {
  void confirm(title, message, confirmLabel, destructive).then((ok) => {
    if (ok) onConfirm();
  });
}
