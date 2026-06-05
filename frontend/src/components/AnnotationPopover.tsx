import { StyleSheet, Text, View } from 'react-native';

import AccessibleModal from './a11y/AccessibleModal';
import IconButton from './a11y/IconButton';
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

  const top = Math.max(8, rect.y + rect.height + 8);
  const left = Math.max(8, rect.x);

  return (
    <AccessibleModal
      visible
      onClose={onClose}
      label="Annotation actions"
      align="custom"
      dimmed={false}
      panelStyle={[
        styles.panel,
        { top, left, backgroundColor: colors.surface, borderColor: colors.border },
      ]}
    >
      <View style={styles.swatchRow}>
        {COLOURS.map((c) => (
          <IconButton
            key={c}
            label={`Recolour ${c}`}
            selected={annotation.colour === c}
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
        <IconButton
          label={annotation.noteBody ? 'Edit note' : 'Add note'}
          onPress={onRequestEditNote}
          style={styles.action}
        >
          <Text style={{ color: colors.accent, fontSize: 14 }}>
            {annotation.noteBody ? 'Edit note' : 'Add note'}
          </Text>
        </IconButton>
        <IconButton label="Delete annotation" onPress={onDelete} style={styles.action}>
          <Text style={{ color: colors.error, fontSize: 14 }}>Delete</Text>
        </IconButton>
      </View>
    </AccessibleModal>
  );
}

const styles = StyleSheet.create({
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
