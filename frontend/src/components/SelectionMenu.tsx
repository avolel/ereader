import { useEffect } from 'react';
import { Modal, Pressable, StyleSheet, Text, View } from 'react-native';

import { useTheme } from '../providers/ThemeProvider';
import type { DOMRectLike } from './ReaderWebView';
import { HighlightColour } from '../types';

// Visible swatch fills for each highlight colour. Kept here (UI concern) rather
// than in types, so the data layer stays presentation-agnostic.
export const HIGHLIGHT_SWATCHES: Record<HighlightColour, string> = {
  yellow: '#ffe066',
  green: '#8ce99a',
  blue: '#74c0fc',
  pink: '#faa2c1',
  orange: '#ffc078',
};

const COLOURS = Object.keys(HIGHLIGHT_SWATCHES) as HighlightColour[];

type Props = {
  rect: DOMRectLike;
  onHighlight: (colour: HighlightColour) => void;
  onAddNote: () => void;
  onBookmark: () => void;
  onClose: () => void;
};

// Selection action popover. Positioned just above the selection rect. Rendered
// inside a transparent Modal so a tap anywhere outside dismisses it.
export default function SelectionMenu({
  rect,
  onHighlight,
  onAddNote,
  onBookmark,
  onClose,
}: Props) {
  const { colors } = useTheme();

  // Keyboard dismissal. On web the Modal's onRequestClose doesn't fire for Esc,
  // so we listen directly. Guarded for native, where `document` is undefined.
  useEffect(() => {
    if (typeof document === 'undefined') return;
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  // Anchor the popover above the selection; clamp the top so it never goes
  // off-screen when the selection is near the very top of the viewport.
  const top = Math.max(8, rect.y - 56);
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
                accessibilityLabel={`Highlight ${c}`}
                focusable
                onPress={() => onHighlight(c)}
                style={[styles.swatch, { backgroundColor: HIGHLIGHT_SWATCHES[c] }]}
              />
            ))}
          </View>
          <View style={[styles.divider, { backgroundColor: colors.border }]} />
          <Pressable
            onPress={onAddNote}
            accessibilityRole="button"
            accessibilityLabel="Add note"
            focusable
            style={styles.action}
          >
            <Text style={{ color: colors.accent, fontSize: 14 }}>Add note</Text>
          </Pressable>
          <Pressable
            onPress={onBookmark}
            accessibilityRole="button"
            accessibilityLabel="Bookmark"
            focusable
            style={styles.action}
          >
            <Text style={{ color: colors.accent, fontSize: 14 }}>Bookmark</Text>
          </Pressable>
        </Pressable>
      </Pressable>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1 },
  panel: {
    position: 'absolute',
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 10,
    paddingVertical: 8,
    borderWidth: 1,
    borderRadius: 10,
    gap: 8,
  },
  swatchRow: { flexDirection: 'row', gap: 6 },
  swatch: { width: 22, height: 22, borderRadius: 11 },
  divider: { width: 1, height: 24, marginHorizontal: 4 },
  action: { paddingHorizontal: 6, paddingVertical: 4 },
});
