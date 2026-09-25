import React, { useEffect, useRef } from 'react';
import { Animated, Pressable, StyleSheet, Text, View } from 'react-native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Avatar } from '@/components/Avatar';
import { AVATAR_GROUPS, AvatarOption } from '@/constants/avatars';
import { colors, spacing, typography } from '@/theme';

const TILE = 72;

function AvatarTile({ avatar, selected, onPress }: { avatar: AvatarOption; selected: boolean; onPress: () => void }) {
  // A small "pop" when an avatar is picked.
  const scale = useRef(new Animated.Value(1)).current;
  useEffect(() => {
    if (!selected) return;
    Animated.sequence([
      Animated.timing(scale, { toValue: 1.12, duration: 110, useNativeDriver: true }),
      Animated.spring(scale, { toValue: 1, friction: 4, useNativeDriver: true }),
    ]).start();
  }, [selected, scale]);

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="radio"
      accessibilityState={{ selected }}
      accessibilityLabel={avatar.label}
      style={({ pressed }) => [styles.tile, pressed && { opacity: 0.8 }]}
    >
      <Animated.View style={[styles.ring, selected && styles.ringSelected, { transform: [{ scale }] }]}>
        <Avatar avatarId={avatar.id} size={56} />
        {selected && (
          <View style={styles.check}>
            <MaterialCommunityIcons name="check" size={14} color={colors.onPrimary} />
          </View>
        )}
      </Animated.View>
      <Text style={[styles.label, selected && styles.labelSelected]} numberOfLines={1}>
        {avatar.label}
      </Text>
    </Pressable>
  );
}

/**
 * All avatars, grouped, as a grid of tappable tiles. Tiles are a fixed
 * size and wrap, so the grid fits any screen width, from a small phone to
 * a browser window.
 */
export function AvatarPicker({ selected, onSelect }: { selected: string | null; onSelect: (id: string) => void }) {
  return (
    <View style={styles.container}>
      {AVATAR_GROUPS.map((group) => (
        <View key={group.title} style={styles.group}>
          <Text style={styles.groupTitle}>{group.title}</Text>
          <View style={styles.grid}>
            {group.avatars.map((avatar) => (
              <AvatarTile key={avatar.id} avatar={avatar} selected={selected === avatar.id} onPress={() => onSelect(avatar.id)} />
            ))}
          </View>
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { gap: spacing.md },
  group: { gap: spacing.xs },
  groupTitle: { ...typography.labelMd, color: colors.onSurfaceVariant },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  tile: { width: TILE, alignItems: 'center', paddingVertical: 4 },
  ring: {
    padding: 3,
    borderRadius: 34,
    borderWidth: 2,
    borderColor: 'transparent',
  },
  ringSelected: { borderColor: colors.primary },
  check: {
    position: 'absolute',
    right: -2,
    bottom: -2,
    width: 22,
    height: 22,
    borderRadius: 11,
    backgroundColor: colors.primary,
    borderWidth: 2,
    borderColor: colors.surfaceContainerLowest,
    alignItems: 'center',
    justifyContent: 'center',
  },
  label: { ...typography.labelSm, color: colors.onSurfaceVariant, marginTop: 4, maxWidth: TILE },
  labelSelected: { color: colors.primary, fontFamily: 'PlusJakartaSans_600SemiBold' },
});
