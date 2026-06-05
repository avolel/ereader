import { useEffect, useState } from 'react';
import {
  Dimensions,
  Modal,
  Pressable,
  StyleSheet,
  Text,
  View,
  type LayoutChangeEvent,
} from 'react-native';

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
  onLookup: () => void;
  onClose: () => void;
};

// Selection action popover. Positioned just above the selection rect. Rendered
// inside a transparent Modal so a tap anywhere outside dismisses it.
export default function SelectionMenu({
  rect,
  onHighlight,
  onAddNote,
  onBookmark,
  onLookup,
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

  // The panel width/height depend on content, so we measure it on first layout
  // and clamp against the viewport on the next render. Until measured we hide it
  // (opacity 0) to avoid a visible jump from an unclamped position.
  const [size, setSize] = useState<{ width: number; height: number } | null>(null);
  function onLayout(e: LayoutChangeEvent) {
    const { width, height } = e.nativeEvent.layout;
    if (!size || size.width !== width || size.height !== height) {
      setSize({ width, height });
    }
  }

  // Anchor the popover above the selection, then clamp all four edges so it
  // stays fully on-screen — left/top minimums plus right/bottom maximums (the
  // latter need the measured panel size). Prefer above the selection; if there
  // isn't room, drop below it.
  const MARGIN = 8;
  const { width: vw, height: vh } = Dimensions.get('window');

  let top = rect.y - 56;
  let left = rect.x;
  if (size) {
    if (top < MARGIN) top = rect.y + rect.height + MARGIN; // no room above → go below
    top = Math.min(top, vh - size.height - MARGIN);
    left = Math.min(left, vw - size.width - MARGIN);
  }
  top = Math.max(MARGIN, top);
  left = Math.max(MARGIN, left);

  return (
    <Modal visible transparent animationType="fade" onRequestClose={onClose}>
      <Pressable style={styles.backdrop} onPress={onClose}>
        <Pressable
          onPress={() => {}}
          onLayout={onLayout}
          style={[
            styles.panel,
            { top, left, backgroundColor: colors.surface, borderColor: colors.border },
            size ? null : styles.hidden,
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
          <Pressable
            onPress={onLookup}
            accessibilityRole="button"
            accessibilityLabel="Look up"
            focusable
            style={styles.action}
          >
            <Text style={{ color: colors.accent, fontSize: 14 }}>Look up</Text>
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
  hidden: { opacity: 0 },
  swatchRow: { flexDirection: 'row', gap: 6 },
  swatch: { width: 22, height: 22, borderRadius: 11 },
  divider: { width: 1, height: 24, marginHorizontal: 4 },
  action: { paddingHorizontal: 6, paddingVertical: 4 },
});
