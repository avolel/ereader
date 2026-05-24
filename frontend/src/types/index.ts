// Shared types used across more than one component or screen.
// Co-located types that are only used in one file belong in that file.

export type Book = {
  id: string;
  title: string;
  author: string;
  coverUrl?: string;
  filePath: string;
  addedAt: string;
};

export type ReadingPosition = {
  bookId: string;
  chapterIndex: number;
  scrollOffset: number;
  updatedAt: string;
};

export type ApiError = {
  error: {
    code: string;
    message: string;
    details?: Record<string, unknown>;
  };
};