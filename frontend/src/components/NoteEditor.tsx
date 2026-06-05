import { useEffect, useState } from 'react';
import { StyleSheet, Text, TextInput, View } from 'react-native';

import AccessibleModal from './a11y/AccessibleModal';
import IconButton from './a11y/IconButton';
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

  return (
    <AccessibleModal
      visible={visible}
      onClose={onCancel}
      label={title}
      animationType="slide"
      align="bottom"
      panelStyle={[styles.panel, { backgroundColor: colors.surface, borderColor: colors.border }]}
    >
      <View style={[styles.header, { borderBottomColor: colors.border }]}>
        <IconButton label="Cancel" onPress={onCancel}>
          <Text style={{ color: colors.accent, fontSize: 16 }}>Cancel</Text>
        </IconButton>
        <Text style={[styles.title, { color: colors.text }]} accessibilityRole="header">
          {title}
        </Text>
        <IconButton label="Save note" onPress={() => onSave(body.trim())}>
          <Text style={{ color: colors.accent, fontSize: 16, fontWeight: '600' }}>Save</Text>
        </IconButton>
      </View>

      <TextInput
        value={body}
        onChangeText={setBody}
        placeholder="Write your note…"
        placeholderTextColor={colors.textMuted}
        accessibilityLabel="Note text"
        multiline
        // autoFocus is respected by the focus trap (it skips moving focus when a
        // child already holds it), so the field gets focus on open.
        autoFocus
        style={[styles.input, { color: colors.text, backgroundColor: colors.background, borderColor: colors.border }]}
      />
    </AccessibleModal>
  );
}

const styles = StyleSheet.create({
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
