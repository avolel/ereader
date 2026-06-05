import { FlatList, StyleSheet, Text, View } from 'react-native';

import AccessibleModal from './a11y/AccessibleModal';
import IconButton from './a11y/IconButton';
import { useTheme } from '../providers/ThemeProvider';
import { TocEntry } from '../types';

type Props = {
  visible: boolean;
  toc: TocEntry[];
  currentChapterId: string | null;
  onSelect: (chapterId: string) => void;
  onClose: () => void;
};

export default function TableOfContents({
  visible,
  toc,
  currentChapterId,
  onSelect,
  onClose,
}: Props) {
  const { colors } = useTheme();

  return (
    <AccessibleModal
      visible={visible}
      onClose={onClose}
      label="Table of contents"
      // Left-edge full-height drawer: 'custom' leaves the panel at the backdrop's
      // start (top-left) and panelStyle sizes it.
      align="custom"
      panelStyle={[styles.panel, { backgroundColor: colors.surface, borderColor: colors.border }]}
    >
      <View style={[styles.header, { borderBottomColor: colors.border }]}>
        <Text style={[styles.title, { color: colors.text }]} accessibilityRole="header">
          Contents
        </Text>
        <IconButton label="Close table of contents" onPress={onClose}>
          <Text style={{ color: colors.accent, fontSize: 16 }}>Close</Text>
        </IconButton>
      </View>
      <FlatList
        data={toc}
        keyExtractor={(c) => c.chapterId}
        accessibilityRole="menu"
        style={styles.list}
        renderItem={({ item }) => {
          const active = item.chapterId === currentChapterId;
          const title = item.title ?? `Chapter ${item.spineOrder + 1}`;
          return (
            <IconButton
              label={title}
              accessibilityRole="menuitem"
              // Conveys the current chapter to AT, not just via the left border.
              selected={active}
              onPress={() => onSelect(item.chapterId)}
              style={[
                styles.row,
                {
                  backgroundColor: active ? colors.background : 'transparent',
                  borderLeftColor: active ? colors.accent : 'transparent',
                },
              ]}
            >
              <Text
                numberOfLines={2}
                style={[styles.rowText, { color: active ? colors.accent : colors.text }]}
              >
                {title}
              </Text>
            </IconButton>
          );
        }}
      />
    </AccessibleModal>
  );
}

const styles = StyleSheet.create({
  panel: {
    width: '80%',
    maxWidth: 380,
    height: '100%',
    borderRightWidth: 1,
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
  row: {
    paddingHorizontal: 16,
    paddingVertical: 12,
    borderLeftWidth: 3,
  },
  rowText: { fontSize: 14 },
});
