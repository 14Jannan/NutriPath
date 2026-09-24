import React, { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TextInput, Pressable, KeyboardAvoidingView, Platform } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Card } from '@/components/Card';
import { colors, typography, spacing, radii } from '@/theme';

interface ChatMessage {
  id: string;
  role: 'assistant' | 'user';
  text: string;
}

const QUICK_PROMPTS = [
  { icon: 'silverware-fork-knife' as const, label: 'What should I eat tonight?' },
  { icon: 'chart-line' as const, label: 'Explain my score' },
  { icon: 'bread-slice-outline' as const, label: 'Suggest a high-fibre snack' },
  { icon: 'fire' as const, label: 'Healthy Kottu alternatives' },
];

// MOCK: the opening summary is built from real daily totals in Phase 13.
const initialMessages: ChatMessage[] = [
  {
    id: '1',
    role: 'assistant',
    text: "You've had 1,480 kcal today with 68g protein and 26g fibre. How can I help you finish strong tonight?",
  },
];

export function AssistantScreen() {
  const [messages, setMessages] = useState<ChatMessage[]>(initialMessages);
  const [input, setInput] = useState('');

  function sendMessage(text: string) {
    if (!text.trim()) return;

    const userMessage: ChatMessage = { id: Date.now().toString(), role: 'user', text };
    setMessages((prev) => [...prev, userMessage]);
    setInput('');

    // Placeholder reply — Phase 13 replaces this with a real call to
    // POST /api/ai/chat, which builds structured context from the
    // Nutrition module and sends it to Groq. The backend, not this
    // screen, decides what the AI is allowed to say.
    setTimeout(() => {
      const reply: ChatMessage = {
        id: (Date.now() + 1).toString(),
        role: 'assistant',
        text: 'Real AI responses arrive in Phase 13, grounded in your actual logged nutrition data rather than made up here.',
      };
      setMessages((prev) => [...prev, reply]);
    }, 500);
  }

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <View style={styles.disclaimerBar}>
        <MaterialCommunityIcons name="information-outline" size={14} color={colors.outline} />
        <Text style={styles.disclaimerText}>AI Wellness Assistant · General wellness guidance, not medical advice.</Text>
      </View>

      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={{ flex: 1 }}>
        <ScrollView contentContainerStyle={styles.messages}>
          {messages.map((message) => (
            <View
              key={message.id}
              style={[styles.bubbleRow, message.role === 'user' && styles.bubbleRowUser]}
            >
              {message.role === 'assistant' && (
                <View style={styles.aiAvatar}>
                  <MaterialCommunityIcons name="robot-happy-outline" size={16} color={colors.onPrimary} />
                </View>
              )}
              <View
                style={[
                  styles.bubble,
                  message.role === 'user' ? styles.bubbleUser : styles.bubbleAssistant,
                ]}
              >
                <Text style={message.role === 'user' ? styles.bubbleTextUser : styles.bubbleTextAssistant}>
                  {message.text}
                </Text>
              </View>
            </View>
          ))}

          <View style={styles.quickPrompts}>
            {QUICK_PROMPTS.map((prompt) => (
              <Pressable
                key={prompt.label}
                style={styles.promptChip}
                onPress={() => sendMessage(prompt.label)}
              >
                <MaterialCommunityIcons name={prompt.icon} size={14} color={colors.secondary} />
                <Text style={styles.promptChipText}>{prompt.label}</Text>
              </Pressable>
            ))}
          </View>
        </ScrollView>

        <Card style={styles.inputBar}>
          <TextInput
            style={styles.input}
            placeholder="Ask NutriPath anything..."
            placeholderTextColor={colors.outline}
            value={input}
            onChangeText={setInput}
            onSubmitEditing={() => sendMessage(input)}
          />
          <Pressable style={styles.sendButton} onPress={() => sendMessage(input)}>
            <MaterialCommunityIcons name="arrow-up" size={18} color={colors.onPrimary} />
          </Pressable>
        </Card>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  disclaimerBar: {
    flexDirection: 'row',
    justifyContent: 'center',
    gap: 6,
    paddingVertical: spacing.xs,
    backgroundColor: colors.surfaceContainerLow,
  },
  disclaimerText: { ...typography.bodySm, color: colors.outline },
  messages: { padding: spacing.margin, gap: spacing.sm },
  bubbleRow: { flexDirection: 'row', gap: 8, alignItems: 'flex-end' },
  bubbleRowUser: { justifyContent: 'flex-end' },
  aiAvatar: {
    width: 28,
    height: 28,
    borderRadius: 14,
    backgroundColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  bubble: { maxWidth: '78%', padding: spacing.sm, borderRadius: radii.lg },
  bubbleAssistant: { backgroundColor: colors.surfaceContainerLowest, borderBottomLeftRadius: 4 },
  bubbleUser: { backgroundColor: colors.primary, borderBottomRightRadius: 4 },
  bubbleTextAssistant: { ...typography.bodyMd, color: colors.onSurface },
  bubbleTextUser: { ...typography.bodyMd, color: colors.onPrimary },
  quickPrompts: { flexDirection: 'row', flexWrap: 'wrap', gap: 8, marginTop: spacing.xs },
  promptChip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    backgroundColor: colors.surfaceContainerLowest,
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: radii.pill,
  },
  promptChipText: { ...typography.labelSm, color: colors.onSurface },
  inputBar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    margin: spacing.margin,
    marginTop: 0,
  },
  input: { flex: 1, ...typography.bodyMd, color: colors.onSurface },
  sendButton: {
    width: 36,
    height: 36,
    borderRadius: 18,
    backgroundColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
  },
});
