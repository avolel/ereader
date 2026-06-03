import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  createBookmark, deleteBookmark, listBookmarks, updateBookmark,
} from '../services/bookmarks';
import { Bookmark, CreateBookmarkInput, UpdateBookmarkInput } from '../types';

export function bookmarksKey(bookId: string) {
  return ['bookmarks', bookId] as const;
}

export function useBookmarks(bookId: string | undefined) {
  return useQuery({
    queryKey: bookmarksKey(bookId ?? ''),
    queryFn: () => listBookmarks(bookId!),
    enabled: !!bookId,
    staleTime: 30_000,
  });
}

export function useCreateBookmark(bookId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateBookmarkInput) => createBookmark(bookId, input),
    onSuccess: (created) => {
      qc.setQueryData<Bookmark[]>(bookmarksKey(bookId), (prev) =>
        prev ? [created, ...prev] : [created]);
    },
  });
}

export function useUpdateBookmark(bookId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateBookmarkInput }) =>
      updateBookmark(bookId, id, input),
    onSuccess: (updated) => {
      qc.setQueryData<Bookmark[]>(bookmarksKey(bookId), (prev) =>
        prev?.map((a) => (a.id === updated.id ? updated : a)) ?? [updated]);
    },
  });
}

export function useDeleteBookmark(bookId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteBookmark(bookId, id),
    onSuccess: (_void, id) => {
      qc.setQueryData<Bookmark[]>(bookmarksKey(bookId), (prev) =>
        prev?.filter((a) => a.id !== id) ?? []);
    },
  });
}