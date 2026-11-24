import React, { useEffect, useRef, useState } from 'react';
import { AccessibilityInfo, Animated, Easing, LayoutChangeEvent, StyleSheet, Text, View } from 'react-native';
import Svg, { Circle, Ellipse, Line, Path, Rect } from 'react-native-svg';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { colors, typography, radii } from '@/theme';

// Scene is drawn on a fixed 280x210 stage and scaled to fit its container.
const STAGE_W = 280;
const STAGE_H = 210;

const SKIN = '#C98B5E';
const HAIR = '#2B2118';
const NIGHT = '#24304F';
const DAY = '#E4F6EC';

type Phase = 0 | 1 | 2; // eat, drink, sleep

const PHASES: { label: string; icon: React.ComponentProps<typeof MaterialCommunityIcons>['name']; ms: number }[] = [
  { label: 'Eat balanced', icon: 'silverware-fork-knife', ms: 4600 },
  { label: 'Stay hydrated', icon: 'cup-water', ms: 2300 },
  { label: 'Rest & recover', icon: 'sleep', ms: 4200 },
];

const ease = Easing.inOut(Easing.quad);
const timing = (value: Animated.Value, toValue: number, duration: number, easing = ease) =>
  Animated.timing(value, { toValue, duration, easing, useNativeDriver: true });

/** Looping cartoon for the landing page: a person eats, drinks water, then sleeps. */
export function DailyRhythmAnimation() {
  const [phase, setPhase] = useState<Phase>(0);
  const [scale, setScale] = useState(1);
  const [reduceMotion, setReduceMotion] = useState(false);

  const lift = useRef(new Animated.Value(0)).current; // arm: 0 resting, 1 at mouth
  const chew = useRef(new Animated.Value(1)).current; // mouth scaleY
  const rest = useRef(new Animated.Value(0)).current; // 0 awake/day, 1 asleep/night
  const holding = useRef(new Animated.Value(0)).current; // 0 spoon, 1 glass
  const zs = useRef([0, 1, 2].map(() => new Animated.Value(0))).current;

  useEffect(() => {
    let mounted = true;
    AccessibilityInfo.isReduceMotionEnabled()
      .then((enabled) => mounted && setReduceMotion(enabled))
      .catch(() => {});
    const sub = AccessibilityInfo.addEventListener('reduceMotionChanged', setReduceMotion);
    return () => {
      mounted = false;
      sub.remove();
    };
  }, []);

  // Advance to the next scene once the current one has played.
  useEffect(() => {
    const t = setTimeout(() => setPhase((p) => ((p + 1) % 3) as Phase), PHASES[phase].ms);
    return () => clearTimeout(t);
  }, [phase]);

  useEffect(() => {
    const loops: Animated.CompositeAnimation[] = [];
    const asleep = phase === 2;

    timing(rest, asleep ? 1 : 0, 700).start();
    timing(holding, phase === 1 ? 1 : 0, 150).start();

    if (!asleep && !reduceMotion) {
      // One bite/sip every 2.3s: raise, hold, lower, pause.
      loops.push(
        Animated.loop(
          Animated.sequence([timing(lift, 1, 650), Animated.delay(300), timing(lift, 0, 650), Animated.delay(700)]),
        ),
      );
    } else {
      timing(lift, 0, 500).start();
    }

    if (phase === 0 && !reduceMotion) {
      const bite = [0, 1, 2].flatMap(() => [timing(chew, 0.35, 150), timing(chew, 1, 150)]);
      loops.push(Animated.loop(Animated.sequence([Animated.delay(1000), ...bite, Animated.delay(400)])));
    } else {
      chew.setValue(1);
    }

    if (asleep && !reduceMotion) {
      loops.push(
        Animated.loop(
          Animated.stagger(
            600,
            zs.map((z) => Animated.sequence([timing(z, 1, 1800, Easing.out(Easing.quad)), timing(z, 0, 0)])),
          ),
        ),
      );
    } else {
      zs.forEach((z) => z.setValue(0));
    }

    loops.forEach((l) => l.start());
    return () => loops.forEach((l) => l.stop());
  }, [phase, reduceMotion, lift, chew, rest, holding, zs]);

  const onLayout = (e: LayoutChangeEvent) => setScale(e.nativeEvent.layout.width / STAGE_W);

  const awake = rest.interpolate({ inputRange: [0, 1], outputRange: [1, 0] });
  const spoon = holding.interpolate({ inputRange: [0, 1], outputRange: [1, 0] });
  const armRotate = lift.interpolate({ inputRange: [0, 1], outputRange: ['110deg', '220deg'] });
  const headTilt = rest.interpolate({ inputRange: [0, 1], outputRange: ['0deg', '-16deg'] });
  const current = PHASES[phase];

  return (
    <View
      style={styles.frame}
      onLayout={onLayout}
      accessible
      accessibilityRole="image"
      accessibilityLabel="Cartoon of a person eating a balanced meal, drinking water, then sleeping"
    >
      <View style={[styles.stage, { transform: [{ scale }] }]}>
        {/* Sky: day colour with a night overlay that fades in while sleeping */}
        <View style={[StyleSheet.absoluteFill, { backgroundColor: DAY }]} />
        <Animated.View style={[StyleSheet.absoluteFill, { backgroundColor: NIGHT, opacity: rest }]} />

        <Animated.View style={[styles.sky, { opacity: awake }]}>
          <Svg width={STAGE_W} height={90}>
            <Circle cx={232} cy={36} r={14} fill="#FFC94A" />
            {[0, 45, 90, 135, 180, 225, 270, 315].map((a) => {
              const r = (a * Math.PI) / 180;
              return (
                <Line
                  key={a}
                  x1={232 + Math.cos(r) * 19}
                  y1={36 + Math.sin(r) * 19}
                  x2={232 + Math.cos(r) * 25}
                  y2={36 + Math.sin(r) * 25}
                  stroke="#FFC94A"
                  strokeWidth={3}
                  strokeLinecap="round"
                />
              );
            })}
            <Ellipse cx={52} cy={40} rx={22} ry={9} fill="#FFFFFF" opacity={0.9} />
            <Ellipse cx={66} cy={34} rx={14} ry={9} fill="#FFFFFF" opacity={0.9} />
          </Svg>
        </Animated.View>
        <Animated.View style={[styles.sky, { opacity: rest }]}>
          <Svg width={STAGE_W} height={90}>
            <Path d="M240 22 A16 16 0 1 0 248 50 A12 12 0 1 1 240 22 Z" fill="#F5E7A1" />
            {[
              [40, 24],
              [78, 46],
              [110, 18],
              [190, 30],
              [205, 62],
              [26, 62],
            ].map(([x, y]) => (
              <Circle key={`${x}-${y}`} cx={x} cy={y} r={1.8} fill="#FFFFFF" />
            ))}
          </Svg>
        </Animated.View>

        {/* Body, table and meal */}
        <Svg width={STAGE_W} height={STAGE_H} style={StyleSheet.absoluteFill}>
          <Rect x={133} y={100} width={14} height={14} fill={SKIN} />
          <Rect x={108} y={106} width={64} height={70} rx={22} fill={colors.primary} />
          <Line x1={114} y1={120} x2={102} y2={150} stroke={colors.primary} strokeWidth={12} strokeLinecap="round" />
          <Rect x={20} y={162} width={240} height={12} rx={4} fill="#B5835A" />
          <Rect x={28} y={174} width={224} height={36} fill="#9C6B45" />
          <Line x1={102} y1={150} x2={118} y2={160} stroke={SKIN} strokeWidth={10} strokeLinecap="round" />
          <Circle cx={120} cy={160} r={6} fill={SKIN} />
          {/* Plate of rice and curry */}
          <Ellipse cx={146} cy={164} rx={38} ry={9} fill="#FFFFFF" stroke="#E3E3E3" strokeWidth={1.5} />
          <Ellipse cx={142} cy={159} rx={16} ry={7} fill="#FFFDF5" stroke="#EDE6D2" strokeWidth={1} />
          <Ellipse cx={164} cy={161} rx={9} ry={4} fill="#F2B53A" />
          <Ellipse cx={124} cy={162} rx={8} ry={3.5} fill="#4C9A4A" />
          <Ellipse cx={155} cy={155} rx={5} ry={3} fill="#C8553D" />
          {/* Water jug */}
          <Rect x={212} y={132} width={22} height={30} rx={4} fill="rgba(160,210,240,0.45)" stroke="#7FB8D8" strokeWidth={1.5} />
          <Rect x={213.5} y={143} width={19} height={17.5} rx={3} fill="#8CCBEB" />
        </Svg>

        {/* Head tilts sideways when asleep */}
        <Animated.View style={[styles.head, { transform: [{ rotate: headTilt }] }]}>
          <Svg width={72} height={72} style={StyleSheet.absoluteFill}>
            <Circle cx={11} cy={39} r={5} fill={SKIN} />
            <Circle cx={61} cy={39} r={5} fill={SKIN} />
            <Circle cx={36} cy={33} r={26} fill={HAIR} />
            <Circle cx={36} cy={38} r={25} fill={SKIN} />
            <Path d="M11 33 Q14 10 36 9 Q58 10 61 33 Q50 21 36 22 Q22 21 11 33 Z" fill={HAIR} />
            <Circle cx={22} cy={47} r={4} fill="#F08A7E" opacity={0.5} />
            <Circle cx={50} cy={47} r={4} fill="#F08A7E" opacity={0.5} />
          </Svg>
          <Animated.View style={[StyleSheet.absoluteFill, { opacity: awake }]}>
            <Svg width={72} height={72}>
              <Circle cx={27} cy={38} r={3.2} fill={HAIR} />
              <Circle cx={45} cy={38} r={3.2} fill={HAIR} />
              <Circle cx={28} cy={37} r={1} fill="#FFFFFF" />
              <Circle cx={46} cy={37} r={1} fill="#FFFFFF" />
            </Svg>
          </Animated.View>
          <Animated.View style={[StyleSheet.absoluteFill, { opacity: rest }]}>
            <Svg width={72} height={72}>
              <Path d="M23 38 Q27 41 31 38" stroke={HAIR} strokeWidth={2} fill="none" strokeLinecap="round" />
              <Path d="M41 38 Q45 41 49 38" stroke={HAIR} strokeWidth={2} fill="none" strokeLinecap="round" />
            </Svg>
          </Animated.View>
          <Animated.View style={[styles.mouth, { transform: [{ scaleY: chew }] }]}>
            <Svg width={16} height={9}>
              <Path d="M2 2 Q8 10 14 2 Z" fill="#8A3A30" />
            </Svg>
          </Animated.View>
        </Animated.View>

        {/* Right arm swings from the plate to the mouth, holding a spoon or a glass */}
        <Animated.View style={[styles.arm, { transform: [{ rotate: armRotate }] }]}>
          <Svg width={100} height={30} style={StyleSheet.absoluteFill}>
            <Line x1={50} y1={15} x2={62} y2={15} stroke={colors.primary} strokeWidth={13} strokeLinecap="round" />
            <Line x1={60} y1={15} x2={86} y2={15} stroke={SKIN} strokeWidth={10} strokeLinecap="round" />
          </Svg>
          <Animated.View style={[StyleSheet.absoluteFill, { opacity: spoon }]}>
            <Svg width={100} height={30}>
              <Line x1={80} y1={15} x2={90} y2={15} stroke="#B9BFC8" strokeWidth={2.5} />
              <Ellipse cx={93} cy={15} rx={5} ry={3.6} fill="#C9CED6" />
              <Ellipse cx={93} cy={14} rx={3.4} ry={2.2} fill="#FFFDF5" />
            </Svg>
          </Animated.View>
          <Animated.View style={[StyleSheet.absoluteFill, { opacity: holding }]}>
            <Svg width={100} height={30}>
              <Rect x={86} y={6} width={11} height={18} rx={2} fill="rgba(160,210,240,0.55)" stroke="#7FB8D8" strokeWidth={1.2} />
              <Rect x={87} y={12} width={9} height={11} rx={1.5} fill="#8CCBEB" />
            </Svg>
          </Animated.View>
          <Svg width={100} height={30} style={StyleSheet.absoluteFill}>
            <Circle cx={86} cy={15} r={6.5} fill={SKIN} />
          </Svg>
        </Animated.View>

        {/* Floating Zzz while sleeping */}
        <Animated.View style={[styles.zzz, { opacity: rest }]} pointerEvents="none">
          {zs.map((z, i) => (
            <Animated.Text
              key={i}
              style={[
                styles.z,
                {
                  fontSize: 12 + i * 4,
                  left: i * 10,
                  top: 30 - i * 8,
                  opacity: z.interpolate({ inputRange: [0, 0.2, 0.8, 1], outputRange: [0, 1, 1, 0] }),
                  transform: [
                    { translateY: z.interpolate({ inputRange: [0, 1], outputRange: [0, -26] }) },
                    { translateX: z.interpolate({ inputRange: [0, 1], outputRange: [0, 10] }) },
                  ],
                },
              ]}
            >
              Z
            </Animated.Text>
          ))}
        </Animated.View>
      </View>

      <View style={styles.phaseTag}>
        <MaterialCommunityIcons name={current.icon} size={14} color={colors.onSecondaryFixed} />
        <Text style={styles.phaseText}>{current.label}</Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  frame: { width: '100%', aspectRatio: STAGE_W / STAGE_H, overflow: 'hidden', backgroundColor: DAY },
  stage: {
    position: 'absolute',
    width: STAGE_W,
    height: STAGE_H,
    left: '50%',
    top: '50%',
    marginLeft: -STAGE_W / 2,
    marginTop: -STAGE_H / 2,
  },
  sky: { position: 'absolute', left: 0, top: 0 },
  head: { position: 'absolute', left: 104, top: 49, width: 72, height: 72, transformOrigin: '50% 95%' },
  mouth: { position: 'absolute', left: 28, top: 47, width: 16, height: 9 },
  arm: { position: 'absolute', left: 115, top: 103, width: 100, height: 30 },
  zzz: { position: 'absolute', left: 176, top: 30, width: 60, height: 60 },
  z: { position: 'absolute', color: '#F5E7A1', fontWeight: '800' },
  phaseTag: {
    position: 'absolute',
    top: 12,
    left: 12,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: 'rgba(179,239,217,0.92)',
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: radii.pill,
  },
  phaseText: { ...typography.labelSm, color: colors.onSecondaryFixed, fontWeight: '600' },
});
