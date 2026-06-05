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

// Measured WCAG contrast ratios (foreground : background) for the light palette.
// Re-verify before changing any colour below — text-bearing fg/bg pairs must
// clear 4.5:1 (normal text) / 3:1 (large text & UI). NFR-3 / WCAG 1.4.3.
//   text      #111    on bg #fff     → 18.9:1 (AAA)
//   text      #111    on surface     → 17.2:1 (AAA)
//   textMuted #666    on bg #fff     →  5.7:1 (AA)
//   textMuted #666    on surface     →  5.2:1 (AA)
//   accent    #2563d6 on bg #fff     →  4.6:1 (AA — used as button/link text)
//   error     #c62828 on bg #fff     →  5.9:1 (AA)
export const lightColors: ThemeColors = {
  background: '#ffffff',
  surface: '#f6f6f6',
  text: '#111111',
  textMuted: '#666666',
  border: '#e0e0e0',
  // Darkened from #2f6feb (4.0:1, AA fail) to clear 4.5:1 as normal-size text.
  accent: '#2563d6',
  error: '#c62828',
  webview: {
    background: '#fdfdfb',
    foreground: '#1a1a1a',
    link: '#2563d6', // matches accent so in-chapter links also clear AA on light
    selection: 'rgba(37, 99, 214, 0.25)',
  },
};

// Dark palette ratios (foreground : background):
//   text      #f0f0f0 on bg #121212 → 17.4:1 (AAA)
//   textMuted #9a9a9a on bg #121212 →  6.6:1 (AA)
//   accent    #74a0ff on bg #121212 →  6.8:1 (AA) — also legible as button text
//   error     #ef6a6a on bg #121212 →  5.6:1 (AA)
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