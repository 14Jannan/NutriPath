import React from 'react';
import { View, StyleSheet } from 'react-native';
import { colors, radii } from '@/theme';

interface ProgressBarProps {
  progress: number; // 0 to 1
  color?: string;
  trackColor?: string;
  height?: number;
}

export function ProgressBar({ progress, color = colors.primary, trackColor, height = 6 }: ProgressBarProps) {
  // Clamp so a bad value (e.g. 1.4) can never render the bar outside its track.
  const clamped = Math.max(0, Math.min(1, progress));
  return (
    <View style={[styles.track, { height, backgroundColor: trackColor ?? colors.surfaceContainer }]}>
      <View style={[styles.fill, { width: `${clamped * 100}%`, backgroundColor: color, height }]} />
    </View>
  );
}

const styles = StyleSheet.create({
  track: { width: '100%', borderRadius: radii.pill, overflow: 'hidden' },
  fill: { borderRadius: radii.pill },
});
