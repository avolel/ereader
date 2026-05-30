import { FlatList, Modal, Pressable, StyleSheet, Text, View } from 'react-native';

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
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <Pressable style={styles.backdrop} onPress={onClose}>
        {/* Stop propagation so taps inside the panel don't dismiss it. */}
        <Pressable
          onPress={() => {}}
          style={[
            styles.panel,
            { backgroundColor: colors.surface, borderColor: colors.border },
          ]}
        >
          <View style={[styles.header, { borderBottomColor: colors.border }]}>
            <Text style={[styles.title, { color: colors.text }]}>Contents</Text>
            <Pressable onPress={onClose} accessibilityLabel="Close table of contents">
              <Text style={{ color: colors.accent, fontSize: 16 }}>Close</Text>
            </Pressable>
          </View>
          <FlatList
            data={toc}
            keyExtractor={(c) => c.chapterId}
            renderItem={({ item }) => {
              const active = item.chapterId === currentChapterId;
              return (
                <Pressable
                  onPress={() => onSelect(item.chapterId)}
                  style={({ pressed }) => [
                    styles.row,
                    {
                      backgroundColor:
                        active || pressed ? colors.background : 'transparent',
                      borderLeftColor: active ? colors.accent : 'transparent',
                    },
                  ]}
                >
                  <Text
                    numberOfLines={2}
                    style={[
                      styles.rowText,
                      { color: active ? colors.accent : colors.text },
                    ]}
                  >
                    {item.title ?? `Chapter ${item.spineOrder + 1}`}
                  </Text>
                </Pressable>
              );
            }}
          />
        </Pressable>
      </Pressable>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.4)',
    alignItems: 'flex-start',
    justifyContent: 'center',
  },
  panel: {
    width: '80%',
    maxWidth: 380,
    height: '100%',
    borderRightWidth: 1,
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
  row: {
    paddingHorizontal: 16,
    paddingVertical: 12,
    borderLeftWidth: 3,
  },
  rowText: { fontSize: 14 },
});