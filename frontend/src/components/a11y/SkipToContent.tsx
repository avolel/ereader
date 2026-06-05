import { useState } from 'react';
import { Platform, Pressable, StyleSheet, Text } from 'react-native';

import { useTheme } from '../../providers/ThemeProvider';

const MAIN_CONTENT_ID = 'main-content';

// Web-only "skip to content" link (WCAG 2.4.1). Rendered as the first focusable
// element in the app so keyboard users can jump past the chrome straight to the
// screen's main region. It's visually hidden until focused, then slides into the
// top-left corner. Native has no equivalent affordance, so this renders nothing.
export default function SkipToContent() {
  const { colors } = useTheme();
  const [focused, setFocused] = useState(false);

  if (Platform.OS !== 'web') return null;

  return (
    <Pressable
      // Move focus to whichever screen tagged itself nativeID="main-content".
      onPress={() => {
        if (typeof document === 'undefined') return;
        const main = document.getElementById(MAIN_CONTENT_ID);
        if (main) {
          // Ensure the target can receive focus even if it's a plain container.
          if (!main.hasAttribute('tabindex')) main.setAttribute('tabindex', '-1');
          main.focus();
        }
      }}
      onFocus={() => setFocused(true)}
      onBlur={() => setFocused(false)}
      accessibilityRole="link"
      accessibilityLabel="Skip to main content"
      style={[
        styles.base,
        focused
          ? { backgroundColor: colors.surface, borderColor: colors.accent }
          : styles.hidden,
      ]}
    >
      <Text style={{ color: colors.accent, fontWeight: '600' }}>Skip to content</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: {
    position: 'absolute',
    top: 8,
    left: 8,
    zIndex: 1000,
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: 6,
    borderWidth: 1,
  },
  // Clipped to a 1px box off-focus so it stays in the tab order but invisible.
  hidden: {
    width: 1,
    height: 1,
    padding: 0,
    overflow: 'hidden',
    borderWidth: 0,
    top: -9999,
    left: -9999,
  },
});
