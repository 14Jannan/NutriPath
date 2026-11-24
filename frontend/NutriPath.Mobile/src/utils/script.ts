import type { TextStyle } from 'react-native';

const TAMIL = /[஀-௿]/;
const SINHALA = /[඀-෿]/;

/** True when the text contains Tamil or Sinhala letters. */
export const hasIndicScript = (text: string) => TAMIL.test(text) || SINHALA.test(text);

/**
 * Tamil and Sinhala glyphs stack vowel signs above and below the base letter, so the
 * Latin-tuned line heights in the theme clip them, especially on Android. This raises the
 * line height for text in those scripts. The custom Latin fonts have no Tamil or Sinhala
 * glyphs; Android, iOS and browsers fall back to their system fonts for these characters.
 */
export function scriptAwareStyle(text: string, base: TextStyle): TextStyle | undefined {
  if (!hasIndicScript(text)) return undefined;
  const fontSize = base.fontSize ?? 14;
  return { lineHeight: Math.round(fontSize * 1.65) };
}
