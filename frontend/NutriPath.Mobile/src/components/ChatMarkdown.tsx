import React from 'react';
import { StyleSheet, Text, TextStyle, View } from 'react-native';
import { spacing, typography } from '@/theme';

// "**bold**" and "*italic*" inside one line. Unmatched stars are left as-is.
function renderInline(text: string, keyPrefix: string) {
  return text.split(/(\*\*[^*]+\*\*|\*[^*\s][^*]*\*)/g).map((part, i) => {
    if (part.startsWith('**') && part.endsWith('**') && part.length > 4)
      return <Text key={`${keyPrefix}-${i}`} style={styles.bold}>{part.slice(2, -2)}</Text>;
    if (part.startsWith('*') && part.endsWith('*') && part.length > 2)
      return <Text key={`${keyPrefix}-${i}`} style={styles.italic}>{part.slice(1, -1)}</Text>;
    return part;
  });
}

/**
 * Shows the assistant's replies, which use a small subset of Markdown:
 * bold, italic, "- " bullets, "1. " lists and "#" headings. A full
 * Markdown library would be overkill for a chat bubble.
 */
export function ChatMarkdown({ text, style }: { text: string; style: TextStyle }) {
  const lines = text.replace(/\r\n/g, '\n').trim().split('\n');

  return (
    <View style={styles.container}>
      {lines.map((raw, i) => {
        const line = raw.trim();
        const key = `l${i}`;
        if (!line) return <View key={key} style={styles.gap} />;
        if (/^(-{3,}|\*{3,}|_{3,})$/.test(line)) return null; // horizontal rules

        const heading = line.match(/^#{1,6}\s+(.*)$/);
        if (heading)
          return (
            <Text key={key} style={[style, styles.heading]}>
              {renderInline(heading[1].replace(/\*\*/g, ''), key)}
            </Text>
          );

        const bullet = raw.match(/^(\s*)[-*•]\s+(.*)$/);
        const numbered = raw.match(/^(\s*)(\d+)[.)]\s+(.*)$/);
        if (bullet || numbered) {
          const indent = (bullet ?? numbered)![1].length >= 2;
          const marker = bullet ? '•' : `${numbered![2]}.`;
          const body = bullet ? bullet[2] : numbered![3];
          return (
            <View key={key} style={[styles.listRow, indent && styles.indented]}>
              <Text style={[style, styles.marker]}>{marker}</Text>
              <Text style={[style, styles.listText]}>{renderInline(body, key)}</Text>
            </View>
          );
        }

        return (
          <Text key={key} style={style}>
            {renderInline(line, key)}
          </Text>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { gap: 2 },
  gap: { height: spacing.xs },
  bold: { fontFamily: 'Inter_600SemiBold' },
  italic: { fontStyle: 'italic' },
  heading: { ...typography.labelLg, marginTop: spacing.xs },
  listRow: { flexDirection: 'row', gap: 6, paddingLeft: 2 },
  indented: { paddingLeft: spacing.md },
  marker: { minWidth: 12 },
  listText: { flex: 1 },
});
