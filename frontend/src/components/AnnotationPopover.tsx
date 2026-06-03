import { useEffect } from 'react';
import { Modal, Pressable, StyleSheet, Text, View } from 'react-native';

import { useTheme } from '../providers/ThemeProvider';
import type { DOMRectLike } from './ReaderWebView';
import { HIGHLIGHT_SWATCHES } from './SelectionMenu';
import { Annotation, HighlightColour } from '../types';

const COLOURS = Object.keys(HIGHLIGHT_SWATCHES) as HighlightColour[];

type Props = {
  annotation: Annotation;
  rect: DOMRectLike;
  onChangeColour: (colour: HighlightColour) => void;
  // Opens the shared NoteEditor (host owns that modal) rather than editing inline.
  onRequestEditNote: () => void;
  onDelete: () => void;
  onClose: () => void;
};

// Popover for an existing highlight/note: recolour, jump to the note editor, delete.
export default function AnnotationPopover({
  annotation,
  rect,
  onChangeColour,
  onRequestEditNote,
  onDelete,
  onClose,
}: Props) {
  const { colors } = useTheme();

  useEffect(() => {
    if (typeof document === 'undefined') return;
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  const top = Math.max(8, rect.y + rect.height + 8);
  const left = Math.max(8, rect.x);

  return (
    <Modal visible transparent animationType="fade" onRequestClose={onClose}>
      <Pressable style={styles.backdrop} onPress={onClose}>
        <Pressable
          onPress={() => {}}
          style={[
            styles.panel,
            { top, left, backgroundColor: colors.surface, borderColor: colors.border },
          ]}
        >
          <View style={styles.swatchRow}>
            {COLOURS.map((c) => (
              <Pressable
                key={c}
                accessibilityRole="button"
                accessibilityLabel={`Recolour ${c}`}
                focusable
                onPress={() => onChangeColour(c)}
                style={[
                  styles.swatch,
                  { backgroundColor: HIGHLIGHT_SWATCHES[c] },
                  annotation.colour === c && { borderColor: colors.text, borderWidth: 2 },
                ]}
              />
            ))}
          </View>

          {annotation.noteBody ? (
            <Text numberOfLines={3} style={[styles.note, { color: colors.textMuted }]}>
              {annotation.noteBody}
            </Text>
          ) : null}

          <View style={styles.actions}>
            <Pressable
              onPress={onRequestEditNote}
              accessibilityRole="button"
              accessibilityLabel={annotation.noteBody ? 'Edit note' : 'Add note'}
              style={styles.action}
            >
              <Text style={{ color: colors.accent, fontSize: 14 }}>
                {annotation.noteBody ? 'Edit note' : 'Add note'}
              </Text>
            </Pressable>
            <Pressable
              onPress={onDelete}
              accessibilityRole="button"
              accessibilityLabel="Delete annotation"
              style={styles.action}
            >
              <Text style={{ color: colors.error, fontSize: 14 }}>Delete</Text>
            </Pressable>
          </View>
        </Pressable>
      </Pressable>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1 },
  panel: {
    position: 'absolute',
    width: 240,
    padding: 12,
    borderWidth: 1,
    borderRadius: 10,
    gap: 10,
  },
  swatchRow: { flexDirection: 'row', gap: 6 },
  swatch: { width: 22, height: 22, borderRadius: 11 },
  note: { fontSize: 13, fontStyle: 'italic' },
  actions: { flexDirection: 'row', justifyContent: 'space-between' },
  action: { paddingHorizontal: 6, paddingVertical: 4 },
});
