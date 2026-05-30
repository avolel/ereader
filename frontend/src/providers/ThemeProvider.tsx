import { ReactNode, createContext, useContext, useEffect, useMemo, useState } from 'react';
import { Appearance, ColorSchemeName } from 'react-native';

import { useGlobalSettings, useUpsertGlobalSettings } from '../hooks/useReadingSettings';
import { ThemeColors, darkColors, lightColors } from '../theme/tokens';
import { ReadingSetting, ReadingSettingUpdate, ThemeMode } from '../types';
import { useAuth } from './AuthProvider';

// "system" follows the OS; "light"/"dark" are absolute. resolveMode collapses
// system to a concrete palette using the current Appearance.
type ResolvedTheme = {
  mode: ThemeMode;        // What the user picked (incl. "system")
  resolvedMode: 'light' | 'dark';  // What we actually paint
  colors: ThemeColors;
};

// Defaults match ReadingSetting model defaults on the backend so the chrome
// can render before the API call completes. Once `globalSettings` arrives,
// the real values flow through.
const DEFAULT_SETTING: ReadingSetting = {
  bookId: null,
  theme: 'light',
  fontFamily: 'serif',
  fontSize: 16,
  lineSpacing: 1.5,
  marginHorizontal: 40,
  marginVertical: 20,
  lastChapterId: null,
  lastScrollOffset: 0,
  lastReadAt: null,
  updatedAt: new Date().toISOString(),
};

type ThemeContextValue = {
  // Convenience: the actually-rendered colors. Most chrome reads this.
  theme: ResolvedTheme;
  // Full global settings (typography + theme). Reader screen reads this to
  // build the WebView style block.
  globalSetting: ReadingSetting;
  // Patch global settings. Optimistic via React Query.
  updateGlobal: (update: ReadingSettingUpdate) => Promise<void>;
};

const ThemeContext = createContext<ThemeContextValue | null>(null);

type Props = { children: ReactNode };

export default function ThemeProvider({ children }: Props) {
  const { status } = useAuth();
  // Only fetch once authed — otherwise the 401 path would spin up the refresh
  // interceptor unnecessarily during the login flow.
  const settingsQuery = useGlobalSettings(status === 'authed');
  const upsertGlobal = useUpsertGlobalSettings();

  const globalSetting = settingsQuery.data ?? DEFAULT_SETTING;

  const [osScheme, setOsScheme] = useState<ColorSchemeName>(
    () => Appearance.getColorScheme() ?? 'light',
  );
  useEffect(() => {
    const sub = Appearance.addChangeListener(({ colorScheme }) =>
      setOsScheme(colorScheme ?? 'light'),
    );
    return () => sub.remove();
  }, []);

  const theme = useMemo<ResolvedTheme>(() => {
    const mode = globalSetting.theme;
    const resolvedMode: 'light' | 'dark' =
      mode === 'system' ? (osScheme === 'dark' ? 'dark' : 'light') : mode;
    return {
      mode,
      resolvedMode,
      colors: resolvedMode === 'dark' ? darkColors : lightColors,
    };
  }, [globalSetting.theme, osScheme]);

  const value = useMemo<ThemeContextValue>(
    () => ({
      theme,
      globalSetting,
      updateGlobal: async (update) => {
        await upsertGlobal.mutateAsync(update);
      },
    }),
    [theme, globalSetting, upsertGlobal],
  );

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

// Most callers only need colors — this is the back-compat path that
// preserves the existing call sites (LibraryScreen, Login, etc.).
export function useTheme() {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error('useTheme must be used inside ThemeProvider.');
  return { mode: ctx.theme.mode, resolvedMode: ctx.theme.resolvedMode, colors: ctx.theme.colors };
}

// Full context for screens that need typography + the update function.
export function useThemeContext() {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error('useThemeContext must be used inside ThemeProvider.');
  return ctx;
}