/**
 * Copied 1:1 from the Stitch NutriPath design system, so every screen
 * matches exactly instead of drifting screen by screen.
 */

export const colors = {
  surface: '#ecfef3',
  surfaceContainerLowest: '#ffffff',
  surfaceContainerLow: '#e6f8ed',
  surfaceContainer: '#e0f2e7',
  onSurface: '#101e18',
  onSurfaceVariant: '#404945',
  primary: '#154539',
  onPrimary: '#ffffff',
  primaryContainer: '#2f5d50',
  secondary: '#2f6858',
  secondaryFixed: '#b3efd9',
  onSecondaryFixed: '#002018',
  outline: '#717975',
  emerald: '#10B981',
  amberCaution: '#B45309',
  error: '#ba1a1a',
  onError: '#ffffff',
} as const;

export const typography = {
  displayLg: { fontFamily: 'PlusJakartaSans_700Bold', fontSize: 40, lineHeight: 48 },
  headlineLg: { fontFamily: 'PlusJakartaSans_700Bold', fontSize: 28, lineHeight: 36 },
  headlineMd: { fontFamily: 'PlusJakartaSans_700Bold', fontSize: 22, lineHeight: 28 },
  labelLg: { fontFamily: 'PlusJakartaSans_600SemiBold', fontSize: 14, lineHeight: 18 },
  labelMd: { fontFamily: 'PlusJakartaSans_600SemiBold', fontSize: 12, lineHeight: 16 },
  labelSm: { fontFamily: 'PlusJakartaSans_500Medium', fontSize: 11, lineHeight: 14 },
  bodyMd: { fontFamily: 'Inter_400Regular', fontSize: 14, lineHeight: 20 },
  bodySm: { fontFamily: 'Inter_400Regular', fontSize: 12, lineHeight: 16 },
} as const;

export const spacing = {
  margin: 20,
  xs: 4,
  sm: 8,
  md: 16,
  lg: 24,
  xl: 32,
} as const;

export const radii = {
  md: 12,
  lg: 16,
  pill: 9999,
} as const;