import { ReactNode } from 'react';
import { Modal, Platform, Pressable, StyleProp, StyleSheet, ViewStyle } from 'react-native';

import { useEscToClose } from './useEscToClose';
import { useFocusTrap } from './useFocusTrap';

type Align = 'center' | 'bottom' | 'custom';

type Props = {
  visible: boolean;
  onClose: () => void;
  // Accessible name for the dialog (announced when focus enters it).
  label: string;
  animationType?: 'none' | 'slide' | 'fade';
  // Where the panel sits over the backdrop. 'custom' leaves positioning to
  // panelStyle (used by anchored popovers like SelectionMenu).
  align?: Align;
  panelStyle?: StyleProp<ViewStyle>;
  // Destructive/confirmation dialogs pass 'alertdialog' for a stronger AT cue.
  role?: 'dialog' | 'alertdialog';
  // DOM id of the element describing the dialog (wired to web aria-describedby).
  describedById?: string;
  // Anchored popovers (SelectionMenu/AnnotationPopover) opt out of the dimming
  // backdrop so they don't darken the reader behind them.
  dimmed?: boolean;
  children: ReactNode;
};

// Wrapper around RN <Modal> standardizing overlay accessibility. Replaces the
// hand-rolled "Modal + backdrop Pressable + inner Pressable" pattern repeated
// across the eight overlays: it traps focus, wires Esc-to-close, marks itself as
// a modal dialog for assistive tech, and routes Android back to onClose.
export default function AccessibleModal({
  visible,
  onClose,
  label,
  animationType = 'fade',
  align = 'center',
  panelStyle,
  role = 'dialog',
  describedById,
  dimmed = true,
  children,
}: Props) {
  const panelRef = useFocusTrap(visible);
  useEscToClose(onClose, visible);

  // ARIA dialog semantics are a web concern; on native, accessibilityViewIsModal
  // already scopes the accessibility tree to the panel, so adding role/aria-modal
  // there would be redundant. Hence the conditional spread.
  const webDialogProps =
    Platform.OS === 'web'
      ? {
          role,
          'aria-modal': true,
          'aria-label': label,
          ...(describedById ? { 'aria-describedby': describedById } : {}),
        }
      : {};

  return (
    <Modal visible={visible} transparent animationType={animationType} onRequestClose={onClose}>
      <Pressable
        style={[styles.backdrop, dimmed ? styles.dim : null, alignStyles[align]]}
        onPress={onClose}
      >
        {/* Inner Pressable swallows presses so taps on the panel don't dismiss. */}
        <Pressable
          ref={panelRef}
          onPress={() => {}}
          style={panelStyle}
          accessibilityViewIsModal
          accessibilityRole="none"
          {...webDialogProps}
        >
          {children}
        </Pressable>
      </Pressable>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1 },
  dim: { backgroundColor: 'rgba(0,0,0,0.4)' },
});

// Applied on top of the backdrop to place the panel. 'custom' is intentionally
// empty — the caller positions the panel absolutely via panelStyle.
const alignStyles: Record<Align, ViewStyle> = {
  center: { justifyContent: 'center', alignItems: 'center' },
  bottom: { justifyContent: 'flex-end' },
  custom: {},
};
