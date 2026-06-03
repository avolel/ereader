import { useMemo } from 'react';
import { Modal, Pressable, SectionList, StyleSheet, Text, View } from 'react-native';

import { useTheme } from '../providers/ThemeProvider';
import { HIGHLIGHT_SWATCHES } from './SelectionMenu';
import type { FlashTarget } from './ReaderWebView';
import { Annotation, Bookmark, TextAnchor, TocEntry } from '../types';

type Props = {
  visible: boolean;
  toc: TocEntry[];
  annotations: Annotation[];
  bookmarks: Bookmark[];
  currentChapterId: string | null;
  onNavigate: (chapterId: string, target: FlashTarget) => void;
  onDeleteAnnotation: (id: string) => void;
  onDeleteBookmark: (id: string) => void;
  onClose: () => void;
};

type Row =
  | { kind: 'annotation'; item: Annotation }
  | { kind: 'bookmark'; item: Bookmark };

type ChapterSection = { title: string; chapterId: string; data: Row[] };

// FR-24 consolidated view: every highlight, note, and bookmark in the book,
// grouped by chapter in spine order, newest-first within each chapter.
export default function AnnotationsDrawer({
  visible,
  toc,
  annotations,
  bookmarks,
  currentChapterId,
  onNavigate,
  onDeleteAnnotation,
  onDeleteBookmark,
  onClose,
}: Props) {
  const { colors } = useTheme();

  const sections = useMemo<ChapterSection[]>(() => {
    // Bucket rows by chapterId so we can emit them in toc (spine) order.
    const byChapter = new Map<string, Row[]>();
    const push = (chapterId: string | null, row: Row) => {
      if (!chapterId) return; // orphaned (null chapter) — not surfaced here
      const list = byChapter.get(chapterId) ?? [];
      list.push(row);
      byChapter.set(chapterId, list);
    };
    annotations.forEach((item) => push(item.chapterId, { kind: 'annotation', item }));
    bookmarks.forEach((item) => push(item.chapterId, { kind: 'bookmark', item }));

    return toc
      .map((entry) => {
        const rows = byChapter.get(entry.chapterId) ?? [];
        // Newest first within the chapter.
        rows.sort((a, b) => createdAt(b).localeCompare(createdAt(a)));
        return {
          title: entry.title ?? `Chapter ${entry.spineOrder + 1}`,
          chapterId: entry.chapterId,
          data: rows,
        };
      })
      .filter((s) => s.data.length > 0);
  }, [toc, annotations, bookmarks]);

  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <Pressable style={styles.backdrop} onPress={onClose}>
        <Pressable
          onPress={() => {}}
          style={[styles.panel, { backgroundColor: colors.surface, borderColor: colors.border }]}
        >
          <View style={[styles.header, { borderBottomColor: colors.border }]}>
            <Text style={[styles.title, { color: colors.text }]}>Annotations</Text>
            <Pressable onPress={onClose} accessibilityRole="button" accessibilityLabel="Close annotations">
              <Text style={{ color: colors.accent, fontSize: 16 }}>Close</Text>
            </Pressable>
          </View>

          <SectionList
            sections={sections}
            keyExtractor={(row) => `${row.kind}:${row.item.id}`}
            stickySectionHeadersEnabled={false}
            ListEmptyComponent={
              <Text style={[styles.empty, { color: colors.textMuted }]}>
                No highlights, notes, or bookmarks yet.
              </Text>
            }
            renderSectionHeader={({ section }) => (
              <Text
                style={[styles.sectionHeader, { color: colors.textMuted, backgroundColor: colors.background }]}
                numberOfLines={1}
              >
                {section.title}
              </Text>
            )}
            renderItem={({ item: row, section }) => {
              const active = section.chapterId === currentChapterId;
              const target: FlashTarget =
                row.kind === 'annotation'
                  ? { id: row.item.id }
                  : { anchor: parseAnchor(row.item.textAnchor) };
              return (
                <View
                  style={[
                    styles.row,
                    { backgroundColor: active ? colors.background : 'transparent' },
                  ]}
                >
                  <Pressable
                    onPress={() => onNavigate(section.chapterId, target)}
                    accessibilityRole="button"
                    accessibilityLabel={`Go to ${rowLabel(row)}`}
                    style={styles.rowMain}
                  >
                    {row.kind === 'annotation' && row.item.colour ? (
                      <View style={[styles.dot, { backgroundColor: HIGHLIGHT_SWATCHES[row.item.colour] }]} />
                    ) : (
                      <View style={[styles.dot, styles.dotPlain, { borderColor: colors.border }]} />
                    )}
                    <View style={styles.rowBody}>
                      <Text numberOfLines={2} style={[styles.rowText, { color: colors.text }]}>
                        {rowLabel(row)}
                      </Text>
                      {row.kind === 'annotation' && row.item.noteBody ? (
                        <Text numberOfLines={2} style={[styles.note, { color: colors.textMuted }]}>
                          {row.item.noteBody}
                        </Text>
                      ) : null}
                    </View>
                  </Pressable>
                  <Pressable
                    onPress={() =>
                      row.kind === 'annotation'
                        ? onDeleteAnnotation(row.item.id)
                        : onDeleteBookmark(row.item.id)
                    }
                    accessibilityRole="button"
                    accessibilityLabel="Delete"
                    hitSlop={8}
                    style={styles.delete}
                  >
                    <Text style={{ color: colors.error, fontSize: 13 }}>Delete</Text>
                  </Pressable>
                </View>
              );
            }}
          />
        </Pressable>
      </Pressable>
    </Modal>
  );
}

function createdAt(row: Row): string {
  return row.item.createdAt;
}

function rowLabel(row: Row): string {
  if (row.kind === 'bookmark') {
    return row.item.label ?? 'Bookmark';
  }
  return row.item.selectedText || '(no text)';
}

// Bookmark anchors are JSON-encoded TextAnchor strings. A malformed one
// shouldn't crash the drawer — fall back to a chapter-top anchor.
function parseAnchor(raw: string): TextAnchor {
  try {
    return JSON.parse(raw) as TextAnchor;
  } catch {
    return { chapterId: '', start: 0, end: 0, prefix: '', exact: '', suffix: '' };
  }
}

const styles = StyleSheet.create({
  backdrop: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.4)',
    alignItems: 'flex-end',
    justifyContent: 'center',
  },
  panel: {
    width: '85%',
    maxWidth: 420,
    height: '100%',
    borderLeftWidth: 1,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingVertical: 14,
    borderBottomWidth: 1,
  },
  title: { fontSize: 18, fontWeight: '700' },
  sectionHeader: {
    fontSize: 12,
    fontWeight: '600',
    textTransform: 'uppercase',
    paddingHorizontal: 16,
    paddingVertical: 6,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    paddingRight: 12,
  },
  rowMain: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'flex-start',
    paddingHorizontal: 16,
    paddingVertical: 12,
    gap: 10,
  },
  dot: { width: 12, height: 12, borderRadius: 6, marginTop: 3 },
  dotPlain: { borderWidth: 1 },
  rowBody: { flex: 1 },
  rowText: { fontSize: 14 },
  note: { fontSize: 13, marginTop: 4, fontStyle: 'italic' },
  delete: { paddingVertical: 12, paddingLeft: 8 },
  empty: { padding: 24, textAlign: 'center', fontSize: 14 },
});
