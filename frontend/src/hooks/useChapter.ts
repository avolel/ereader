import { useQuery } from '@tanstack/react-query';

import { getChapter } from '../services/books';

export function chapterQueryKey(bookId: string, chapterId: string) {
  return ['books', 'chapter', bookId, chapterId] as const;
}

export function useChapter(bookId: string | undefined, chapterId: string | undefined) {
  return useQuery({
    queryKey: chapterQueryKey(bookId ?? '', chapterId ?? ''),
    queryFn: () => getChapter(bookId!, chapterId!),
    enabled: !!bookId && !!chapterId,
    // Chapter content is immutable once ingested, so stale data is fine.
    staleTime: Infinity,
  });
}