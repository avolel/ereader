import * as DocumentPicker from 'expo-document-picker';
import { useRouter } from 'expo-router';
import { useMemo, useState } from 'react';
import {
  ActivityIndicator,
  FlatList,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
  useWindowDimensions,
} from 'react-native';

import AuthImage from '../components/AuthImage';
import ConfirmDialog from '../components/ConfirmDialog';
import { useDeleteBook } from '../hooks/useDeleteBook';
import { useBooks } from '../hooks/useBooks';
import { useUploadBook } from '../hooks/useUploadBook';
import { useAuth } from '../providers/AuthProvider';
import { useTheme } from '../providers/ThemeProvider';
import { absoluteCoverUrl } from '../services/books';
import { extractApiError } from '../services/errors';
import { BookSortKey, BookSummary, SortDirection } from '../types';

const COVER_MIN_WIDTH = 140;
const COVER_GAP = 16;
const COVER_ASPECT = 2 / 3;

type SortOption = { label: string; sort: BookSortKey; dir: SortDirection };
const SORT_OPTIONS: SortOption[] = [
  { label: 'Newest', sort: 'importedAt', dir: 'desc' },
  { label: 'Oldest', sort: 'importedAt', dir: 'asc' },
  { label: 'Title A→Z', sort: 'title', dir: 'asc' },
  { label: 'Author A→Z', sort: 'author', dir: 'asc' },
];

export default function LibraryScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { logout, user } = useAuth();
  const { width } = useWindowDimensions();
  const [sortIndex, setSortIndex] = useState(0);
  const [authorFilter, setAuthorFilter] = useState('');
  const [pendingDelete, setPendingDelete] = useState<BookSummary | null>(null);
  const deleteMutation = useDeleteBook();
  // Committed value used for the actual query so we don't refetch on every keystroke.
  const [committedAuthor, setCommittedAuthor] = useState('');

  const sortOption = SORT_OPTIONS[sortIndex];
  const queryParams = useMemo(
    () => ({
      sort: sortOption.sort,
      dir: sortOption.dir,
      author: committedAuthor || undefined,
    }),
    [sortOption, committedAuthor],
  );

  const booksQuery = useBooks(queryParams);
  const uploadMutation = useUploadBook();

  // Compute column count from container width. Subtract one gap because we
  // count gaps *between* cards (n-1 gaps for n cards).
  const numColumns = Math.max(1, Math.floor((width - 32 + COVER_GAP) / (COVER_MIN_WIDTH + COVER_GAP)));
  const coverWidth = (width - 32 - COVER_GAP * (numColumns - 1)) / numColumns;

  const items = useMemo(
    () => booksQuery.data?.pages.flatMap((p) => p.items) ?? [],
    [booksQuery.data],
  );

  async function onUpload() {
    if (uploadMutation.isPending) return;
    const result = await DocumentPicker.getDocumentAsync({
      type: 'application/epub+zip',
      multiple: false,
      copyToCacheDirectory: true,
    });
    if (result.canceled || !result.assets?.[0]) return;
    const asset = result.assets[0];

    try {
      if (Platform.OS === 'web' && asset.file) {
        await uploadMutation.mutateAsync({ kind: 'web', file: asset.file });
      } else {
        await uploadMutation.mutateAsync({
          kind: 'native',
          uri: asset.uri,
          name: asset.name ?? 'book.epub',
          mimeType: asset.mimeType ?? 'application/epub+zip',
        });
      }
    } catch {
      // Surface via the mutation's error state below — no need to handle here.
    }
  }

  const uploadError = uploadMutation.isError ? extractApiError(uploadMutation.error).message : null;

  return (
    <View style={[styles.container, { backgroundColor: theme.colors.background }]}>
      <View style={[styles.header, { borderBottomColor: theme.colors.border }]}>
        <Text style={[styles.title, { color: theme.colors.text }]}>Library</Text>
        <View style={styles.headerRight}>
          <Pressable
            onPress={() => router.push('/(authed)/search')}
            accessibilityLabel="Search library"
            style={{ marginRight: 16 }}
          >
            <Text style={{ color: theme.colors.accent }}>Search</Text>
          </Pressable>
          {user && <Text style={{ color: theme.colors.textMuted, marginRight: 12 }}>{user.username}</Text>}
          <Pressable onPress={() => void logout()}>
            <Text style={{ color: theme.colors.accent }}>Sign out</Text>
          </Pressable>
        </View>
      </View>

      <View style={[styles.toolbar, { borderBottomColor: theme.colors.border }]}>
        <View style={styles.sortRow}>
          {SORT_OPTIONS.map((opt, idx) => {
            const active = idx === sortIndex;
            return (
              <Pressable
                key={opt.label}
                onPress={() => setSortIndex(idx)}
                style={[
                  styles.chip,
                  {
                    borderColor: active ? theme.colors.accent : theme.colors.border,
                    backgroundColor: active ? theme.colors.accent : 'transparent',
                  },
                ]}
              >
                <Text style={{ color: active ? '#fff' : theme.colors.text, fontSize: 13 }}>
                  {opt.label}
                </Text>
              </Pressable>
            );
          })}
        </View>
        <TextInput
          style={[styles.filterInput, { borderColor: theme.colors.border, color: theme.colors.text }]}
          placeholder="Filter by author"
          placeholderTextColor={theme.colors.textMuted}
          value={authorFilter}
          onChangeText={setAuthorFilter}
          onSubmitEditing={() => setCommittedAuthor(authorFilter.trim())}
          onBlur={() => setCommittedAuthor(authorFilter.trim())}
          returnKeyType="search"
        />
      </View>

      {uploadError && (
        <Text style={[styles.uploadError, { color: theme.colors.error }]}>{uploadError}</Text>
      )}

      {booksQuery.isLoading ? (
        <View style={styles.center}>
          <ActivityIndicator />
        </View>
      ) : booksQuery.isError ? (
        <View style={styles.center}>
          <Text style={{ color: theme.colors.error }}>
            {extractApiError(booksQuery.error).message}
          </Text>
        </View>
      ) : items.length === 0 ? (
        <View style={styles.center}>
          <Text style={{ color: theme.colors.textMuted, textAlign: 'center' }}>
            Your library is empty.{'\n'}Tap the + button to add an EPUB.
          </Text>
        </View>
      ) : (
        <FlatList
          // key forces re-mount when columns change — FlatList can't change
          // numColumns dynamically otherwise.
          key={`cols-${numColumns}`}
          data={items}
          keyExtractor={(b) => b.id}
          numColumns={numColumns}
          contentContainerStyle={styles.gridContent}
          columnWrapperStyle={numColumns > 1 ? { gap: COVER_GAP } : undefined}
          ItemSeparatorComponent={() => <View style={{ height: COVER_GAP }} />}
          onEndReachedThreshold={0.4}
          onEndReached={() => {
            if (booksQuery.hasNextPage && !booksQuery.isFetchingNextPage) {
              void booksQuery.fetchNextPage();
            }
          }}
          ListFooterComponent={
            booksQuery.isFetchingNextPage ? (
              <View style={{ paddingVertical: 16 }}>
                <ActivityIndicator />
              </View>
            ) : null
          }
          renderItem={({ item }) => (
            <BookCard
              book={item}
              width={coverWidth}              
              onPress={() =>
                router.push({
                  pathname: '/(authed)/reader/[bookId]',
                  params: { bookId: item.id },
                })
              }
              onRequestDelete={() => setPendingDelete(item)}
            />
          )}
        />
      )}

      <Pressable
        onPress={onUpload}
        disabled={uploadMutation.isPending}
        style={[
          styles.fab,
          { backgroundColor: theme.colors.accent, opacity: uploadMutation.isPending ? 0.6 : 1 },
        ]}
      >
        {uploadMutation.isPending ? (
          <ActivityIndicator color="#fff" />
        ) : (
          <Text style={styles.fabIcon}>+</Text>
        )}
      </Pressable>

      <ConfirmDialog
        visible={pendingDelete !== null}
        title="Delete book?"
        message={
          pendingDelete
            ? `"${pendingDelete.title}" and its bookmarks, highlights, and reading progress will be permanently deleted.`
            : ''
        }
        confirmLabel="Delete"
        destructive
        busy={deleteMutation.isPending}
        onCancel={() => setPendingDelete(null)}
        onConfirm={async () => {
          if (!pendingDelete) return;
          try {
            await deleteMutation.mutateAsync(pendingDelete.id);
          } catch {
            // surfaced below via deleteMutation.error
          } finally {
            setPendingDelete(null);
          }
        }}
    />
    </View>
  );
}

type BookCardProps = { book: BookSummary; width: number; onPress: () => void; onRequestDelete: () => void };

function BookCard({ book, width, onPress, onRequestDelete }: BookCardProps) {
  const theme = useTheme();
  const coverHeight = width / COVER_ASPECT;
  return (
    <View style={{ width }}>
      <Pressable
        onPress={onPress}
        onLongPress={onRequestDelete}
        accessibilityLabel={`Open ${book.title}`}
        style={({ pressed }) => [{ opacity: pressed ? 0.85 : 1 }]}
      >
        <AuthImage
          url={absoluteCoverUrl(book.coverUrl)}
          style={{ width, height: coverHeight, borderRadius: 4, backgroundColor: theme.colors.surface }}
          placeholderStyle={{
            backgroundColor: theme.colors.surface,
            borderRadius: 4,
            borderWidth: 1,
            borderColor: theme.colors.border,
          }}
          alt={book.title}
        />
        <Text style={[styles.bookTitle, { color: theme.colors.text }]} numberOfLines={2}>
          {book.title}
        </Text>
        <Text style={[styles.bookAuthor, { color: theme.colors.textMuted }]} numberOfLines={1}>
          {book.author}
        </Text>
      </Pressable>

      <Pressable
        onPress={onRequestDelete}
        accessibilityLabel={`Delete ${book.title}`}
        hitSlop={8}
        style={styles.overflowButton}
      >
        <Text style={styles.overflowIcon}>⋯</Text>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingVertical: 12,
    borderBottomWidth: 1,
  },
  title: { fontSize: 22, fontWeight: '700' },
  headerRight: { flexDirection: 'row', alignItems: 'center' },
  toolbar: {
    paddingHorizontal: 16,
    paddingVertical: 10,
    borderBottomWidth: 1,
    gap: 10,
  },
  sortRow: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  chip: {
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: 14,
    borderWidth: 1,
  },
  filterInput: {
    borderWidth: 1,
    borderRadius: 6,
    paddingHorizontal: 10,
    paddingVertical: 8,
    fontSize: 14,
  },
  uploadError: { paddingHorizontal: 16, paddingTop: 8, fontSize: 13 },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 24 },
  gridContent: { padding: 16 },
  bookTitle: { marginTop: 6, fontSize: 14, fontWeight: '600' },
  bookAuthor: { marginTop: 2, fontSize: 12 },
  fab: {
    position: 'absolute',
    right: 24,
    bottom: 24,
    width: 56,
    height: 56,
    borderRadius: 28,
    alignItems: 'center',
    justifyContent: 'center',
    shadowColor: '#000',
    shadowOpacity: 0.2,
    shadowRadius: 4,
    shadowOffset: { width: 0, height: 2 },
    elevation: 4,
  },
  fabIcon: { color: '#fff', fontSize: 32, lineHeight: 36, marginTop: -2 },
  overflowButton: {
    position: 'absolute',
    top: 6,
    right: 6,
    width: 28,
    height: 28,
    borderRadius: 14,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: 'rgba(0,0,0,0.45)',
  },
  overflowIcon: { color: '#fff', fontSize: 18, lineHeight: 18, marginTop: -4 },
});