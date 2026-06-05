import { useLocalSearchParams, useRouter } from 'expo-router';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Platform,
  StyleSheet,
  Text,
  View,
} from 'react-native';

import IconButton from '../components/a11y/IconButton';
import { useAnnouncer } from '../components/a11y/useAnnouncer';
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
  const { announce } = useAnnouncer();
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
        // Drives the "has note" suffix in the mark's screen-reader label.
        hasNote: !!a.noteBody,
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

  // Memoized so the keyboard-shortcut effect below can depend on them without
  // re-subscribing the document listener on every render.
  const goPrev = useCallback(() => {
    if (!bookQuery.data || currentSpineIndex <= 0) return;
    goToChapter(bookQuery.data.toc[currentSpineIndex - 1].chapterId);
  }, [bookQuery.data, currentSpineIndex, goToChapter]);
  const goNext = useCallback(() => {
    if (!bookQuery.data || currentSpineIndex < 0) return;
    const next = bookQuery.data.toc[currentSpineIndex + 1];
    if (next) goToChapter(next.chapterId);
  }, [bookQuery.data, currentSpineIndex, goToChapter]);

  // On a *chapter change* (not the first paint), move focus into the chapter body
  // and announce the new chapter for screen readers (FR-32). Gated on webViewReady
  // so #er-content exists before we focus it.
  const announcedChapterRef = useRef<string | null>(null);
  useEffect(() => {
    if (!webViewReady || !currentChapterId || !bookQuery.data) return;
    if (announcedChapterRef.current === currentChapterId) return;
    const isFirst = announcedChapterRef.current === null;
    announcedChapterRef.current = currentChapterId;
    if (isFirst) return; // don't yank focus / announce on initial open
    const toc = bookQuery.data.toc;
    const idx = toc.findIndex((c) => c.chapterId === currentChapterId);
    const entry = idx >= 0 ? toc[idx] : null;
    const title = entry?.title ?? (entry ? `Chapter ${entry.spineOrder + 1}` : 'Chapter');
    webRef.current?.focusContent();
    announce(`${title}, chapter ${idx + 1} of ${toc.length}`);
  }, [webViewReady, currentChapterId, bookQuery.data, announce]);

  // Announce chapter load failures assertively so AT users aren't left on a
  // silent blank screen.
  useEffect(() => {
    if (chapterQuery.isError) announce('Failed to load chapter', true);
  }, [chapterQuery.isError, announce]);

  // Reader keyboard shortcuts (web only, FR-29). Arrow/Page keys page chapters.
  // Bail when a modal is open or focus is in a text field so we don't hijack
  // typing or compete with an overlay's own keys. Note: when focus is inside the
  // chapter iframe these don't fire (events don't cross the frame), so the iframe
  // keeps its native arrow-scroll there — intentional.
  useEffect(() => {
    if (Platform.OS !== 'web' || typeof document === 'undefined') return;
    const modalOpen =
      tocOpen || settingsOpen || annotationsOpen || confirmDelete ||
      !!selection || !!activeHighlight || !!noteEditor || !!lookupTerm;
    if (modalOpen) return;
    function onKey(e: KeyboardEvent) {
      const t = document.activeElement as HTMLElement | null;
      const tag = t?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || t?.isContentEditable) return;
      if (e.key === 'ArrowLeft' || e.key === 'PageUp') {
        e.preventDefault();
        goPrev();
      } else if (e.key === 'ArrowRight' || e.key === 'PageDown') {
        e.preventDefault();
        goNext();
      }
    }
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [
    goPrev, goNext, tocOpen, settingsOpen, annotationsOpen, confirmDelete,
    selection, activeHighlight, noteEditor, lookupTerm,
  ]);

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
    announce(`${colour} highlight added`);
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
        announce('Note added');
      }
    } else {
      updateAnnotation.mutate({ id: noteEditor.id, input: { noteBody: body } });
      announce('Note saved');
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
    announce('Bookmark set');
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
    <View
      style={[styles.container, { backgroundColor: colors.background }]}
      nativeID="main-content"
      role="main"
    >
      <View style={[styles.header, { borderBottomColor: colors.border, backgroundColor: colors.surface }]}>
        <IconButton label="Back to library" onPress={() => router.back()}>
          <Text style={{ color: colors.accent, fontSize: 16 }}>← Library</Text>
        </IconButton>
        <View style={styles.headerCenter} accessibilityRole="header">
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
          <IconButton label="Table of contents" onPress={() => setTocOpen(true)} style={styles.headerBtn}>
            <Text style={{ color: colors.accent, fontSize: 15 }}>TOC</Text>
          </IconButton>
          <IconButton label="Annotations and bookmarks" onPress={() => setAnnotationsOpen(true)} style={styles.headerBtn}>
            <Text style={{ color: colors.accent, fontSize: 18 }}>✎</Text>
          </IconButton>
          <IconButton label="Display settings" onPress={() => setSettingsOpen(true)} style={styles.headerBtn}>
            <Text style={{ color: colors.accent, fontSize: 15 }}>Aa</Text>
          </IconButton>
          <IconButton label="More options" onPress={() => setConfirmDelete(true)} style={styles.headerBtn}>
            <Text style={{ color: colors.accent, fontSize: 18 }}>⋯</Text>
          </IconButton>
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
          chapterTitle={currentTocEntry?.title ?? book.title}
          highlights={currentChapterHighlights}
          onMessage={handleWebViewMessage}
        />
      ) : null}

      <View style={[styles.footer, { borderTopColor: colors.border, backgroundColor: colors.surface }]}>
        <IconButton
          label="Previous chapter"
          onPress={goPrev}
          disabled={currentSpineIndex <= 0}
          style={{ opacity: currentSpineIndex <= 0 ? 0.4 : 1 }}
        >
          <Text style={{ color: colors.accent, fontSize: 16 }}>← Prev</Text>
        </IconButton>
        <Text style={{ color: colors.textMuted, fontSize: 12 }}>
          {currentSpineIndex >= 0 ? `${currentSpineIndex + 1} / ${book.toc.length}` : ''}
        </Text>
        <IconButton
          label="Next chapter"
          onPress={goNext}
          disabled={currentSpineIndex < 0 || currentSpineIndex >= book.toc.length - 1}
          style={{
            opacity:
              currentSpineIndex < 0 || currentSpineIndex >= book.toc.length - 1 ? 0.4 : 1,
          }}
        >
          <Text style={{ color: colors.accent, fontSize: 16 }}>Next →</Text>
        </IconButton>
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
            announce('Annotation deleted');
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
        onDeleteAnnotation={(id) => {
          deleteAnnotation.mutate(id);
          announce('Annotation deleted');
        }}
        onDeleteBookmark={(id) => {
          deleteBookmark.mutate(id);
          announce('Bookmark deleted');
        }}
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