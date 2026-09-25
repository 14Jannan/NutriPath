import React, { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, FlatList, Modal, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import * as aiApi from '@/api/aiApi';
import type { ChatSearchResult, ConversationSummary } from '@/api/aiApi';
import { describeApiError } from '@/api/client';
import { confirmAction, showAlert } from '@/utils/alert';
import { describeDay, toLocalIsoDate } from '@/utils/date';
import { colors, radii, spacing, typography } from '@/theme';

const SEARCH_DELAY_MS = 300;

/** "Today, 7:30 PM", "Yesterday, 9:05 AM", "Mon, 21 Sep, 1:15 PM". */
export function describeWhen(utc: string): string {
  const date = new Date(utc);
  const time = date.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
  return `${describeDay(toLocalIsoDate(date))}, ${time}`;
}

/** Shows the search term in bold inside a snippet, ignoring case. */
function Highlighted({ text, term }: { text: string; term: string }) {
  if (!term) return <Text style={styles.snippet}>{text}</Text>;
  const escaped = term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const parts = text.split(new RegExp(`(${escaped})`, 'ig'));
  return (
    <Text style={styles.snippet} numberOfLines={3}>
      {parts.map((part, i) =>
        part.toLowerCase() === term.toLowerCase() ? (
          <Text key={i} style={styles.match}>
            {part}
          </Text>
        ) : (
          part
        )
      )}
    </Text>
  );
}

interface ChatHistorySheetProps {
  visible: boolean;
  currentConversationId: string | null;
  onClose: () => void;
  // messageId is set when opened from a search result, to jump to it.
  onOpen: (conversationId: string, messageId?: string) => void;
  onDeleted: (conversationId: string) => void;
}

/**
 * Past conversations, newest first, with a search box that finds any
 * message across all of them. Tapping a chat or a search result opens it.
 */
export function ChatHistorySheet({ visible, currentConversationId, onClose, onOpen, onDeleted }: ChatHistorySheetProps) {
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [loading, setLoading] = useState(false);
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<ChatSearchResult[] | null>(null);
  const [searching, setSearching] = useState(false);

  const loadConversations = useCallback(() => {
    setLoading(true);
    aiApi
      .listConversations()
      .then(setConversations)
      .catch((error) => showAlert("Couldn't load your chats", describeApiError(error)))
      .finally(() => setLoading(false));
  }, []);

  // Fresh list every time the sheet opens.
  useEffect(() => {
    if (visible) {
      setQuery('');
      setResults(null);
      loadConversations();
    }
  }, [visible, loadConversations]);

  // Search as the user types, once they pause.
  useEffect(() => {
    const term = query.trim();
    if (term.length < 2) {
      setResults(null);
      setSearching(false);
      return;
    }
    let stale = false;
    setSearching(true);
    const timer = setTimeout(() => {
      aiApi
        .searchChats(term)
        .then((r) => !stale && setResults(r))
        .catch(() => !stale && setResults([]))
        .finally(() => !stale && setSearching(false));
    }, SEARCH_DELAY_MS);
    return () => {
      stale = true;
      clearTimeout(timer);
    };
  }, [query]);

  function remove(conversation: ConversationSummary) {
    confirmAction(
      'Delete this chat?',
      `"${conversation.title}" and its ${conversation.messageCount} messages will be deleted permanently.`,
      'Delete',
      async () => {
        try {
          await aiApi.deleteConversation(conversation.id);
          setConversations((prev) => prev.filter((c) => c.id !== conversation.id));
          onDeleted(conversation.id);
          showAlert('Chat deleted', undefined, 'success');
        } catch (error) {
          showAlert("Couldn't delete the chat", describeApiError(error));
        }
      },
      true
    );
  }

  const term = query.trim();
  const isSearching = term.length >= 2;

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose} presentationStyle="pageSheet">
      <SafeAreaView style={styles.screen} edges={['top', 'bottom']}>
        <View style={styles.inner}>
          <View style={styles.header}>
            <Text style={styles.title}>Chat history</Text>
            <Pressable onPress={onClose} style={styles.iconButton} accessibilityLabel="Close chat history" hitSlop={6}>
              <MaterialCommunityIcons name="close" size={24} color={colors.onSurface} />
            </Pressable>
          </View>

          <View style={styles.searchBar}>
            <MaterialCommunityIcons name="magnify" size={20} color={colors.onSurfaceVariant} />
            <TextInput
              style={styles.searchInput}
              value={query}
              onChangeText={setQuery}
              placeholder="Search your messages"
              placeholderTextColor={colors.outline}
              accessibilityLabel="Search your messages"
              returnKeyType="search"
            />
            {searching && <ActivityIndicator size="small" color={colors.primary} />}
            {!!query && !searching && (
              <Pressable onPress={() => setQuery('')} accessibilityLabel="Clear search" hitSlop={6}>
                <MaterialCommunityIcons name="close-circle" size={18} color={colors.outline} />
              </Pressable>
            )}
          </View>

          {isSearching ? (
            <FlatList
              data={results ?? []}
              keyExtractor={(r) => r.messageId}
              keyboardShouldPersistTaps="handled"
              contentContainerStyle={styles.list}
              ListEmptyComponent={
                results && !searching ? <Text style={styles.empty}>No messages match "{term}".</Text> : null
              }
              renderItem={({ item }) => (
                <Pressable
                  style={({ pressed }) => [styles.row, pressed && styles.pressed]}
                  onPress={() => onOpen(item.conversationId, item.messageId)}
                  accessibilityLabel={`Open message in ${item.conversationTitle}`}
                >
                  <MaterialCommunityIcons
                    name={item.role === 'User' ? 'account-outline' : 'robot-happy-outline'}
                    size={20}
                    color={colors.primary}
                  />
                  <View style={{ flex: 1 }}>
                    <Text style={styles.rowTitle} numberOfLines={1}>
                      {item.conversationTitle}
                    </Text>
                    <Highlighted text={item.snippet} term={term} />
                    <Text style={styles.rowMeta}>{describeWhen(item.createdAtUtc)}</Text>
                  </View>
                </Pressable>
              )}
            />
          ) : loading ? (
            <ActivityIndicator color={colors.primary} style={{ marginTop: spacing.lg }} />
          ) : (
            <FlatList
              data={conversations}
              keyExtractor={(c) => c.id}
              contentContainerStyle={styles.list}
              ListEmptyComponent={
                <View style={styles.emptyBlock}>
                  <MaterialCommunityIcons name="chat-outline" size={36} color={colors.outline} />
                  <Text style={styles.empty}>No chats yet. Your conversations will appear here.</Text>
                </View>
              }
              renderItem={({ item }) => {
                const current = item.id === currentConversationId;
                return (
                  <Pressable
                    style={({ pressed }) => [styles.row, current && styles.rowCurrent, pressed && styles.pressed]}
                    onPress={() => onOpen(item.id)}
                    accessibilityLabel={`Open chat: ${item.title}`}
                  >
                    <MaterialCommunityIcons name="chat-processing-outline" size={20} color={colors.primary} />
                    <View style={{ flex: 1 }}>
                      <Text style={styles.rowTitle} numberOfLines={2}>
                        {item.title}
                      </Text>
                      <Text style={styles.rowMeta}>
                        {describeWhen(item.lastMessageAtUtc)} · {item.messageCount} messages
                        {current ? ' · Current' : ''}
                      </Text>
                    </View>
                    <Pressable
                      onPress={() => remove(item)}
                      style={styles.iconButton}
                      accessibilityLabel={`Delete chat: ${item.title}`}
                      hitSlop={6}
                    >
                      <MaterialCommunityIcons name="trash-can-outline" size={20} color={colors.outline} />
                    </Pressable>
                  </Pressable>
                );
              }}
            />
          )}
        </View>
      </SafeAreaView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.surface },
  // Phone-width on a wide browser window.
  inner: { flex: 1, width: '100%', maxWidth: 560, alignSelf: 'center', paddingHorizontal: spacing.margin },
  header: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingVertical: spacing.sm },
  title: { ...typography.headlineMd, color: colors.onSurface },
  iconButton: { width: 40, height: 40, alignItems: 'center', justifyContent: 'center' },
  searchBar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    backgroundColor: colors.surfaceContainerLowest,
    borderRadius: radii.pill,
    paddingHorizontal: spacing.sm,
    minHeight: 44,
    marginBottom: spacing.sm,
  },
  searchInput: { flex: 1, ...typography.bodyMd, color: colors.onSurface },
  list: { paddingBottom: spacing.lg, gap: spacing.xs },
  row: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
    padding: spacing.sm,
    backgroundColor: colors.surfaceContainerLowest,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: 'transparent',
  },
  rowCurrent: { borderColor: colors.primary },
  pressed: { opacity: 0.8 },
  rowTitle: { ...typography.labelLg, color: colors.onSurface },
  rowMeta: { ...typography.labelSm, color: colors.onSurfaceVariant, marginTop: 4 },
  snippet: { ...typography.bodySm, color: colors.onSurfaceVariant, marginTop: 2 },
  match: { fontFamily: 'PlusJakartaSans_700Bold', color: colors.primary, backgroundColor: colors.secondaryFixed },
  emptyBlock: { alignItems: 'center', gap: spacing.sm, marginTop: spacing.xl },
  empty: { ...typography.bodyMd, color: colors.onSurfaceVariant, textAlign: 'center', marginTop: spacing.md },
});
