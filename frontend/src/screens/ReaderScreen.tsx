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
  RenderHighlight,
  DOMRectLike,
  FlashTarget,
} from '../components/ReaderWebView';
import SettingsDrawer from '../components/SettingsDrawer';
import TableOfContents from '../components/TableOfContents';
import ConfirmDialog from '../components/ConfirmDialog';
import SelectionMenu from '../components/SelectionMenu';
import AnnotationPopover from '../components/AnnotationPopover';
import AnnotationsDrawer from '../components/AnnotationsDrawer'; // FR-24 consolidated view
import NoteEditor from '../components/NoteEditor';
import { useDeleteBook } from '../hooks/useDeleteBook';
import {
  useAnnotations, useCreateAnnotation, useUpdateAnnotation, useDeleteAnnotation,
} from '../hooks/useAnnotations';
import { useBookmarks, useCreateBookmark, useDeleteBookmark } from '../hooks/useBookmarks';
import { useBook } from '../hooks/useBook';
import { useBookSettings, useUpdatePosition } from '../hooks/useReadingSettings';
import { useChapter } from '../hooks/useChapter';
import { useTheme } from '../providers/ThemeProvider';
import { extractApiError } from '../services/errors';
import { Annotation, HighlightColour, TextAnchor } from '../types';
import LookupOverlay from '../components/LookupOverlay';

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
  const bookmarksQuery = useBookmarks(bookId);
  const annotationsQuery = useAnnotations(bookId);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const deleteMutation = useDeleteBook();
  const positionMutation = useUpdatePosition(bookId ?? '');

  const createAnnotation = useCreateAnnotation(bookId ?? '');
  const updateAnnotation = useUpdateAnnotation(bookId ?? '');
  const deleteAnnotation = useDeleteAnnotation(bookId ?? '');
  const createBookmark = useCreateBookmark(bookId ?? '');
  const deleteBookmark = useDeleteBookmark(bookId ?? '');

  // currentChapterId is local state, seeded from saved position once both
  // book and settings have loaded. anchor (from search results) wins if set.
  const [currentChapterId, setCurrentChapterId] = useState<string | null>(null);
  const [lookupTerm, setLookupTerm] = useState<string | null>(null);
  const [initialScrollY, setInitialScrollY] = useState(0);
  const [tocOpen, setTocOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [annotationsOpen, setAnnotationsOpen] = useState(false); // FR-24 drawer
  // Selection action popover: anchor (sans chapterId, attached on save) + where to position it.
  const [selection, setSelection] = useState<
    { anchor: Omit<TextAnchor, 'chapterId'>; selectedText: string; rect: DOMRectLike } | null
  >(null);
  // Tapped existing highlight → edit/delete popover.
  const [activeHighlight, setActiveHighlight] = useState<
    { annotation: Annotation; rect: DOMRectLike } | null
  >(null);
  // Note editor (shared by create-from-selection and edit-existing).
  const [noteEditor, setNoteEditor] = useState<
    | { mode: 'create'; anchor: Omit<TextAnchor, 'chapterId'>; selectedText: string; chapterId: string }
    | { mode: 'edit'; id: string; initialBody: string }
    | null
  >(null);
  // Gates applyHighlights() until the WebView reports 'ready' (DOM laid out).
  const [webViewReady, setWebViewReady] = useState(false);
  const webRef = useRef<ReaderWebViewHandle>(null);
  // A flash queued while navigating to another chapter; flushed once that
  // chapter's WebView reports ready (see the applyHighlights effect).
  const pendingFlashRef = useRef<FlashTarget | null>(null);
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

  // Annotations → render marks for the chapter currently on screen.
  // Bookmarks are NOT marks; they only feed the drawer/creation, so they're excluded here.
  const currentChapterHighlights = useMemo<RenderHighlight[]>(() => {
    const list = annotationsQuery.data ?? [];
    return list
      .filter((a) => a.chapterId === currentChapterId)
      .map((a) => ({
        id: a.id,
        anchor: JSON.parse(a.textAnchor) as TextAnchor,
        // Notes carry colour: null; give their mark a default swatch.
        colour: (a.colour ?? 'yellow') as HighlightColour,
      }));
  }, [annotationsQuery.data, currentChapterId]);

  // First paint is seeded via the `highlights` prop (buildChapterDocument). This
  // effect handles *live* add/update/delete within a chapter by re-injecting the
  // full list. Reapplying identical marks is idempotent, so we don't diff.
  useEffect(() => {
    if (!webViewReady) return;
    webRef.current?.applyHighlights(currentChapterHighlights);
    // Flush a flash queued by a cross-chapter "jump to" once marks are in place.
    if (pendingFlashRef.current) {
      webRef.current?.flashTo(pendingFlashRef.current);
      pendingFlashRef.current = null;
    }
  }, [currentChapterHighlights, webViewReady]);

  // "Jump to" from the consolidated drawer. Same chapter → flash now; different
  // chapter → switch and let the ready-effect flush the queued flash.
  const navigateToEntry = useCallback(
    (chapterId: string, target: FlashTarget) => {
      setAnnotationsOpen(false);
      if (chapterId === currentChapterId) {
        if (webViewReady) webRef.current?.flashTo(target);
        else pendingFlashRef.current = target;
      } else {
        pendingFlashRef.current = target;
        goToChapter(chapterId);
      }
    },
    // goToChapter is stable (useCallback []); other deps are primitives/state.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [currentChapterId, webViewReady],
  );

  const goToChapter = useCallback((chapterId: string) => {
    flushPosition();
    setCurrentChapterId(chapterId);
    setInitialScrollY(0);
    lastScrollYRef.current = 0;
    setWebViewReady(false); // WebView remounts on chapter change; wait for 'ready' again
    setSelection(null);
    setActiveHighlight(null);
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

  // Stamp the current chapter onto a selection anchor to form a full TextAnchor.
  function withChapter(anchor: Omit<TextAnchor, 'chapterId'>): TextAnchor {
    return { ...anchor, chapterId: currentChapterId ?? '' };
  }

  function handleHighlight(colour: HighlightColour) {
    if (!selection || !currentChapterId) return;
    createAnnotation.mutate({
      type: 'highlight',
      chapterId: currentChapterId,
      colour,
      textAnchor: JSON.stringify(withChapter(selection.anchor)),
      selectedText: selection.selectedText,
      noteBody: null,
    });
    setSelection(null);
  }

  // "Add note" from the selection menu: stash the selection and open the editor.
  function openNoteEditorForSelection() {
    if (!selection || !currentChapterId) return;
    setNoteEditor({
      mode: 'create',
      anchor: selection.anchor,
      selectedText: selection.selectedText,
      chapterId: currentChapterId,
    });
    setSelection(null);
  }

  // NoteEditor save: create a new note annotation, or patch an existing one.
  function handleNoteSave(body: string) {
    if (!noteEditor) return;
    if (noteEditor.mode === 'create') {
      // Empty body on create → nothing to save (treat as cancel).
      if (body) {
        createAnnotation.mutate({
          type: 'note',
          chapterId: noteEditor.chapterId,
          colour: null,
          textAnchor: JSON.stringify({ ...noteEditor.anchor, chapterId: noteEditor.chapterId }),
          selectedText: noteEditor.selectedText,
          noteBody: body,
        });
      }
    } else {
      updateAnnotation.mutate({ id: noteEditor.id, input: { noteBody: body } });
    }
    setNoteEditor(null);
  }

  // anchor omitted → chapter-top marker so a bookmark can be dropped without selecting text.
  function handleBookmark(anchor?: Omit<TextAnchor, 'chapterId'>) {
    if (!currentChapterId) return;
    const full: TextAnchor = anchor
      ? withChapter(anchor)
      : { chapterId: currentChapterId, start: 0, end: 0, prefix: '', exact: '', suffix: '' };
    createBookmark.mutate({ chapterId: currentChapterId, textAnchor: JSON.stringify(full), label: null });
    setSelection(null);
  }

  const handleWebViewMessage = useCallback(
    (msg: ReaderWebViewMessage) => {
      switch (msg.type) {
        case 'scroll':
          lastScrollYRef.current = msg.scrollY;
          schedulePositionSave();
          break;
        case 'ready':
          setWebViewReady(true);
          break;
        case 'selection':
          setActiveHighlight(null);
          setSelection({ anchor: msg.anchor, selectedText: msg.selectedText, rect: msg.rect });
          break;
        case 'highlightTap': {
          const annotation = (annotationsQuery.data ?? []).find((a) => a.id === msg.id);
          if (!annotation) break; // cache/DOM out of sync — ignore the tap
          setSelection(null);
          setActiveHighlight({ annotation, rect: msg.rect });
          break;
        }
        case 'anchorMiss':
          // Highlight couldn't be re-anchored after reflow. Non-fatal; the drawer
          // is where orphaned marks should surface. No inline action here.
          break;
      }
    },
    [annotationsQuery.data],
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
          <HeaderButton label="✎" onPress={() => setAnnotationsOpen(true)} colors={colors} />
          <HeaderButton label="Aa" onPress={() => setSettingsOpen(true)} colors={colors} />
          <HeaderButton label="⋯" onPress={() => setConfirmDelete(true)} colors={colors} />
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
          highlights={currentChapterHighlights}
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

      {selection && (
        <SelectionMenu
          rect={selection.rect}
          onHighlight={handleHighlight}
          onAddNote={openNoteEditorForSelection}
          onBookmark={() => handleBookmark(selection.anchor)}
          onClose={() => setSelection(null)}
          onLookup={() => {
            if (!selection) return;
            setLookupTerm(selection.selectedText);
            setSelection(null);
          }}
        />
      )}

      {lookupTerm && (
        <LookupOverlay term={lookupTerm} onClose={() => setLookupTerm(null)} />
      )}

      {activeHighlight && (
        <AnnotationPopover
          annotation={activeHighlight.annotation}
          rect={activeHighlight.rect}
          onChangeColour={(colour) =>
            updateAnnotation.mutate({ id: activeHighlight.annotation.id, input: { colour } })}
          onRequestEditNote={() => {
            setNoteEditor({
              mode: 'edit',
              id: activeHighlight.annotation.id,
              initialBody: activeHighlight.annotation.noteBody ?? '',
            });
            setActiveHighlight(null);
          }}
          onDelete={() => {
            deleteAnnotation.mutate(activeHighlight.annotation.id);
            setActiveHighlight(null);
          }}
          onClose={() => setActiveHighlight(null)}
        />
      )}

      <NoteEditor
        visible={!!noteEditor}
        initialBody={noteEditor?.mode === 'edit' ? noteEditor.initialBody : ''}
        title={noteEditor?.mode === 'edit' ? 'Edit note' : 'Add note'}
        onSave={handleNoteSave}
        onCancel={() => setNoteEditor(null)}
      />

      <AnnotationsDrawer
        visible={annotationsOpen}
        toc={book.toc}
        annotations={annotationsQuery.data ?? []}
        bookmarks={bookmarksQuery.data ?? []}
        currentChapterId={currentChapterId}
        onNavigate={navigateToEntry}
        onDeleteAnnotation={(id) => deleteAnnotation.mutate(id)}
        onDeleteBookmark={(id) => deleteBookmark.mutate(id)}
        onClose={() => setAnnotationsOpen(false)}
      />
      <ConfirmDialog
        visible={confirmDelete}
        title="Delete book"
        message={` "${book.title}" and its bookmarks, highlights, and reading progress will be permanently deleted.`}
        confirmLabel="Delete"
        destructive
        busy={deleteMutation.isPending}
        onConfirm={async () => {
          if (!bookId) return;
          try {
            await deleteMutation.mutateAsync(bookId);
            router.back(); // back to library; useDeleteBook already invalidated the list
          } catch {
            // surfaced via deleteMutation.error
          } finally {
            setConfirmDelete(false);
          }
        }}
        onCancel={() => setConfirmDelete(false)}
      />
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