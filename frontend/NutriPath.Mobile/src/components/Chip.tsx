import React from 'react';
import { Pressable, Text, StyleSheet } from 'react-native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { colors, typography, radii } from '@/theme';

interface ChipProps {
  label: string;
  icon: keyof typeof MaterialCommunityIcons.glyphMap;
  selected: boolean;
  onPress: () => void;
}

export function Chip({ label, icon, selected, onPress }: ChipProps) {
  return (
    <Pressable
      onPress={onPress}
      style={[styles.chip, selected ? styles.selected : styles.unselected]}
    >
      <MaterialCommunityIcons
        name={icon}
        size={16}
        color={selected ? colors.onPrimary : colors.onSurface}
      />
      <Text style={[styles.label, { color: selected ? colors.onPrimary : colors.onSurface }]}>
        {label}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    paddingHorizontal: 14,
    paddingVertical: 8,
    borderRadius: radii.pill,
  },
  selected: { backgroundColor: colors.primary },
  unselected: { backgroundColor: colors.surfaceContainerLowest },
  label: { ...typography.labelMd },
});