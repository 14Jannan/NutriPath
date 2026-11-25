import React, { useEffect, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Keyboard,
  KeyboardAvoidingView,
  Linking,
  Pressable,
  ScrollView,
  type ScrollViewInstance,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Card } from '@/components/Card';
import { ChatMarkdown } from '@/components/ChatMarkdown';
import { ChatHistorySheet } from '@/components/ChatHistorySheet';
import * as aiApi from '@/api/aiApi';
import type { AiUsageStatus, ChatHistoryMessage, KnowledgeSource } from '@/api/aiApi';
import { describeApiError } from '@/api/client';
import { isAxiosError } from 'axios';
import { showAlert } from '@/utils/alert';
import { describeDay, toLocalIsoDate } from '@/utils/date';
import { colors, typography, spacing, radii } from '@/theme';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import '@/i18n';
import { CHAT_LANGUAGES, useChatLanguage, type ChatLanguage } from '@/i18n/chatLanguage';
import { scriptAwareStyle } from '@/utils/script';
import { AnswerFeedback } from '@/components/AnswerFeedback';
import { useAppStatus } from '@/hooks/useAppStatus';

interface ChatMessage {
  id: string;
  role: 'assistant' | 'user';
  text: string;
  createdAt: Date;
  failed?: boolean;
  references?: KnowledgeSource[];
  insufficientEvidence?: boolean;
  // The saved answer's id on the server, for rating it.
  serverId?: string;
}

// Sent in the chat's language, so the assistant replies in it too.
const QUICK_PROMPTS = [
  { icon: 'silverware-fork-knife' as const, key: 'eatNow' },
  { icon: 'chart-line' as const, key: 'score' },
  { icon: 'bread-slice-outline' as const, key: 'fibreSnack' },
  { icon: 'calendar-today' as const, key: 'today' },
  { icon: 'calendar-week' as const, key: 'weekly' },
  { icon: 'fire' as const, key: 'streak' },
];

// A neutral greeting rather than hard-coded numbers — real figures only
// ever come from the backend, via the assistant's grounded replies. Its
// text is looked up at render time so it follows the chat language.
const WELCOME_ID = 'welcome';
const welcome = (): ChatMessage => ({ id: WELCOME_ID, role: 'assistant', text: '', createdAt: new Date() });

// The server may send UTC without a zone marker; make that explicit.
const parseUtc = (utc: string) => new Date(utc.endsWith('Z') ? utc : `${utc}Z`);

const fromHistory = (h: ChatHistoryMessage): ChatMessage => ({
  id: h.id,
  role: h.role.toLowerCase() === 'user' ? 'user' : 'assistant',
  text: h.content,
  createdAt: parseUtc(h.createdAtUtc),
  references: h.references,
  insufficientEvidence: h.insufficientEvidence,
  serverId: h.role.toLowerCase() === 'user' ? undefined : h.id,
});

const timeOf = (d: Date) => d.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });

// "2h 5m", "12m", or "less than a minute".
function describeWait(ms: number, t: TFunction): string {
  const minutes = Math.ceil(ms / 60_000);
  if (minutes <= 1) return t('chat.wait.lessThanMinute');
  const hours = Math.floor(minutes / 60);
  return hours ? `${hours}h ${minutes % 60}m` : `${minutes}m`;
}

// Warn once most of the allowance is used.
const NEAR_LIMIT = 0.8;

/** A localized explanation for a failed question. */
function describeChatError(error: unknown, t: TFunction): string {
  if (isAxiosError(error)) {
    const status = error.response?.status;
    if (!error.response) return t('chat.errors.network');
    if (status === 409) return t('chat.errors.busy');
    if (status === 503) {
      if ((error.response.data as { code?: string } | undefined)?.code === 'provider_not_permitted')
        return t('chat.errors.notPermitted');
      const retryAfter = Number(error.response.headers?.['retry-after']);
      return Number.isFinite(retryAfter) && retryAfter > 0
        ? t('chat.errors.rateLimited', { wait: describeWait(retryAfter * 1000, t) })
        : t('chat.errors.unavailable');
    }
  }
  return `${t('chat.sorry')} ${describeApiError(error)}`;
}

export function AssistantScreen() {
  const { t } = useTranslation();
  const { isEnabled } = useAppStatus();
  const { language, setLanguage } = useChatLanguage();
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([welcome()]);
  const [input, setInput] = useState('');
  const [thinking, setThinking] = useState(false);
  const [loadingChat, setLoadingChat] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [highlightId, setHighlightId] = useState<string | null>(null);
  const [usage, setUsage] = useState<AiUsageStatus | null>(null);
  const [now, setNow] = useState(Date.now());

  const scrollRef = useRef<ScrollViewInstance>(null);
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
      showAlert(t('chat.openChatError'), describeApiError(error));
    } finally {
      setLoadingChat(false);
    }
  }

  // Each session (app launch or login) starts on a fresh chat, and earlier
  // ones are a tap away in History. Tab screens stay mounted, so the
  // allowance is loaded once here and then updated by each reply.
  useEffect(() => {
    aiApi.getUsage().then(setUsage).catch(() => {}); // the server still enforces the limit
  }, []);

  // While locked, tick the countdown and check again once it's over.
  const resetsAt = usage?.locked && usage.resetsAtUtc ? parseUtc(usage.resetsAtUtc) : null;
  const resetsAtMs = resetsAt?.getTime();
  useEffect(() => {
    if (!resetsAtMs) return;
    const tick = setInterval(() => {
      setNow(Date.now());
      if (Date.now() >= resetsAtMs) aiApi.getUsage().then(setUsage).catch(() => {});
    }, 15_000);
    setNow(Date.now());
    return () => clearInterval(tick);
  }, [resetsAtMs]);

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

  // Keep the latest message in view when the keyboard opens and shrinks the list.
  useEffect(() => {
    const sub = Keyboard.addListener('keyboardDidShow', () => {
      if (!highlightId) scrollRef.current?.scrollToEnd({ animated: true });
    });
    return () => sub.remove();
  }, [highlightId]);

  // A new chat is only created on the server with its first message, so
  // opening the app or tapping New doesn't leave empty chats behind.
  function resetToNewChat() {
    positions.current.clear();
    setConversationId(null);
    setMessages([welcome()]);
    setHighlightId(null);
  }

  function startNewChat() {
    resetToNewChat();
    showAlert(t('chat.newChatStarted'), t('chat.earlierChats'), 'success');
  }

  function onChatDeleted(id: string) {
    if (id === conversationId) resetToNewChat();
  }

  async function sendMessage(text: string) {
    const question = text.trim();
    if (!question || thinking || usage?.locked) return;

    setHighlightId(null);
    const questionId = `local-${Date.now()}`;
    setMessages((prev) => [...prev, { id: questionId, role: 'user', text: question, createdAt: new Date() }]);
    setInput('');
    setThinking(true);

    // The backend builds the grounded context (the user's real numbers,
    // local date and time, and the chat so far) — this screen only shows it.
    try {
      // Without an id the server continues the latest chat, so a fresh
      // chat is created first.
      const id = conversationId ?? (await aiApi.startNewConversation());
      setConversationId(id);
      const response = await aiApi.askAssistant(question, id, language);
      setConversationId(response.conversationId);
      if (response.usage) setUsage(response.usage);
      setMessages((prev) => [
        ...prev,
        {
          id: `local-${Date.now()}-a`, role: 'assistant', text: response.answer, createdAt: new Date(),
          references: response.references, insufficientEvidence: response.insufficientEvidence,
          serverId: response.messageId,
        },
      ]);
    } catch (error) {
      // Over the limit: the server says when it resets.
      const limited = isAxiosError(error) && error.response?.status === 429
        ? (error.response.data as { usage?: AiUsageStatus } | undefined)?.usage
        : undefined;
      if (limited) {
        // It wasn't answered, so give the question back to send later.
        setMessages((prev) => prev.filter((m) => m.id !== questionId));
        setInput(question);
        setUsage(limited);
      } else {
        setMessages((prev) => [
          ...prev,
          {
            id: `local-${Date.now()}-e`,
            role: 'assistant',
            text: describeChatError(error, t),
            createdAt: new Date(),
            failed: true,
          },
        ]);
      }
    } finally {
      setThinking(false);
    }
  }

  const hasQuestions = messages.some((m) => m.role === 'user');
  const locked = !!usage?.locked;
  const nearLimit = !!usage && !locked && usage.tokensUsed >= usage.tokenLimit * NEAR_LIMIT;

  return (
    <SafeAreaView style={styles.screen} edges={['top']}>
      <View style={styles.topBar}>
        <View style={styles.topBarInner}>
          <View style={{ flex: 1 }}>
            <Text style={styles.title}>{t('chat.title')}</Text>
            <Text style={[styles.disclaimerText, scriptAwareStyle(t('chat.disclaimer'), styles.disclaimerText)]}>
              {t('chat.disclaimer')}
            </Text>
          </View>
          <Pressable
            onPress={() => setHistoryOpen(true)}
            style={({ pressed }) => [styles.topButton, pressed && styles.pressed]}
            accessibilityLabel={t('chat.historyA11y')}
          >
            <MaterialCommunityIcons name="history" size={20} color={colors.primary} />
            <Text style={styles.topButtonText} numberOfLines={1}>{t('chat.history')}</Text>
          </Pressable>
          <Pressable
            onPress={startNewChat}
            style={({ pressed }) => [styles.topButton, pressed && styles.pressed]}
            accessibilityLabel={t('chat.newChatA11y')}
          >
            <MaterialCommunityIcons name="chat-plus-outline" size={20} color={colors.primary} />
            <Text style={styles.topButtonText} numberOfLines={1}>{t('chat.newChat')}</Text>
          </Pressable>
        </View>
        <LanguageSelector value={language} onChange={setLanguage} t={t} />
      </View>

      {/* Android is edge-to-edge, so the window no longer resizes for the
          keyboard — pad on both platforms to keep the input bar visible. */}
      <KeyboardAvoidingView behavior="padding" style={{ flex: 1 }}>
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
                        {isUser ? (
                          <Text style={[styles.bubbleTextUser, scriptAwareStyle(message.text, styles.bubbleTextUser)]}>
                            {message.text}
                          </Text>
                        ) : (
                          <>
                            <ChatMarkdown
                              text={message.id === WELCOME_ID ? t('chat.welcome') : message.text}
                              style={styles.bubbleTextAssistant}
                            />
                            {message.insufficientEvidence && (
                              <Text style={[styles.time, scriptAwareStyle(t('chat.limitedEvidence'), styles.time)]}>
                                {t('chat.limitedEvidence')}
                              </Text>
                            )}
                            {!!message.references?.some((source) => source.url.startsWith('https://')) && (
                              <Text style={[styles.sourcesLabel, scriptAwareStyle(t('chat.sources'), styles.sourcesLabel)]}>
                                {t('chat.sources')}
                              </Text>
                            )}
                            {message.references?.filter((source) => source.url.startsWith('https://')).map((source) => (
                              <Text
                                key={source.id}
                                accessibilityRole="link"
                                style={styles.sourceLink}
                                onPress={() => {
                                  void Linking.openURL(source.url).catch(() => showAlert(t('chat.sourceError')));
                                }}
                              >
                                {source.title}
                              </Text>
                            ))}
                          </>
                        )}
                      </View>
                      {!isUser && !message.failed && message.serverId && isEnabled('assistant.feedback') && (
                        <AnswerFeedback messageId={message.serverId} />
                      )}
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
                  <Text style={[styles.thinkingText, scriptAwareStyle(t('chat.thinking'), styles.thinkingText)]}>
                    {t('chat.thinking')}
                  </Text>
                </View>
              </View>
            )}

            {!hasQuestions && !thinking && (
              <View style={styles.quickPrompts}>
                {QUICK_PROMPTS.map((prompt) => {
                  const label = t(`chat.prompts.${prompt.key}`);
                  return (
                    <Pressable
                      key={prompt.key}
                      style={({ pressed }) => [styles.promptChip, pressed && styles.pressed]}
                      onPress={() => sendMessage(label)}
                    >
                      <MaterialCommunityIcons name={prompt.icon} size={14} color={colors.secondary} />
                      <Text style={[styles.promptChipText, scriptAwareStyle(label, styles.promptChipText)]}>{label}</Text>
                    </Pressable>
                  );
                })}
              </View>
            )}
          </ScrollView>
        )}

        <View style={styles.inputWrap}>
          {locked ? (
            <Card style={styles.lockedCard}>
              <MaterialCommunityIcons name="timer-sand" size={22} color={colors.amberCaution} />
              <View style={{ flex: 1 }}>
                <Text style={styles.lockedTitle}>{t('chat.locked.title')}</Text>
                <Text style={[styles.lockedText, scriptAwareStyle(t('chat.locked.history'), styles.lockedText)]}>
                  {resetsAt
                    ? t('chat.locked.at', { time: timeOf(resetsAt), wait: describeWait(resetsAt.getTime() - now, t) })
                    : t('chat.locked.soon')}{' '}
                  {t('chat.locked.history')}
                </Text>
              </View>
            </Card>
          ) : (
          <>
          {nearLimit && usage && (
            <Text style={[styles.nearLimit, scriptAwareStyle(t('chat.disclaimer'), styles.nearLimit)]}>
              {t('chat.nearLimit', {
                percent: Math.min(99, Math.round((usage.tokensUsed / usage.tokenLimit) * 100)),
                hours: usage.windowHours,
              })}
            </Text>
          )}
          <Card style={styles.inputBar}>
            <TextInput
              style={[styles.input, scriptAwareStyle(input || t('chat.placeholder'), styles.input)]}
              placeholder={t('chat.placeholder')}
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
              accessibilityLabel={t('chat.send')}
            >
              <MaterialCommunityIcons name="arrow-up" size={18} color={colors.onPrimary} />
            </Pressable>
          </Card>
          </>
          )}
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

/** Automatic / English / தமிழ் / සිංහල. Each option is labelled in its own language. */
function LanguageSelector({ value, onChange, t }: { value: ChatLanguage; onChange: (l: ChatLanguage) => void; t: TFunction }) {
  return (
    <View style={styles.languageRow} accessibilityRole="radiogroup" accessibilityLabel={t('chat.language.label')}>
      <MaterialCommunityIcons name="translate" size={16} color={colors.secondary} />
      {CHAT_LANGUAGES.map((code) => {
        const name = t(`chat.language.${code}`);
        const selected = code === value;
        return (
          <Pressable
            key={code}
            onPress={() => onChange(code)}
            accessibilityRole="radio"
            accessibilityState={{ selected }}
            accessibilityLabel={t('chat.language.a11y', { name })}
            testID={`chat-language-${code}`}
            style={({ pressed }) => [styles.languageChip, selected && styles.languageChipSelected, pressed && styles.pressed]}
          >
            <Text
              style={[
                styles.languageChipText,
                selected && styles.languageChipTextSelected,
                scriptAwareStyle(name, styles.languageChipText),
              ]}
              numberOfLines={1}
            >
              {name}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}

// Phone-width content on a wide browser window.
const MAX_WIDTH = 720;

const styles = StyleSheet.create({
  sourceLink: { color: colors.primary, textDecorationLine: 'underline', marginTop: spacing.xs },
  sourcesLabel: { ...typography.labelSm, color: colors.onSurfaceVariant, marginTop: spacing.xs },
  languageRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: 6,
    marginTop: spacing.xs,
    width: '100%',
    maxWidth: 720,
    alignSelf: 'center',
  },
  languageChip: {
    paddingHorizontal: 10,
    minHeight: 32,
    justifyContent: 'center',
    borderRadius: radii.pill,
    borderWidth: 1,
    borderColor: colors.surfaceContainer,
    backgroundColor: colors.surfaceContainerLowest,
  },
  languageChipSelected: { backgroundColor: colors.primary, borderColor: colors.primary },
  languageChipText: { ...typography.labelMd, color: colors.onSurface },
  languageChipTextSelected: { color: colors.onPrimary },
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
  lockedCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    width: '100%',
    maxWidth: MAX_WIDTH,
    alignSelf: 'center',
    borderWidth: 1,
    borderColor: colors.amberCaution,
  },
  lockedTitle: { ...typography.labelLg, color: colors.onSurface },
  lockedText: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 2 },
  nearLimit: {
    ...typography.labelSm,
    color: colors.amberCaution,
    textAlign: 'center',
    marginBottom: spacing.xs,
    width: '100%',
    maxWidth: MAX_WIDTH,
    alignSelf: 'center',
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
