import { useInfiniteQuery } from '@tanstack/react-query';

import { listBooks } from '../services/books';
import { BookListParams, BookListResponse } from '../types';

// Query key is shaped as [domain, 'list', params] so invalidating ['books']
// hits every list variant (different sorts/filters) at once after mutations.
export function booksQueryKey(params: BookListParams) {
  return ['books', 'list', params] as const;
}

export function useBooks(params: BookListParams = {}) {
  return useInfiniteQuery<BookListResponse>({
    queryKey: booksQueryKey(params),
    queryFn: ({ pageParam }) =>
      listBooks({ ...params, cursor: pageParam as string | undefined }),
    initialPageParam: undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
}