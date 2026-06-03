import { useEffect, useState } from 'react';
import { Modal, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';

import { useTheme } from '../providers/ThemeProvider';

type Props = {
  visible: boolean;
  // Pre-fills the field when editing an existing note; empty for a new note.
  initialBody?: string;
  // Lets the host distinguish "Add note" vs "Edit note" in the header.
  title?: string;
  onSave: (body: string) => void;
  onCancel: () => void;
};

// Bottom-sheet modal for the note body. Single editing surface for both create
// and edit so we don't maintain two note inputs. Styled to match SettingsDrawer.
export default function NoteEditor({
  visible,
  initialBody = '',
  title = 'Note',
  onSave,
  onCancel,
}: Props) {
  const { colors } = useTheme();
  const [body, setBody] = useState(initialBody);

  // Re-seed when (re)opened or when editing a different note.
  useEffect(() => {
    if (visible) setBody(initialBody);
  }, [visible, initialBody]);

  // Esc to cancel on web (Modal.onRequestClose doesn't cover it there).
  useEffect(() => {
    if (!visible || typeof document === 'undefined') return;
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onCancel();
    }
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [visible, onCancel]);

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onCancel}>
      <Pressable style={styles.backdrop} onPress={onCancel}>
        <Pressable
          onPress={() => {}}
          style={[styles.panel, { backgroundColor: colors.surface, borderColor: colors.border }]}
        >
          <View style={[styles.header, { borderBottomColor: colors.border }]}>
            <Pressable onPress={onCancel} accessibilityRole="button" accessibilityLabel="Cancel">
              <Text style={{ color: colors.accent, fontSize: 16 }}>Cancel</Text>
            </Pressable>
            <Text style={[styles.title, { color: colors.text }]}>{title}</Text>
            <Pressable
              onPress={() => onSave(body.trim())}
              accessibilityRole="button"
              accessibilityLabel="Save note"
            >
              <Text style={{ color: colors.accent, fontSize: 16, fontWeight: '600' }}>Save</Text>
            </Pressable>
          </View>

          <TextInput
            value={body}
            onChangeText={setBody}
            placeholder="Write your note…"
            placeholderTextColor={colors.textMuted}
            multiline
            autoFocus
            style={[styles.input, { color: colors.text, backgroundColor: colors.background, borderColor: colors.border }]}
          />
        </Pressable>
      </Pressable>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.4)', justifyContent: 'flex-end' },
  panel: {
    maxHeight: '80%',
    borderTopLeftRadius: 12,
    borderTopRightRadius: 12,
    borderWidth: 1,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingVertical: 14,
    borderBottomWidth: 1,
  },
  title: { fontSize: 16, fontWeight: '700' },
  input: {
    margin: 16,
    minHeight: 120,
    borderWidth: 1,
    borderRadius: 8,
    padding: 12,
    fontSize: 15,
    textAlignVertical: 'top',
  },
});
