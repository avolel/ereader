import { useInfiniteQuery } from '@tanstack/react-query';

import { search } from '../services/search';
import { SearchResponse } from '../types';

export function searchQueryKey(query: string, bookId?: string) {
  return ['search', query, bookId ?? null] as const;
}

// Caller is responsible for debouncing — pass the already-debounced query string.
// Hook stays passive: if query is empty/too-short the query is disabled.
export function useSearch(query: string, bookId?: string) {
  const trimmed = query.trim();
  const enabled = trimmed.length >= 2;

  return useInfiniteQuery<SearchResponse>({
    queryKey: searchQueryKey(trimmed, bookId),
    queryFn: ({ pageParam }) =>
      search({ q: trimmed, bookId, cursor: pageParam as string | undefined }),
    initialPageParam: undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
    enabled,
  });
}