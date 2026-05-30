// Two palette presets keyed by ThemeMode. "system" mode resolves to one of
// these via the OS color scheme — done in ThemeProvider, not here.
//
// Chrome (library/auth/etc.) consumes `colors` directly; the reader's WebView
// gets the `webview` subset injected as CSS variables so book chapters can be
// re-skinned without rebuilding the chrome theme on every change.

export type ThemeColors = {
  background: string;
  surface: string;
  text: string;
  textMuted: string;
  border: string;
  accent: string;
  error: string;
  // Subset injected into chapter HTML.
  webview: {
    background: string;
    foreground: string;
    link: string;
    selection: string;
  };
};

export const lightColors: ThemeColors = {
  background: '#ffffff',
  surface: '#f6f6f6',
  text: '#111111',
  textMuted: '#666666',
  border: '#e0e0e0',
  accent: '#2f6feb',
  error: '#c62828',
  webview: {
    background: '#fdfdfb',
    foreground: '#1a1a1a',
    link: '#2f6feb',
    selection: 'rgba(47, 111, 235, 0.25)',
  },
};

export const darkColors: ThemeColors = {
  background: '#121212',
  surface: '#1e1e1e',
  text: '#f0f0f0',
  textMuted: '#9a9a9a',
  border: '#2a2a2a',
  accent: '#74a0ff',
  error: '#ef6a6a',
  webview: {
    background: '#181818',
    foreground: '#e8e6e1',
    link: '#74a0ff',
    selection: 'rgba(116, 160, 255, 0.3)',
  },
};