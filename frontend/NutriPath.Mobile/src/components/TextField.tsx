import React, { useState } from 'react';
import { View, TextInput, Text, StyleSheet, Pressable, TextInputProps } from 'react-native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { colors, typography, radii, spacing } from '@/theme';

interface TextFieldProps extends TextInputProps {
  label: string;
  icon: keyof typeof MaterialCommunityIcons.glyphMap;
  isPassword?: boolean;
}

export function TextField({ label, icon, isPassword, ...inputProps }: TextFieldProps) {
  const [hidden, setHidden] = useState(isPassword);

  return (
    <View style={styles.wrapper}>
      <Text style={styles.label}>{label}</Text>
      <View style={styles.inputRow}>
        <MaterialCommunityIcons name={icon} size={20} color={colors.onSurfaceVariant} style={styles.icon} />
        <TextInput
          style={styles.input}
          placeholderTextColor={colors.outline}
          secureTextEntry={hidden}
          autoCapitalize="none"
          {...inputProps}
        />
        {isPassword && (
          <Pressable onPress={() => setHidden((h) => !h)} style={styles.eyeButton}>
            <MaterialCommunityIcons
              name={hidden ? 'eye-off' : 'eye'}
              size={20}
              color={colors.onSurfaceVariant}
            />
          </Pressable>
        )}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: { gap: 6 },
  label: { ...typography.labelMd, color: colors.onSurface },
  inputRow: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: colors.surfaceContainerLowest,
    borderRadius: radii.md,
    height: 48,
  },
  icon: { marginLeft: 14 },
  input: {
    flex: 1,
    paddingHorizontal: spacing.sm,
    ...typography.bodyMd,
    color: colors.onSurface,
  },
  eyeButton: { padding: 12 },
});