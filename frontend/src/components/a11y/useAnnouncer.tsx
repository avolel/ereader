import { ReactNode, createContext, useCallback, useContext, useState } from 'react';
import { Platform, StyleSheet, Text } from 'react-native';

type AnnouncerContextValue = {
  // Push a transient message to assistive tech. `assertive` interrupts the
  // current speech (errors); the default polite queue waits for a pause
  // (chapter changes, "Highlight added", lookup results).
  announce: (message: string, assertive?: boolean) => void;
};

const AnnouncerContext = createContext<AnnouncerContextValue | null>(null);

// App-level live region. Mounted once (in app/_layout.tsx, inside ThemeProvider)
// so any screen can fire announcements without rendering its own region. Two
// fixed nodes — polite and assertive — are kept visually hidden but present in
// the accessibility tree.
export function AnnouncerProvider({ children }: { children: ReactNode }) {
  const [polite, setPolite] = useState('');
  const [assertive, setAssertive] = useState('');

  const announce = useCallback((message: string, assertive = false) => {
    const setter = assertive ? setAssertive : setPolite;
    // Clear first, then set on the next frame. Re-announcing the same string
    // (e.g. two identical errors) wouldn't change the node's text and screen
    // readers would stay silent; the empty-then-fill cycle forces a re-read.
    setter('');
    requestAnimationFrame(() => setter(message));
  }, []);

  return (
    <AnnouncerContext.Provider value={{ announce }}>
      {children}
      <Text
        style={styles.visuallyHidden}
        accessibilityLiveRegion="polite"
        {...(Platform.OS === 'web' ? ({ 'aria-live': 'polite' } as const) : {})}
      >
        {polite}
      </Text>
      <Text
        style={styles.visuallyHidden}
        accessibilityLiveRegion="assertive"
        {...(Platform.OS === 'web' ? ({ 'aria-live': 'assertive' } as const) : {})}
      >
        {assertive}
      </Text>
    </AnnouncerContext.Provider>
  );
}

export function useAnnouncer(): AnnouncerContextValue {
  const ctx = useContext(AnnouncerContext);
  if (!ctx) throw new Error('useAnnouncer must be used inside AnnouncerProvider.');
  return ctx;
}

const styles = StyleSheet.create({
  // Present for screen readers, invisible on screen. Kept to a 1px clipped box
  // rather than display:none, which would drop it from the accessibility tree.
  visuallyHidden: {
    position: 'absolute',
    width: 1,
    height: 1,
    overflow: 'hidden',
  },
});
