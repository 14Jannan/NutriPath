import React, { useEffect, useState } from 'react';
import { Modal, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Avatar } from '@/components/Avatar';
import { AvatarPicker } from '@/components/AvatarPicker';
import { Button } from '@/components/Button';
import { colors, spacing, typography } from '@/theme';

interface AvatarPickerSheetProps {
  visible: boolean;
  current: string | null;
  saving: boolean;
  onClose: () => void;
  onSave: (avatarId: string) => void;
}

/** A full-height sheet to change the avatar: preview on top, grid below, Save at the bottom. */
export function AvatarPickerSheet({ visible, current, saving, onClose, onSave }: AvatarPickerSheetProps) {
  const [choice, setChoice] = useState<string | null>(current);

  // Start from the current avatar each time the sheet opens.
  useEffect(() => {
    if (visible) setChoice(current);
  }, [visible, current]);

  return (
    <Modal visible={visible} animationType="slide" presentationStyle="pageSheet" onRequestClose={onClose}>
      <SafeAreaView style={styles.screen} edges={['top', 'bottom']}>
        <View style={styles.inner}>
          <View style={styles.header}>
            <Text style={styles.title}>Choose your avatar</Text>
            <Pressable onPress={onClose} style={styles.close} accessibilityLabel="Close" hitSlop={6}>
              <MaterialCommunityIcons name="close" size={24} color={colors.onSurface} />
            </Pressable>
          </View>

          <View style={styles.preview}>
            <Avatar avatarId={choice} size={96} />
          </View>

          <ScrollView contentContainerStyle={{ paddingBottom: spacing.md }}>
            <AvatarPicker selected={choice} onSelect={setChoice} />
          </ScrollView>

          <Button
            label={saving ? 'Saving...' : 'Save avatar'}
            onPress={() => choice && onSave(choice)}
            disabled={!choice || choice === current || saving}
            style={{ marginVertical: spacing.sm }}
          />
        </View>
      </SafeAreaView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  inner: { flex: 1, width: '100%', maxWidth: 560, alignSelf: 'center', paddingHorizontal: spacing.margin },
  header: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingVertical: spacing.sm },
  title: { ...typography.headlineMd, color: colors.onSurface },
  close: { width: 40, height: 40, alignItems: 'center', justifyContent: 'center' },
  preview: { alignItems: 'center', marginBottom: spacing.md },
});
