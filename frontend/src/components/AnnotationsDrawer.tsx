import { useMemo } from 'react';
import { SectionList, StyleSheet, Text, View } from 'react-native';

import AccessibleModal from './a11y/AccessibleModal';
import IconButton from './a11y/IconButton';
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
    <AccessibleModal
      visible={visible}
      onClose={onClose}
      label="Annotations and bookmarks"
      // Right-edge full-height drawer: 'custom' + alignSelf positions it.
      align="custom"
      panelStyle={[styles.panel, { backgroundColor: colors.surface, borderColor: colors.border }]}
    >
      <View style={[styles.header, { borderBottomColor: colors.border }]}>
        <Text style={[styles.title, { color: colors.text }]} accessibilityRole="header">
          Annotations
        </Text>
        <IconButton label="Close annotations" onPress={onClose}>
          <Text style={{ color: colors.accent, fontSize: 16 }}>Close</Text>
        </IconButton>
      </View>

      <SectionList
        sections={sections}
        keyExtractor={(row) => `${row.kind}:${row.item.id}`}
        stickySectionHeadersEnabled={false}
        style={styles.list}
        ListEmptyComponent={
          <Text style={[styles.empty, { color: colors.textMuted }]}>
            No highlights, notes, or bookmarks yet.
          </Text>
        }
        renderSectionHeader={({ section }) => (
          <Text
            accessibilityRole="header"
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
              <IconButton
                // Colour is folded into the accessible name so the swatch dot
                // isn't a colour-only affordance (1.4.1).
                label={`Go to ${rowAccessibleLabel(row)}`}
                onPress={() => onNavigate(section.chapterId, target)}
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
              </IconButton>
              <IconButton
                label={`Delete ${rowAccessibleLabel(row)}`}
                onPress={() =>
                  row.kind === 'annotation'
                    ? onDeleteAnnotation(row.item.id)
                    : onDeleteBookmark(row.item.id)
                }
                hitSlop={8}
                style={styles.delete}
              >
                <Text style={{ color: colors.error, fontSize: 13 }}>Delete</Text>
              </IconButton>
            </View>
          );
        }}
      />
    </AccessibleModal>
  );
}

// Accessible name for a row that folds in the colour/type so AT users get the
// same information the coloured dot conveys visually.
function rowAccessibleLabel(row: Row): string {
  if (row.kind === 'bookmark') return `bookmark: ${rowLabel(row)}`;
  const colour = row.item.colour ? `${row.item.colour} ` : '';
  return `${colour}${row.item.type}: ${rowLabel(row)}`;
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
  panel: {
    width: '85%',
    maxWidth: 420,
    height: '100%',
    alignSelf: 'flex-end', // pin to the right edge within the 'custom' backdrop
    borderLeftWidth: 1,
  },
  list: { flex: 1 },
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
