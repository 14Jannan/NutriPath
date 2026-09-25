import React from 'react';
import { StyleSheet, View } from 'react-native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { getAvatar } from '@/constants/avatars';
import { colors } from '@/theme';

interface AvatarProps {
  avatarId?: string | null;
  size?: number;
}

/** A user's chosen avatar in a circle; a neutral person icon if none is set yet. */
export function Avatar({ avatarId, size = 64 }: AvatarProps) {
  const avatar = getAvatar(avatarId);
  return (
    <View
      style={[
        styles.circle,
        { width: size, height: size, borderRadius: size / 2 },
        { backgroundColor: avatar?.background ?? colors.primary },
      ]}
      accessibilityRole="image"
      accessibilityLabel={avatar ? `${avatar.label} avatar` : 'Default avatar'}
    >
      <MaterialCommunityIcons
        name={avatar?.icon ?? 'account'}
        size={Math.round(size * 0.55)}
        color={avatar?.foreground ?? colors.onPrimary}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  circle: { alignItems: 'center', justifyContent: 'center' },
});
