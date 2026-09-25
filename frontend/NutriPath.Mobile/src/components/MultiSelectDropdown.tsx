import React, { useState } from 'react';
import { View, Text, StyleSheet, Pressable, TextInput } from 'react-native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { colors, typography, spacing, radii } from '@/theme';

export const OTHER_OPTION = 'Other';
export const NONE_OPTION = 'None';

interface MultiSelectDropdownProps {
  options: readonly string[];
  selected: string[];
  onChange: (selected: string[]) => void;
  // Free text shown when "Other" is ticked, e.g. "kiwi, latex".
  otherText: string;
  onOtherTextChange: (text: string) => void;
  placeholder: string;
  otherPlaceholder: string;
}

/**
 * A tap-to-open list of checkboxes. "None" is exclusive (it clears the
 * rest, and picking anything else clears it); "Other" reveals a text box
 * so users can add something the list doesn't cover.
 */
export function MultiSelectDropdown({
  options,
  selected,
  onChange,
  otherText,
  onOtherTextChange,
  placeholder,
  otherPlaceholder,
}: MultiSelectDropdownProps) {
  const [open, setOpen] = useState(false);

  function toggle(option: string) {
    if (selected.includes(option)) {
      onChange(selected.filter((s) => s !== option));
    } else if (option === NONE_OPTION) {
      onChange([NONE_OPTION]);
      onOtherTextChange('');
    } else {
      onChange([...selected.filter((s) => s !== NONE_OPTION), option]);
    }
  }

  const summary = selected
    .map((s) => (s === OTHER_OPTION && otherText.trim() ? otherText.trim() : s))
    .join(', ');

  return (
    <View>
      <Pressable
        style={[styles.field, open && styles.fieldOpen]}
        onPress={() => setOpen((o) => !o)}
        accessibilityRole="button"
        accessibilityState={{ expanded: open }}
      >
        <Text style={[styles.fieldText, !summary && styles.placeholder]} numberOfLines={1}>
          {summary || placeholder}
        </Text>
        <MaterialCommunityIcons name={open ? 'chevron-up' : 'chevron-down'} size={20} color={colors.onSurfaceVariant} />
      </Pressable>

      {open && (
        <View style={styles.list}>
          {options.map((option) => {
            const checked = selected.includes(option);
            return (
              <Pressable
                key={option}
                style={styles.row}
                onPress={() => toggle(option)}
                accessibilityRole="checkbox"
                accessibilityState={{ checked }}
              >
                <MaterialCommunityIcons
                  name={checked ? 'checkbox-marked' : 'checkbox-blank-outline'}
                  size={20}
                  color={checked ? colors.primary : colors.outline}
                />
                <Text style={styles.rowText}>{option}</Text>
              </Pressable>
            );
          })}
          <Pressable style={styles.doneButton} onPress={() => setOpen(false)}>
            <Text style={styles.doneText}>Done</Text>
          </Pressable>
        </View>
      )}

      {selected.includes(OTHER_OPTION) && (
        <TextInput
          style={[styles.field, styles.otherInput]}
          value={otherText}
          onChangeText={onOtherTextChange}
          placeholder={otherPlaceholder}
          placeholderTextColor={colors.outline}
        />
      )}
    </View>
  );
}

/**
 * Splits a saved list into the known options plus free text for "Other",
 * so an existing profile pre-fills the dropdown correctly.
 */
export function splitKnownAndOther(saved: string[], options: readonly string[]) {
  const known = saved.filter((s) => options.includes(s));
  const other = saved.filter((s) => !options.includes(s));
  return {
    selected: other.length > 0 ? [...known, OTHER_OPTION] : known,
    otherText: other.join(', '),
  };
}

/** The inverse: the list to save, with "Other" replaced by what was typed. */
export function combineKnownAndOther(selected: string[], otherText: string): string[] {
  const typed = otherText
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean);
  return [...selected.filter((s) => s !== OTHER_OPTION && s !== NONE_OPTION), ...(selected.includes(OTHER_OPTION) ? typed : [])];
}

const styles = StyleSheet.create({
  field: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: colors.surfaceContainerLow,
    borderRadius: radii.md,
    paddingHorizontal: spacing.sm,
    minHeight: 44,
  },
  fieldOpen: { borderWidth: 1, borderColor: colors.primary },
  fieldText: { ...typography.bodyMd, color: colors.onSurface, flex: 1 },
  placeholder: { color: colors.outline },
  list: {
    marginTop: 4,
    backgroundColor: colors.surfaceContainerLowest,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.surfaceContainer,
    paddingVertical: spacing.xs,
  },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingHorizontal: spacing.sm, paddingVertical: 8 },
  rowText: { ...typography.bodyMd, color: colors.onSurface },
  doneButton: { alignSelf: 'flex-end', paddingHorizontal: spacing.md, paddingVertical: spacing.xs },
  doneText: { ...typography.labelLg, color: colors.primary },
  otherInput: { marginTop: spacing.xs, ...typography.bodyMd, color: colors.onSurface },
});
