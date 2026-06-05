import { ReactNode, useState } from 'react';
import {
  AccessibilityRole,
  GestureResponderEvent,
  Pressable,
  StyleProp,
  ViewStyle,
} from 'react-native';

import { useTheme } from '../../providers/ThemeProvider';
import { focusRing } from './focusStyles';

type Props = {
  // Required accessible name — the visible child is often a glyph with no text,
  // so the label is the only thing a screen reader has to announce.
  label: string;
  onPress?: (e: GestureResponderEvent) => void;
  onLongPress?: (e: GestureResponderEvent) => void;
  // Supplementary description (maps to accessibilityHint / web aria-describedby).
  accessibilityHint?: string;
  disabled?: boolean;
  // Toggle/segmented controls pass `selected`; surfaced via accessibilityState.
  selected?: boolean;
  // In-flight actions pass `busy` (e.g. a saving button); surfaced too.
  busy?: boolean;
  // Override the default "button" role for menuitem/radio/etc. in lists & groups.
  accessibilityRole?: AccessibilityRole;
  // Optional — some buttons are pure colour swatches/glyph-via-style with no child.
  children?: ReactNode;
  style?: StyleProp<ViewStyle>;
  hitSlop?: number;
};

// Standard pressable for icon/glyph or text actions. Centralizes the button
// accessibility contract (role, label, state, focusability) and the web focus
// ring so individual call sites stop re-deriving it. Replaces bare <Pressable>
// buttons across the chrome.
export default function IconButton({
  label,
  onPress,
  onLongPress,
  accessibilityHint,
  disabled = false,
  selected,
  busy,
  accessibilityRole = 'button',
  children,
  style,
  hitSlop = 8,
}: Props) {
  const { colors } = useTheme();
  // Track focus so the ring only paints while focused — see focusStyles for why
  // this can't be left to a CSS pseudo-class.
  const [focused, setFocused] = useState(false);

  return (
    <Pressable
      onPress={onPress}
      onLongPress={onLongPress}
      disabled={disabled}
      hitSlop={hitSlop}
      focusable={!disabled}
      accessibilityRole={accessibilityRole}
      accessibilityLabel={label}
      accessibilityHint={accessibilityHint}
      accessibilityState={{ disabled, selected, busy }}
      onFocus={() => setFocused(true)}
      onBlur={() => setFocused(false)}
      style={[style, focused && !disabled ? focusRing(colors.accent) : null]}
    >
      {children}
    </Pressable>
  );
}
