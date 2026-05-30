import { useRouter } from 'expo-router';
import { useEffect, useMemo, useState } from 'react';
import {
  ActivityIndicator,
  FlatList,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';

import { useSearch } from '../hooks/useSearch';
import { useTheme } from '../providers/ThemeProvider';
import { extractApiError } from '../services/errors';
import { SearchHit } from '../types';

const DEBOUNCE_MS = 300;

export default function SearchScreen() {
  const router = useRouter();
  const { colors } = useTheme();
  const [input, setInput] = useState('');
  const [debounced, setDebounced] = useState('');

  // Plain debounce — every keystroke pushes the timer back.
  useEffect(() => {
    const t = setTimeout(() => setDebounced(input.trim()), DEBOUNCE_MS);
    return () => clearTimeout(t);
  }, [input]);

  const query = useSearch(debounced);
  const items = useMemo(
    () => query.data?.pages.flatMap((p) => p.items) ?? [],
    [query.data],
  );

  function onHitPress(hit: SearchHit) {
    // anchor format consumed by ReaderScreen: chapterId[:scrollY]. Search results
    // don't carry scrollY (no per-match offset), so jumping lands at the top of
    // the matched chapter.
    router.push({
      pathname: '/(authed)/reader/[bookId]',
      params: { bookId: hit.bookId, anchor: hit.chapterId },
    });
  }

  return (
    <View style={[styles.container, { backgroundColor: colors.background }]}>
      <View style={[styles.header, { borderBottomColor: colors.border }]}>
        <Pressable onPress={() => router.back()} accessibilityLabel="Back">
          <Text style={{ color: colors.accent, fontSize: 16 }}>← Back</Text>
        </Pressable>
        <TextInput
          autoFocus
          style={[
            styles.input,
            { borderColor: colors.border, color: colors.text, backgroundColor: colors.surface },
          ]}
          placeholder="Search your library"
          placeholderTextColor={colors.textMuted}
          value={input}
          onChangeText={setInput}
          returnKeyType="search"
        />
      </View>

      {debounced.length < 2 ? (
        <Hint colors={colors}>Type at least 2 characters to search.</Hint>
      ) : query.isLoading ? (
        <View style={styles.center}><ActivityIndicator /></View>
      ) : query.isError ? (
        <Hint colors={colors} error>
          {extractApiError(query.error).message}
        </Hint>
      ) : items.length === 0 ? (
        <Hint colors={colors}>No matches for &ldquo;{debounced}&rdquo;.</Hint>
      ) : (
        <FlatList
          data={items}
          keyExtractor={(h, i) => `${h.bookId}-${h.chapterId}-${i}`}
          renderItem={({ item }) => (
            <ResultRow hit={item} onPress={onHitPress} colors={colors} />
          )}
          ItemSeparatorComponent={() => (
            <View style={{ height: 1, backgroundColor: colors.border }} />
          )}
          onEndReachedThreshold={0.4}
          onEndReached={() => {
            if (query.hasNextPage && !query.isFetchingNextPage) {
              void query.fetchNextPage();
            }
          }}
          ListFooterComponent={
            query.isFetchingNextPage ? (
              <View style={{ paddingVertical: 16 }}><ActivityIndicator /></View>
            ) : null
          }
        />
      )}
    </View>
  );
}

function ResultRow({
  hit,
  onPress,
  colors,
}: {
  hit: SearchHit;
  onPress: (h: SearchHit) => void;
  colors: ReturnType<typeof useTheme>['colors'];
}) {
  return (
    <Pressable
      onPress={() => onPress(hit)}
      style={({ pressed }) => [
        styles.row,
        { backgroundColor: pressed ? colors.surface : 'transparent' },
      ]}
    >
      <Text style={[styles.bookLine, { color: colors.text }]} numberOfLines={1}>
        {hit.bookTitle}
      </Text>
      <Text style={[styles.chapterLine, { color: colors.textMuted }]} numberOfLines={1}>
        {hit.chapterTitle ?? `Chapter ${hit.chapterSpineOrder + 1}`}
      </Text>
      {/* Snippet is server-rendered HTML with <mark> wrappers. We don't have a
          native HTML renderer wired up for v1, so strip tags and bold-mark the
          plain text instead. Renderable HTML is a Phase 4-ish polish. */}
      <Text style={[styles.snippet, { color: colors.text }]} numberOfLines={3}>
        {stripHtml(hit.snippet)}
      </Text>
    </Pressable>
  );
}

function Hint({
  children,
  colors,
  error,
}: {
  children: React.ReactNode;
  colors: ReturnType<typeof useTheme>['colors'];
  error?: boolean;
}) {
  return (
    <View style={styles.center}>
      <Text style={{ color: error ? colors.error : colors.textMuted, textAlign: 'center' }}>
        {children}
      </Text>
    </View>
  );
}

function stripHtml(html: string): string {
  // Snippet is small (ts_headline output, ~20 words). Quick regex is fine.
  return html.replace(/<[^>]+>/g, '').replace(/\s+/g, ' ').trim();
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 12,
    paddingVertical: 10,
    gap: 12,
    borderBottomWidth: 1,
  },
  input: {
    flex: 1,
    borderWidth: 1,
    borderRadius: 8,
    paddingHorizontal: 12,
    paddingVertical: 8,
    fontSize: 15,
  },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 24 },
  row: { paddingHorizontal: 16, paddingVertical: 12 },
  bookLine: { fontSize: 15, fontWeight: '600' },
  chapterLine: { fontSize: 12, marginTop: 2 },
  snippet: { fontSize: 13, marginTop: 6, lineHeight: 18 },
});