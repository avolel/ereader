import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactNode, useState } from 'react';

type Props = { children: ReactNode };

export default function QueryProvider({ children }: Props) {
  // useState ensures a single QueryClient survives re-renders without being
  // recreated; we don't need a module-level singleton because React Native /
  // Expo runs one app instance per process.
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            // Reasonable defaults for a single-user reader. Tune per-query when needed.
            staleTime: 30_000,
            retry: 1,
            refetchOnWindowFocus: false,
          },
          mutations: {
            retry: 0,
          },
        },
      }),
  );

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}