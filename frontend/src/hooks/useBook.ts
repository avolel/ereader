import { useQuery } from '@tanstack/react-query';

import { getBook } from '../services/books';

export function bookQueryKey(bookId: string) {
  return ['books', 'detail', bookId] as const;
}

export function useBook(bookId: string | undefined) {
  return useQuery({
    queryKey: bookQueryKey(bookId ?? ''),
    queryFn: () => getBook(bookId!),
    enabled: !!bookId,
  });
}