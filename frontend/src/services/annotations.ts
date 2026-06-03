import {
  Annotation, AnnotationListResponse,
  CreateAnnotationInput, UpdateAnnotationInput,
} from '../types';
import { api } from './api';

export async function listAnnotations(bookId: string): Promise<Annotation[]> {
  // Per-book annotation counts are small; fetch the full set with the max page size and
  // follow nextCursor until exhausted so the reader always has every highlight to render.
  const items: Annotation[] = [];
  let cursor: string | null = null;
  do {
    const { data }: { data: AnnotationListResponse } = await api.get<AnnotationListResponse>(
      `/api/v1/books/${bookId}/annotations`,
      { params: { pageSize: 200, cursor: cursor ?? undefined } },
    );
    items.push(...data.items);
    cursor = data.nextCursor;
  } while (cursor);
  return items;
}

export async function createAnnotation(bookId: string, input: CreateAnnotationInput): Promise<Annotation> {
  const { data } = await api.post<Annotation>(`/api/v1/books/${bookId}/annotations`, input);
  return data;
}

export async function updateAnnotation(
  bookId: string, id: string, input: UpdateAnnotationInput,
): Promise<Annotation> {
  const { data } = await api.patch<Annotation>(`/api/v1/books/${bookId}/annotations/${id}`, input);
  return data;
}

export async function deleteAnnotation(bookId: string, id: string): Promise<void> {
  await api.delete(`/api/v1/books/${bookId}/annotations/${id}`);
}