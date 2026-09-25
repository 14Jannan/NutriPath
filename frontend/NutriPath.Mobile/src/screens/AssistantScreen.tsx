import React, { useEffect, useRef, useState } from 'react';
import {
  ActivityIndicator,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Card } from '@/components/Card';
import { ChatHistorySheet } from '@/components/ChatHistorySheet';
import * as aiApi from '@/api/aiApi';
import type { ChatHistoryMessage } from '@/api/aiApi';
import { describeApiError } from '@/api/client';
import { showAlert } from '@/utils/alert';
import { describeDay, toLocalIsoDate } from '@/utils/date';
import { colors, typography, spacing, radii } from '@/theme';

interface ChatMessage {
  id: string;
  role: 'assistant' | 'user';
  text: string;
  createdAt: Date;
  failed?: boolean;
}

const QUICK_PROMPTS = [
  { icon: 'silverware-fork-knife' as const, label: 'What should I eat now?' },
  { icon: 'chart-line' as const, label: 'Explain my score' },
  { icon: 'bread-slice-outline' as const, label: 'Suggest a high-fibre snack' },
  { icon: 'calendar-today' as const, label: 'How am I doing today?' },
];

// A neutral greeting rather than hard-coded numbers — real figures only
// ever come from the backend, via the assistant's grounded replies.
const welcome = (): ChatMessage => ({
  id: 'welcome',
  role: 'assistant',
  text: 'Hi! Ask me about your meals today, your weekly score, or what to eat next.',
  createdAt: new Date(),
});

const fromHistory = (h: ChatHistoryMessage): ChatMessage => ({
  id: h.id,
  role: h.role.toLowerCase() === 'user' ? 'user' : 'assistant',
  text: h.content,
  // The server sends UTC without a zone marker; make that explicit.
  createdAt: new Date(h.createdAtUtc.endsWith('Z') ? h.createdAtUtc : `${h.createdAtUtc}Z`),
});

const timeOf = (d: Date) => d.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });

export function AssistantScreen() {
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([welcome()]);
  const [input, setInput] = useState('');
  const [thinking, setThinking] = useState(false);
  const [loadingChat, setLoadingChat] = useState(true);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [highlightId, setHighlightId] = useState<string | null>(null);

  const scrollRef = useRef<ScrollView>(null);
  // Each message's vertical position, so a search result can scroll to it.
  const positions = useRef(new Map<string, number>());

  async function openConversation(id: string, messageId?: string) {
    setHistoryOpen(false);
    setLoadingChat(true);
    try {
      const history = await aiApi.getConversationMessages(id);
      positions.current.clear();
      setConversationId(id);
      setMessages(history.length ? history.map(fromHistory) : [welcome()]);
      setHighlightId(messageId ?? null);
    } catch (error) {
      showAlert("Couldn't open that chat", describeApiError(error));
    } finally {
      setLoadingChat(false);
    }
  }

  // Open the most recent conversation once. Tab screens stay mounted, so
  // this doesn't re-run on tab switches.
  useEffect(() => {
    aiApi
      .listConversations()
      .then((list) => (list.length ? openConversation(list[0].id) : setLoadingChat(false)))
      .catch(() => setLoadingChat(false)); // chat still works without history
  }, []);

  // Jump to a message opened from search, then fade its highlight.
  useEffect(() => {
    if (!highlightId) return;
    const scroll = setTimeout(() => {
      const y = positions.current.get(highlightId);
      if (y !== undefined) scrollRef.current?.scrollTo({ y: Math.max(0, y - 80), animated: true });
    }, 150);
    const clear = setTimeout(() => setHighlightId(null), 3000);
    return () => {
      clearTimeout(scroll);
      clearTimeout(clear);
    };
  }, [highlightId, messages]);

  async function startNewChat() {
    try {
      const id = await aiApi.startNewConversation();
      positions.current.clear();
      setConversationId(id);
      setMessages([welcome()]);
      setHighlightId(null);
      showAlert('New chat started', 'Earlier chats are in History.', 'success');
    } catch (error) {
      showAlert("Couldn't start a new chat", describeApiError(error));
    }
  }

  function onChatDeleted(id: string) {
    if (id === conversationId) {
      // The open chat is gone, so switch to a fresh one. Without an id the
      // server would continue the latest remaining chat instead.
      setConversationId(null);
      setMessages([welcome()]);
      void aiApi.startNewConversation().then(setConversationId).catch(() => {});
    }
  }

  function sendMessage(text: string) {
    const question = text.trim();
    if (!question || thinking) return;

    setHighlightId(null);
    setMessages((prev) => [
      ...prev,
      { id: `local-${Date.now()}`, role: 'user', text: question, createdAt: new Date() },
    ]);
    setInput('');
    setThinking(true);

    // The backend builds the grounded context (the user's real numbers,
    // local date and time, and the chat so far) — this screen only shows it.
    aiApi
      .askAssistant(question, conversationId)
      .then((response) => {
        setConversationId(response.conversationId);
        setMessages((prev) => [
          ...prev,
          { id: `local-${Date.now()}-a`, role: 'assistant', text: response.answer, createdAt: new Date() },
        ]);
      })
      .catch((error) => {
        setMessages((prev) => [
          ...prev,
          {
            id: `local-${Date.now()}-e`,
            role: 'assistant',
            text: `Sorry, I couldn't answer that. ${describeApiError(error)}`,
            createdAt: new Date(),
            failed: true,
          },
        ]);
      })
      .finally(() => setThinking(false));
  }

  const hasQuestions = messages.some((m) => m.role === 'user');

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <View style={styles.topBar}>
        <View style={styles.topBarInner}>
          <View style={{ flex: 1 }}>
            <Text style={styles.title}>Assistant</Text>
            <Text style={styles.disclaimerText}>General wellness guidance, not medical advice.</Text>
          </View>
          <Pressable
            onPress={() => setHistoryOpen(true)}
            style={({ pressed }) => [styles.topButton, pressed && styles.pressed]}
            accessibilityLabel="Chat history"
          >
            <MaterialCommunityIcons name="history" size={20} color={colors.primary} />
            <Text style={styles.topButtonText}>History</Text>
          </Pressable>
          <Pressable
            onPress={startNewChat}
            style={({ pressed }) => [styles.topButton, pressed && styles.pressed]}
            accessibilityLabel="Start a new chat"
          >
            <MaterialCommunityIcons name="chat-plus-outline" size={20} color={colors.primary} />
            <Text style={styles.topButtonText}>New</Text>
          </Pressable>
        </View>
      </View>

      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={{ flex: 1 }}>
        {loadingChat ? (
          <ActivityIndicator color={colors.primary} style={{ marginTop: spacing.xl }} />
        ) : (
          <ScrollView
            ref={scrollRef}
            contentContainerStyle={styles.messages}
            keyboardShouldPersistTaps="handled"
            // Follow new messages, except while showing a search result.
            onContentSizeChange={() => !highlightId && scrollRef.current?.scrollToEnd({ animated: true })}
          >
            {messages.map((message, index) => {
              const day = toLocalIsoDate(message.createdAt);
              const newDay = index === 0 || toLocalIsoDate(messages[index - 1].createdAt) !== day;
              const isUser = message.role === 'user';
              return (
                <View
                  key={message.id}
                  onLayout={(e) => positions.current.set(message.id, e.nativeEvent.layout.y)}
                >
                  {newDay && (
                    <View style={styles.dayDivider}>
                      <Text style={styles.dayDividerText}>{describeDay(day)}</Text>
                    </View>
                  )}
                  <View style={[styles.bubbleRow, isUser && styles.bubbleRowUser]}>
                    {!isUser && (
                      <View style={styles.aiAvatar}>
                        <MaterialCommunityIcons name="robot-happy-outline" size={16} color={colors.onPrimary} />
                      </View>
                    )}
                    <View style={[styles.bubbleColumn, isUser && { alignItems: 'flex-end' }]}>
                      <View
                        style={[
                          styles.bubble,
                          isUser ? styles.bubbleUser : styles.bubbleAssistant,
                          message.failed && styles.bubbleFailed,
                          message.id === highlightId && styles.bubbleHighlight,
                        ]}
                      >
                        <Text style={isUser ? styles.bubbleTextUser : styles.bubbleTextAssistant}>{message.text}</Text>
                      </View>
                      <Text style={styles.time}>{timeOf(message.createdAt)}</Text>
                    </View>
                  </View>
                </View>
              );
            })}

            {thinking && (
              <View style={styles.bubbleRow}>
                <View style={styles.aiAvatar}>
                  <MaterialCommunityIcons name="robot-happy-outline" size={16} color={colors.onPrimary} />
                </View>
                <View style={[styles.bubble, styles.bubbleAssistant, styles.thinking]}>
                  <ActivityIndicator size="small" color={colors.primary} />
                  <Text style={styles.thinkingText}>Thinking...</Text>
                </View>
              </View>
            )}

            {!hasQuestions && !thinking && (
              <View style={styles.quickPrompts}>
                {QUICK_PROMPTS.map((prompt) => (
                  <Pressable
                    key={prompt.label}
                    style={({ pressed }) => [styles.promptChip, pressed && styles.pressed]}
                    onPress={() => sendMessage(prompt.label)}
                  >
                    <MaterialCommunityIcons name={prompt.icon} size={14} color={colors.secondary} />
                    <Text style={styles.promptChipText}>{prompt.label}</Text>
                  </Pressable>
                ))}
              </View>
            )}
          </ScrollView>
        )}

        <View style={styles.inputWrap}>
          <Card style={styles.inputBar}>
            <TextInput
              style={styles.input}
              placeholder="Ask NutriPath anything..."
              placeholderTextColor={colors.outline}
              value={input}
              onChangeText={setInput}
              onSubmitEditing={() => sendMessage(input)}
              multiline
              maxLength={1000}
            />
            <Pressable
              style={[styles.sendButton, (!input.trim() || thinking) && { opacity: 0.4 }]}
              onPress={() => sendMessage(input)}
              disabled={!input.trim() || thinking}
              accessibilityLabel="Send"
            >
              <MaterialCommunityIcons name="arrow-up" size={18} color={colors.onPrimary} />
            </Pressable>
          </Card>
        </View>
      </KeyboardAvoidingView>

      <ChatHistorySheet
        visible={historyOpen}
        currentConversationId={conversationId}
        onClose={() => setHistoryOpen(false)}
        onOpen={openConversation}
        onDeleted={onChatDeleted}
      />
    </SafeAreaView>
  );
}

// Phone-width content on a wide browser window.
const MAX_WIDTH = 720;

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  topBar: {
    paddingHorizontal: spacing.margin,
    paddingVertical: spacing.xs,
    backgroundColor: colors.surfaceContainerLow,
  },
  topBarInner: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, width: '100%', maxWidth: MAX_WIDTH, alignSelf: 'center' },
  title: { ...typography.headlineMd, fontSize: 18, color: colors.onSurface },
  disclaimerText: { ...typography.labelSm, color: colors.outline },
  topButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    paddingHorizontal: 10,
    minHeight: 40,
    borderRadius: radii.pill,
    backgroundColor: colors.surfaceContainerLowest,
  },
  topButtonText: { ...typography.labelMd, color: colors.primary },
  pressed: { opacity: 0.8 },
  messages: { padding: spacing.margin, gap: spacing.sm, width: '100%', maxWidth: MAX_WIDTH, alignSelf: 'center' },
  dayDivider: { alignItems: 'center', marginVertical: spacing.xs },
  dayDividerText: {
    ...typography.labelSm,
    color: colors.onSurfaceVariant,
    backgroundColor: colors.surfaceContainer,
    paddingHorizontal: 10,
    paddingVertical: 3,
    borderRadius: radii.pill,
    overflow: 'hidden',
  },
  bubbleRow: { flexDirection: 'row', gap: 8, alignItems: 'flex-start' },
  bubbleRowUser: { justifyContent: 'flex-end' },
  bubbleColumn: { maxWidth: '80%' },
  aiAvatar: {
    width: 28,
    height: 28,
    borderRadius: 14,
    backgroundColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  bubble: { padding: spacing.sm, borderRadius: radii.lg, borderWidth: 2, borderColor: 'transparent' },
  bubbleAssistant: { backgroundColor: colors.surfaceContainerLowest, borderBottomLeftRadius: 4 },
  bubbleUser: { backgroundColor: colors.primary, borderBottomRightRadius: 4 },
  bubbleFailed: { borderColor: colors.amberCaution },
  bubbleHighlight: { borderColor: colors.emerald },
  bubbleTextAssistant: { ...typography.bodyMd, color: colors.onSurface },
  bubbleTextUser: { ...typography.bodyMd, color: colors.onPrimary },
  time: { ...typography.labelSm, color: colors.outline, marginTop: 2, marginHorizontal: 4 },
  thinking: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  thinkingText: { ...typography.bodySm, color: colors.onSurfaceVariant },
  quickPrompts: { flexDirection: 'row', flexWrap: 'wrap', gap: 8, marginTop: spacing.xs },
  promptChip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    backgroundColor: colors.surfaceContainerLowest,
    paddingHorizontal: 12,
    minHeight: 36,
    borderRadius: radii.pill,
  },
  promptChipText: { ...typography.labelSm, color: colors.onSurface },
  inputWrap: { paddingHorizontal: spacing.margin, paddingBottom: spacing.sm },
  inputBar: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    gap: spacing.sm,
    width: '100%',
    maxWidth: MAX_WIDTH,
    alignSelf: 'center',
    paddingVertical: spacing.xs,
  },
  input: { flex: 1, ...typography.bodyMd, color: colors.onSurface, maxHeight: 120, paddingVertical: 8 },
  sendButton: {
    width: 40,
    height: 40,
    borderRadius: 20,
    backgroundColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
  },
});
