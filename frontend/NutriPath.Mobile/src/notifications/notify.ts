// A tiny in-app notification bus. Any code (screens, API helpers) can
// raise a notification without hooks; NotificationHost, mounted once at
// the app root, renders them. This replaces Alert.alert / window.alert,
// which look like browser popups on web ("localhost says...").

export type NotificationType = 'success' | 'error' | 'warning' | 'info';

export interface Toast {
  id: number;
  type: NotificationType;
  title: string;
  message?: string;
}

export interface ConfirmRequest {
  id: number;
  title: string;
  message: string;
  confirmLabel: string;
  destructive: boolean;
  resolve: (confirmed: boolean) => void;
}

type Listener = {
  onToast: (toast: Toast) => void;
  onConfirm: (request: ConfirmRequest) => void;
};

let listener: Listener | null = null;
let nextId = 1;

/** Called by NotificationHost; returns an unsubscribe function. */
export function subscribe(l: Listener): () => void {
  listener = l;
  return () => {
    if (listener === l) listener = null;
  };
}

export function notify(type: NotificationType, title: string, message?: string) {
  listener?.onToast({ id: nextId++, type, title, message });
}

/** Resolves true if the user confirms, false if they cancel. */
export function confirm(
  title: string,
  message: string,
  confirmLabel = 'OK',
  destructive = false
): Promise<boolean> {
  return new Promise((resolve) => {
    // With no host mounted (e.g. in tests) nothing can be confirmed.
    if (!listener) return resolve(false);
    listener.onConfirm({ id: nextId++, title, message, confirmLabel, destructive, resolve });
  });
}
