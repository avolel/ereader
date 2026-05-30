import { useLocalSearchParams, useRouter } from 'expo-router';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Pressable,
  StyleSheet,
  Text,
  View,
} from 'react-native';

import ReaderWebView, {
  ReaderWebViewHandle,
  ReaderWebViewMessage,
} from '../components/ReaderWebView';
import SettingsDrawer from '../components/SettingsDrawer';
import TableOfContents from '../components/TableOfContents';
import { useBook } from '../hooks/useBook';
import { useBookSettings, useUpdatePosition } from '../hooks/useReadingSettings';
import { useChapter } from '../hooks/useChapter';
import { useTheme } from '../providers/ThemeProvider';
import { extractApiError } from '../services/errors';

const BASE_URL = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5000';
const POSITION_SAVE_DELAY_MS = 1200;

export default function ReaderScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ bookId: string; anchor?: string }>();
  const bookId = params.bookId;
  const anchor = params.anchor;

  const { colors } = useTheme();
  const bookQuery = useBook(bookId);
  const settingsQuery = useBookSettings(bookId);
  const positionMutation = useUpdatePosition(bookId ?? '');

  // currentChapterId is local state, seeded from saved position once both
  // book and settings have loaded. anchor (from search results) wins if set.
  const [currentChapterId, setCurrentChapterId] = useState<string | null>(null);
  const [initialScrollY, setInitialScrollY] = useState(0);
  const [tocOpen, setTocOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const webRef = useRef<ReaderWebViewHandle>(null);
  // Track the most recent scrollY reported by the WebView so we can persist it.
  const lastScrollYRef = useRef(0);
  const saveTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // anchor format: "chapterId" or "chapterId:scrollY"
  const parsedAnchor = useMemo(() => parseAnchor(anchor), [anchor]);

  // Seed currentChapterId once book + settings have loaded.
  useEffect(() => {
    if (currentChapterId) return;
    if (!bookQuery.data || settingsQuery.isLoading) return;

    const toc = bookQuery.data.toc;
    if (toc.length === 0) return;

    if (parsedAnchor) {
      const exists = toc.find((c) => c.chapterId === parsedAnchor.chapterId);
      if (exists) {
        setCurrentChapterId(parsedAnchor.chapterId);
        setInitialScrollY(parsedAnchor.scrollY ?? 0);
        return;
      }
      // Anchor names a chapter that no longer exists (re-import) — fall through.
    }

    const saved = settingsQuery.data?.lastChapterId;
    if (saved && toc.find((c) => c.chapterId === saved)) {
      setCurrentChapterId(saved);
      setInitialScrollY(settingsQuery.data?.lastScrollOffset ?? 0);
    } else {
      setCurrentChapterId(toc[0].chapterId);
      setInitialScrollY(0);
    }
  }, [bookQuery.data, settingsQuery.data, settingsQuery.isLoading, parsedAnchor, currentChapterId]);

  const chapterQuery = useChapter(bookId, currentChapterId ?? undefined);

  const currentSpineIndex = useMemo(() => {
    if (!bookQuery.data || !currentChapterId) return -1;
    return bookQuery.data.toc.findIndex((c) => c.chapterId === currentChapterId);
  }, [bookQuery.data, currentChapterId]);

  const goToChapter = useCallback((chapterId: string) => {
    flushPosition();
    setCurrentChapterId(chapterId);
    setInitialScrollY(0);
    lastScrollYRef.current = 0;
    setTocOpen(false);
  }, []);

  function goPrev() {
    if (!bookQuery.data || currentSpineIndex <= 0) return;
    goToChapter(bookQuery.data.toc[currentSpineIndex - 1].chapterId);
  }
  function goNext() {
    if (!bookQuery.data || currentSpineIndex < 0) return;
    const next = bookQuery.data.toc[currentSpineIndex + 1];
    if (next) goToChapter(next.chapterId);
  }

  // Debounced position persistence. The WebView already throttles its scroll
  // reports; we layer one more debounce on top so chapter scroll → API request
  // happens at most ~once per second of idle.
  function schedulePositionSave() {
    if (saveTimerRef.current) clearTimeout(saveTimerRef.current);
    saveTimerRef.current = setTimeout(() => {
      flushPosition();
    }, POSITION_SAVE_DELAY_MS);
  }

  function flushPosition() {
    if (saveTimerRef.current) {
      clearTimeout(saveTimerRef.current);
      saveTimerRef.current = null;
    }
    if (!currentChapterId || !bookId) return;
    positionMutation.mutate({
      chapterId: currentChapterId,
      scrollOffset: Math.max(0, Math.floor(lastScrollYRef.current)),
    });
  }

  // Persist on unmount / chapter change to avoid losing the last few pixels.
  useEffect(() => {
    return () => {
      if (saveTimerRef.current) clearTimeout(saveTimerRef.current);
    };
  }, []);

  const handleWebViewMessage = useCallback(
    (msg: ReaderWebViewMessage) => {
      if (msg.type === 'scroll') {
        lastScrollYRef.current = msg.scrollY;
        schedulePositionSave();
      }
      // 'ready' is currently informational; future highlight-rendering work
      // will use it to know when DOM is laid out before injecting spans.
    },
    [],
  );

  if (bookQuery.isLoading) {
    return <CenteredLoader />;
  }
  if (bookQuery.isError) {
    return <ErrorState message={extractApiError(bookQuery.error).message} />;
  }
  if (!bookQuery.data) {
    return <ErrorState message="Book not found." />;
  }

  const book = bookQuery.data;
  const currentTocEntry = book.toc.find((c) => c.chapterId === currentChapterId);

  return (
    <View style={[styles.container, { backgroundColor: colors.background }]}>
      <View style={[styles.header, { borderBottomColor: colors.border, backgroundColor: colors.surface }]}>
        <Pressable onPress={() => router.back()} accessibilityLabel="Back to library">
          <Text style={{ color: colors.accent, fontSize: 16 }}>← Library</Text>
        </Pressable>
        <View style={styles.headerCenter}>
          <Text style={[styles.bookTitle, { color: colors.text }]} numberOfLines={1}>
            {book.title}
          </Text>
          {currentTocEntry && (
            <Text style={[styles.chapterTitle, { color: colors.textMuted }]} numberOfLines={1}>
              {currentTocEntry.title ?? `Chapter ${currentTocEntry.spineOrder + 1}`}
            </Text>
          )}
        </View>
        <View style={styles.headerRight}>
          <HeaderButton label="TOC" onPress={() => setTocOpen(true)} colors={colors} />
          <HeaderButton label="Aa" onPress={() => setSettingsOpen(true)} colors={colors} />
        </View>
      </View>

      {chapterQuery.isLoading || !currentChapterId ? (
        <CenteredLoader />
      ) : chapterQuery.isError ? (
        <ErrorState message={extractApiError(chapterQuery.error).message} />
      ) : chapterQuery.data ? (
        <ReaderWebView
          ref={webRef}
          chapterHtml={chapterQuery.data.content}
          assetsBaseUrl={`${BASE_URL}/api/v1/books/${bookId}/assets/`}
          initialScrollY={initialScrollY}
          language={book.language}
          onMessage={handleWebViewMessage}
        />
      ) : null}

      <View style={[styles.footer, { borderTopColor: colors.border, backgroundColor: colors.surface }]}>
        <Pressable
          onPress={goPrev}
          disabled={currentSpineIndex <= 0}
          style={{ opacity: currentSpineIndex <= 0 ? 0.4 : 1 }}
          accessibilityLabel="Previous chapter"
        >
          <Text style={{ color: colors.accent, fontSize: 16 }}>← Prev</Text>
        </Pressable>
        <Text style={{ color: colors.textMuted, fontSize: 12 }}>
          {currentSpineIndex >= 0 ? `${currentSpineIndex + 1} / ${book.toc.length}` : ''}
        </Text>
        <Pressable
          onPress={goNext}
          disabled={currentSpineIndex < 0 || currentSpineIndex >= book.toc.length - 1}
          style={{
            opacity:
              currentSpineIndex < 0 || currentSpineIndex >= book.toc.length - 1 ? 0.4 : 1,
          }}
          accessibilityLabel="Next chapter"
        >
          <Text style={{ color: colors.accent, fontSize: 16 }}>Next →</Text>
        </Pressable>
      </View>

      <TableOfContents
        visible={tocOpen}
        toc={book.toc}
        currentChapterId={currentChapterId}
        onSelect={goToChapter}
        onClose={() => setTocOpen(false)}
      />
      <SettingsDrawer visible={settingsOpen} onClose={() => setSettingsOpen(false)} />
    </View>
  );
}

function HeaderButton({
  label,
  onPress,
  colors,
}: {
  label: string;
  onPress: () => void;
  colors: { accent: string };
}) {
  return (
    <Pressable onPress={onPress} style={styles.headerBtn}>
      <Text style={{ color: colors.accent, fontSize: 15 }}>{label}</Text>
    </Pressable>
  );
}

function CenteredLoader() {
  return (
    <View style={styles.center}>
      <ActivityIndicator />
    </View>
  );
}

function ErrorState({ message }: { message: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.center}>
      <Text style={{ color: colors.error }}>{message}</Text>
    </View>
  );
}

function parseAnchor(raw: string | undefined): { chapterId: string; scrollY?: number } | null {
  if (!raw) return null;
  const [chapterId, scrollStr] = raw.split(':');
  if (!chapterId) return null;
  const scrollY = scrollStr ? Number(scrollStr) : undefined;
  return { chapterId, scrollY: Number.isFinite(scrollY) ? scrollY : undefined };
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 12,
    paddingVertical: 10,
    borderBottomWidth: 1,
    gap: 8,
  },
  headerCenter: { flex: 1, paddingHorizontal: 8 },
  headerRight: { flexDirection: 'row', gap: 6 },
  bookTitle: { fontSize: 15, fontWeight: '600' },
  chapterTitle: { fontSize: 12, marginTop: 2 },
  headerBtn: { paddingHorizontal: 8, paddingVertical: 4 },
  footer: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingVertical: 10,
    borderTopWidth: 1,
  },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 24 },
});