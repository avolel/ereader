import { useEffect, useRef } from 'react';
import { Platform, View } from 'react-native';

// Elements that can receive keyboard focus. Mirrors the WAI-ARIA dialog
// recommendation; the `:not([tabindex="-1"])` clause excludes nodes that are
// programmatically focusable but skipped by sequential Tab navigation.
const FOCUSABLE_SELECTOR =
  'a[href], button, input, textarea, select, [tabindex]:not([tabindex="-1"])';

// Web-only focus trap for modal panels (FR-accessibility). Keeps Tab/Shift+Tab
// inside the panel while it's open and returns focus to wherever the user was
// when it closes. On native this is a no-op — RN's <Modal> already manages
// focus — and the returned ref simply goes unused by the DOM logic.
//
// The ref is typed as React.RefObject<View> so callers attach it to a <View>,
// but under React Native Web that View resolves to a DOM node at runtime, which
// is what we read here. Hence the `as unknown as HTMLElement` bridge.
export function useFocusTrap(active: boolean): React.RefObject<View> {
  const panelRef = useRef<View>(null);

  useEffect(() => {
    if (Platform.OS !== 'web' || !active) return;

    const panel = panelRef.current as unknown as HTMLElement | null;
    if (!panel || typeof document === 'undefined') return;

    // Remember the trigger so we can hand focus back on close — but only if it's
    // OUTSIDE the panel. A child with autoFocus (e.g. NoteEditor's TextInput)
    // fires its focus in a mount effect before this parent effect runs, so by now
    // focus may already be inside the panel; recording that would later restore
    // focus to a node that's being unmounted.
    const active0 = document.activeElement as HTMLElement | null;
    const previouslyFocused = active0 && !panel.contains(active0) ? active0 : null;

    // Resolve focusables fresh each call — the panel's contents can change
    // (async-loaded sections, conditional buttons) between open and Tab.
    const getFocusable = (): HTMLElement[] =>
      Array.from(panel.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR));

    // Move focus into the dialog — unless something inside already has it (an
    // autoFocus child), in which case respect that initial focus. Otherwise
    // prefer the first focusable descendant, falling back to the panel itself.
    if (!panel.contains(document.activeElement)) {
      const focusables = getFocusable();
      if (focusables.length > 0) {
        focusables[0].focus();
      } else {
        if (!panel.hasAttribute('tabindex')) panel.setAttribute('tabindex', '-1');
        panel.focus();
      }
    }

    function onKeyDown(e: KeyboardEvent) {
      if (e.key !== 'Tab' || !panel) return;

      const items = getFocusable();
      if (items.length === 0) {
        // Nothing to cycle through — keep focus pinned to the panel.
        e.preventDefault();
        return;
      }

      const first = items[0];
      const last = items[items.length - 1];
      const current = document.activeElement;

      if (e.shiftKey) {
        // Wrap from the first element (or focus that escaped the panel) to the last.
        if (current === first || !panel.contains(current)) {
          e.preventDefault();
          last.focus();
        }
      } else if (current === last || !panel.contains(current)) {
        e.preventDefault();
        first.focus();
      }
    }

    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      // Restore focus to the trigger if it's still in the document. We don't gate
      // on "focus is still inside the panel" — on unmount React detaches the panel
      // before this cleanup runs, so that check would spuriously fail. The trap
      // itself keeps focus inside while open, so unconditional restore is safe.
      if (previouslyFocused && previouslyFocused.isConnected) {
        previouslyFocused.focus();
      }
    };
  }, [active]);

  // useRef gives RefObject<View | null> under current React types; the public
  // signature pins it to View since callers only ever attach it to a <View>.
  return panelRef as React.RefObject<View>;
}
