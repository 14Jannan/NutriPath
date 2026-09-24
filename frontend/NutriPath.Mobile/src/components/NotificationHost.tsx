import React, { useEffect, useRef, useState } from 'react';
import { Animated, Modal, Pressable, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { ConfirmRequest, NotificationType, subscribe, Toast } from '@/notifications/notify';
import { colors, radii, spacing, typography } from '@/theme';

const TOAST_DURATION_MS = 4000;
const MAX_VISIBLE = 3;

const LOOK: Record<NotificationType, { icon: keyof typeof MaterialCommunityIcons.glyphMap; color: string }> = {
  success: { icon: 'check-circle', color: colors.primary },
  error: { icon: 'alert-circle', color: colors.amberCaution },
  warning: { icon: 'alert', color: colors.amberCaution },
  info: { icon: 'information', color: colors.secondary },
};

/** One toast: slides down from the top, auto-hides, tap to dismiss. */
function ToastCard({ toast, onDismiss }: { toast: Toast; onDismiss: () => void }) {
  const anim = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    Animated.timing(anim, { toValue: 1, duration: 200, useNativeDriver: true }).start();
    const timer = setTimeout(() => {
      Animated.timing(anim, { toValue: 0, duration: 200, useNativeDriver: true }).start(onDismiss);
    }, TOAST_DURATION_MS);
    return () => clearTimeout(timer);
  }, [anim, onDismiss]);

  const look = LOOK[toast.type];
  return (
    <Animated.View
      style={[
        styles.toast,
        { borderLeftColor: look.color, opacity: anim },
        { transform: [{ translateY: anim.interpolate({ inputRange: [0, 1], outputRange: [-20, 0] }) }] },
      ]}
    >
      <Pressable style={styles.toastInner} onPress={onDismiss} accessibilityRole="alert" accessibilityLabel={`${toast.title}. ${toast.message ?? ''}`}>
        <MaterialCommunityIcons name={look.icon} size={22} color={look.color} />
        <View style={{ flex: 1 }}>
          <Text style={styles.toastTitle}>{toast.title}</Text>
          {toast.message ? <Text style={styles.toastMessage}>{toast.message}</Text> : null}
        </View>
        <MaterialCommunityIcons name="close" size={16} color={colors.outline} />
      </Pressable>
    </Animated.View>
  );
}

/**
 * Renders in-app notifications (top toasts) and confirmations (a bottom
 * sheet), so messages look like part of the app on phone and web alike.
 * Mount once, at the root.
 */
export function NotificationHost() {
  const insets = useSafeAreaInsets();
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [confirmRequest, setConfirmRequest] = useState<ConfirmRequest | null>(null);

  useEffect(
    () =>
      subscribe({
        // Newest last; old ones drop off so the stack never covers the screen.
        onToast: (toast) => setToasts((prev) => [...prev, toast].slice(-MAX_VISIBLE)),
        onConfirm: (request) =>
          setConfirmRequest((current) => {
            current?.resolve(false); // a newer question replaces an unanswered one
            return request;
          }),
      }),
    []
  );

  const dismiss = (id: number) => setToasts((prev) => prev.filter((t) => t.id !== id));

  function answer(confirmed: boolean) {
    confirmRequest?.resolve(confirmed);
    setConfirmRequest(null);
  }

  return (
    <>
      <View pointerEvents="box-none" style={[styles.toastArea, { top: insets.top + spacing.xs }]}>
        {toasts.map((toast) => (
          <ToastCard key={toast.id} toast={toast} onDismiss={() => dismiss(toast.id)} />
        ))}
      </View>

      <Modal visible={!!confirmRequest} transparent animationType="fade" onRequestClose={() => answer(false)}>
        <Pressable style={styles.backdrop} onPress={() => answer(false)}>
          {/* Inner Pressable stops taps on the sheet from closing it. */}
          <Pressable style={[styles.sheet, { paddingBottom: insets.bottom + spacing.md }]} onPress={() => {}}>
            <Text style={styles.sheetTitle}>{confirmRequest?.title}</Text>
            <Text style={styles.sheetMessage}>{confirmRequest?.message}</Text>
            <View style={styles.sheetButtons}>
              <Pressable style={[styles.sheetButton, styles.cancelButton]} onPress={() => answer(false)}>
                <Text style={styles.cancelText}>Cancel</Text>
              </Pressable>
              <Pressable
                style={[
                  styles.sheetButton,
                  { backgroundColor: confirmRequest?.destructive ? colors.amberCaution : colors.primary },
                ]}
                onPress={() => answer(true)}
              >
                <Text style={styles.confirmText}>{confirmRequest?.confirmLabel}</Text>
              </Pressable>
            </View>
          </Pressable>
        </Pressable>
      </Modal>
    </>
  );
}

const styles = StyleSheet.create({
  toastArea: {
    position: 'absolute',
    left: spacing.sm,
    right: spacing.sm,
    alignItems: 'center',
    gap: spacing.xs,
    zIndex: 1000,
    elevation: 1000,
  },
  toast: {
    width: '100%',
    maxWidth: 480, // stays phone-sized on a wide browser window
    backgroundColor: colors.surfaceContainerLowest,
    borderRadius: radii.md,
    borderLeftWidth: 4,
    shadowColor: '#000',
    shadowOpacity: 0.15,
    shadowRadius: 12,
    shadowOffset: { width: 0, height: 4 },
    elevation: 6,
  },
  toastInner: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm, padding: spacing.sm },
  toastTitle: { ...typography.labelLg, color: colors.onSurface },
  toastMessage: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 2 },
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.35)', justifyContent: 'flex-end', alignItems: 'center' },
  sheet: {
    width: '100%',
    maxWidth: 520,
    backgroundColor: colors.surfaceContainerLowest,
    borderTopLeftRadius: radii.lg,
    borderTopRightRadius: radii.lg,
    padding: spacing.md,
    gap: spacing.xs,
  },
  sheetTitle: { ...typography.headlineMd, fontSize: 18, color: colors.onSurface },
  sheetMessage: { ...typography.bodyMd, color: colors.onSurfaceVariant },
  sheetButtons: { flexDirection: 'row', gap: spacing.sm, marginTop: spacing.md },
  sheetButton: { flex: 1, height: 48, borderRadius: radii.pill, alignItems: 'center', justifyContent: 'center' },
  cancelButton: { backgroundColor: colors.surfaceContainerLow },
  cancelText: { ...typography.labelLg, color: colors.primary },
  confirmText: { ...typography.labelLg, color: colors.onPrimary },
});
