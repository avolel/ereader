import { BookDetail, BookListParams, BookListResponse, ChapterDetail } from '../types';
import { api } from './api';

export async function listBooks(
  params: BookListParams & { cursor?: string } = {},
): Promise<BookListResponse> {
  const { data } = await api.get<BookListResponse>('/api/v1/books', { params });
  return data;
}

export async function getBook(bookId: string): Promise<BookDetail> {
  const { data } = await api.get<BookDetail>(`/api/v1/books/${bookId}`);
  return data;
}

export async function getChapter(
  bookId: string,
  chapterId: string,
): Promise<ChapterDetail> {
  const { data } = await api.get<ChapterDetail>(
    `/api/v1/books/${bookId}/chapters/${chapterId}`,
  );
  return data;
}

export type UploadInput =
  | { kind: 'web'; file: File }
  | { kind: 'native'; uri: string; name: string; mimeType: string };

export async function uploadBook(input: UploadInput): Promise<BookDetail> {
  const form = new FormData();
  if (input.kind === 'web') {
    form.append('file', input.file, input.file.name);
  } else {
    // RN's FormData accepts the { uri, name, type } object shape; TS doesn't
    // know about that overload so the cast is necessary.
    form.append('file', {
      uri: input.uri,
      name: input.name,
      type: input.mimeType,
    } as unknown as Blob);
  }
  const { data } = await api.post<BookDetail>('/api/v1/books', form);
  return data;
}

export async function deleteBook(bookId: string): Promise<void> {
  await api.delete(`/api/v1/books/${bookId}`);
}

// Cover URLs come back as relative paths (e.g. /api/v1/books/{id}/cover).
// The img tag on web needs an absolute URL, and the Authorization header isn't
// sent on plain <img src=...> requests — so for now we ask the backend to
// gate covers by the same userId and rely on the *path* not being guessable.
// TODO(phase-3): switch to signed cover URLs or fetch as a blob with auth.
const BASE_URL = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5000';

export function absoluteCoverUrl(coverUrl: string | null | undefined): string | null {
  if (!coverUrl) return null;
  return coverUrl.startsWith('http') ? coverUrl : `${BASE_URL}${coverUrl}`;
}