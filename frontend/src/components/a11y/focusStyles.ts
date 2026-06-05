import { Platform, ViewStyle } from 'react-native';

// Shared visible focus indicator (WCAG 2.4.7). Web-only: native platforms draw
// their own accessibility focus cursor, so this returns an empty style off web.
//
// `outline*` are React-Native-Web style keys that don't exist on RN's ViewStyle
// type, so the object is cast through `unknown`. Keep the cast confined here so
// call sites (IconButton et al.) receive a clean ViewStyle.
//
// Note on focus-visible: CSS `outline` paints whenever it's set, so callers must
// apply this only while the control is actually focused (e.g. gate it on an
// onFocus/onBlur flag) — applying it unconditionally would draw a permanent
// ring. We can't express the `:focus-visible` pseudo-class from inline RNW
// styles, so the ring shows on any focus, including pointer focus.
export function focusRing(accent: string): ViewStyle {
  if (Platform.OS !== 'web') return {};
  return {
    outlineColor: accent,
    outlineStyle: 'solid',
    outlineWidth: 2,
    outlineOffset: 2,
  } as unknown as ViewStyle;
}
