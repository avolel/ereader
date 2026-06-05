import { useState } from 'react';
import {
  Dimensions,
  StyleSheet,
  Text,
  View,
  type LayoutChangeEvent,
} from 'react-native';

import AccessibleModal from './a11y/AccessibleModal';
import IconButton from './a11y/IconButton';
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
    <AccessibleModal
      visible
      onClose={onClose}
      label="Selection actions"
      align="custom"
      dimmed={false}
      panelStyle={[
        styles.panel,
        { top, left, backgroundColor: colors.surface, borderColor: colors.border },
        size ? null : styles.hidden,
      ]}
    >
      {/* Inner view carries onLayout — AccessibleModal owns the panel Pressable,
          so we measure the content (≈panel size minus padding) for clamping. */}
      <View onLayout={onLayout} style={styles.inner}>
        <View style={styles.swatchRow}>
          {COLOURS.map((c) => (
            <IconButton
              key={c}
              label={`Highlight ${c}`}
              onPress={() => onHighlight(c)}
              style={[styles.swatch, { backgroundColor: HIGHLIGHT_SWATCHES[c] }]}
            />
          ))}
        </View>
        <View style={[styles.divider, { backgroundColor: colors.border }]} />
        <IconButton label="Add note" onPress={onAddNote} style={styles.action}>
          <Text style={{ color: colors.accent, fontSize: 14 }}>Add note</Text>
        </IconButton>
        <IconButton label="Bookmark" onPress={onBookmark} style={styles.action}>
          <Text style={{ color: colors.accent, fontSize: 14 }}>Bookmark</Text>
        </IconButton>
        <IconButton label="Look up" onPress={onLookup} style={styles.action}>
          <Text style={{ color: colors.accent, fontSize: 14 }}>Look up</Text>
        </IconButton>
      </View>
    </AccessibleModal>
  );
}

const styles = StyleSheet.create({
  inner: { flexDirection: 'row', alignItems: 'center', gap: 8 },
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
