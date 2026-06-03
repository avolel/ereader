import {
  Bookmark, BookmarkListResponse,
  CreateBookmarkInput, UpdateBookmarkInput,
} from '../types';
import { api } from './api';

export async function listBookmarks(bookId: string): Promise<Bookmark[]> {
  const items: Bookmark[] = [];
  let cursor: string | null = null;
  do {
    const { data }: { data: BookmarkListResponse } = await api.get<BookmarkListResponse>(
      `/api/v1/books/${bookId}/bookmarks`,
      { params: { pageSize: 200, cursor: cursor ?? undefined } },
    );
    items.push(...data.items);
    cursor = data.nextCursor;
  } while (cursor);
  return items;
}

export async function createBookmark(bookId: string, input: CreateBookmarkInput): Promise<Bookmark> {
  const { data } = await api.post<Bookmark>(`/api/v1/books/${bookId}/bookmarks`, input);
  return data;
}

export async function updateBookmark(
  bookId: string, id: string, input: UpdateBookmarkInput,
): Promise<Bookmark> {
  const { data } = await api.patch<Bookmark>(`/api/v1/books/${bookId}/bookmarks/${id}`, input);
  return data;
}

export async function deleteBookmark(bookId: string, id: string): Promise<void> {
  await api.delete(`/api/v1/books/${bookId}/bookmarks/${id}`);
}