import { ReactNode, createContext, useContext, useMemo, useState } from 'react';

// Stub for now — Phase 3 fills in real theme settings (font, size, mode)
// backed by ReadingSetting. Putting the shell in place so screens can already
// consume `useTheme()` without churning every component later.

type ThemeMode = 'light' | 'dark';

type Theme = {
  mode: ThemeMode;
  colors: {
    background: string;
    surface: string;
    text: string;
    textMuted: string;
    border: string;
    accent: string;
    error: string;
  };
};

const lightTheme: Theme = {
  mode: 'light',
  colors: {
    background: '#ffffff',
    surface: '#f6f6f6',
    text: '#111111',
    textMuted: '#666666',
    border: '#e0e0e0',
    accent: '#2f6feb',
    error: '#c62828',
  },
};

type ThemeContextValue = {
  theme: Theme;
  setMode: (mode: ThemeMode) => void;
};

const ThemeContext = createContext<ThemeContextValue | null>(null);

type Props = { children: ReactNode };

export default function ThemeProvider({ children }: Props) {
  const [theme] = useState<Theme>(lightTheme);

  const value = useMemo<ThemeContextValue>(
    () => ({
      theme,
      setMode: () => {
        // No-op until Phase 3 wires real theming.
      },
    }),
    [theme],
  );

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme(): Theme {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error('useTheme must be used inside ThemeProvider.');
  return ctx.theme;
}