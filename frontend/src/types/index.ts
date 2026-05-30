// Shared types used across more than one component or screen.
// Co-located types that are only used in one file belong in that file.
// Field names mirror the backend DTOs (camelCase via ASP.NET defaults).

export type UserProfile = {
  id: string;
  username: string;
  createdAt: string;
  lastLoginAt: string | null;
};

export type AuthTokenResponse = {
  accessToken: string;
  refreshToken: string;
  accessExpiresAt: string;
  refreshExpiresAt: string;
  user: UserProfile;
};

export type BookSummary = {
  id: string;
  title: string;
  author: string;
  language: string | null;
  coverUrl: string | null;
  importedAt: string;
};

export type TocEntry = {
  chapterId: string;
  title: string;
  spineOrder: number;
};

export type BookDetail = {
  id: string;
  title: string;
  author: string;
  language: string | null;
  publisher: string | null;
  publishedDate: string | null;
  publishedYear: number | null;
  description: string | null;
  coverUrl: string | null;
  importedAt: string;
  toc: TocEntry[];
};

export type BookListResponse = {
  items: BookSummary[];
  nextCursor: string | null;
};

export type BookSortKey = 'importedAt' | 'title' | 'author';
export type SortDirection = 'asc' | 'desc';

export type BookListParams = {
  sort?: BookSortKey;
  dir?: SortDirection;
  author?: string;
  language?: string;
  pageSize?: number;
};

export type ApiError = {
  error: {
    code: string;
    message: string;
    details?: Record<string, unknown>;
  };
};

export type ChapterDetail = {
  id: string;
  bookId: string;
  spineOrder: number;
  title: string | null;
  // Rewritten HTML with asset hrefs pointing at /api/v1/books/{id}/assets/...
  content: string;
  previousChapterId: string | null;
  nextChapterId: string | null;
};

export type SearchHit = {
  bookId: string;
  bookTitle: string;
  bookAuthor: string;
  chapterId: string;
  chapterTitle: string | null;
  chapterSpineOrder: number;
  // HTML with <mark> wrapping matched terms — must be sanitized at render time.
  snippet: string;
};

export type SearchResponse = {
  items: SearchHit[];
  nextCursor: string | null;
};

export type ThemeMode = 'light' | 'dark' | 'system';

export type ReadingSetting = {
  // null = global default row; non-null = per-book override
  bookId: string | null;
  theme: ThemeMode;
  fontFamily: string;
  fontSize: number;
  lineSpacing: number;
  marginHorizontal: number;
  marginVertical: number;
  lastChapterId: string | null;
  lastScrollOffset: number;
  lastReadAt: string | null;
  updatedAt: string;
};

// Partial: omit fields you don't want to change. Mirrors backend PATCH semantics.
export type ReadingSettingUpdate = {
  theme?: ThemeMode;
  fontFamily?: string;
  fontSize?: number;
  lineSpacing?: number;
  marginHorizontal?: number;
  marginVertical?: number;
};

export type ReadingPositionUpdate = {
  chapterId: string;
  scrollOffset: number;
};