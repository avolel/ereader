import { StyleSheet, Text, View } from 'react-native';

import AccessibleModal from './a11y/AccessibleModal';
import IconButton from './a11y/IconButton';
import { useTheme } from '../providers/ThemeProvider';

type Props = {
  visible: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
};

// Stable id linking the message text to the dialog via aria-describedby.
const MESSAGE_ID = 'confirm-dialog-message';

export default function ConfirmDialog({
  visible,
  title,
  message,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  destructive = false,
  busy = false,
  onConfirm,
  onCancel,
}: Props) {
  const theme = useTheme();
  return (
    <AccessibleModal
      visible={visible}
      onClose={busy ? () => {} : onCancel}
      label={title}
      role="alertdialog"
      describedById={MESSAGE_ID}
      align="center"
      panelStyle={[styles.card, { backgroundColor: theme.colors.surface }]}
    >
      <Text style={[styles.title, { color: theme.colors.text }]} accessibilityRole="header">
        {title}
      </Text>
      <Text nativeID={MESSAGE_ID} style={[styles.message, { color: theme.colors.textMuted }]}>
        {message}
      </Text>
      <View style={styles.actions}>
        {/* Cancel is first in DOM order so the focus trap lands here by default —
            the safer initial focus for a destructive dialog. */}
        <IconButton label={cancelLabel} onPress={onCancel} disabled={busy} style={styles.btn}>
          <Text style={{ color: theme.colors.text }}>{cancelLabel}</Text>
        </IconButton>
        <IconButton label={confirmLabel} onPress={onConfirm} disabled={busy} busy={busy} style={styles.btn}>
          <Text
            style={{
              color: destructive ? theme.colors.error : theme.colors.accent,
              fontWeight: '600',
            }}
          >
            {busy ? '…' : confirmLabel}
          </Text>
        </IconButton>
      </View>
    </AccessibleModal>
  );
}

const styles = StyleSheet.create({
  card: { width: '100%', maxWidth: 380, borderRadius: 10, padding: 20, gap: 10 },
  title: { fontSize: 17, fontWeight: '700' },
  message: { fontSize: 14, lineHeight: 20 },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: 8, marginTop: 12 },
  btn: { paddingHorizontal: 14, paddingVertical: 8 },
});
