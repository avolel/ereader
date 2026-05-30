import { Slot } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import AuthProvider from '../src/providers/AuthProvider';
import QueryProvider from '../src/providers/QueryProvider';
import ThemeProvider from '../src/providers/ThemeProvider';

// Provider order matters: AuthProvider uses useQueryClient (so it has to be
// inside QueryProvider) and emits theme-agnostic auth events, so ThemeProvider
// can sit at either layer — placed innermost for visual symmetry.
export default function RootLayout() {
  return (
    <SafeAreaProvider>
      <QueryProvider>
        <AuthProvider>
          <ThemeProvider>
            <Slot />
            <StatusBar style="auto" />
          </ThemeProvider>
        </AuthProvider>
      </QueryProvider>
    </SafeAreaProvider>
  );
}