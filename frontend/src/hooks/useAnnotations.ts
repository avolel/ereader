import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  createAnnotation, deleteAnnotation, listAnnotations, updateAnnotation,
} from '../services/annotations';
import { Annotation, CreateAnnotationInput, UpdateAnnotationInput } from '../types';

export function annotationsKey(bookId: string) {
  return ['annotations', bookId] as const;
}

export function useAnnotations(bookId: string | undefined) {
  return useQuery({
    queryKey: annotationsKey(bookId ?? ''),
    queryFn: () => listAnnotations(bookId!),
    enabled: !!bookId,
    staleTime: 30_000,
  });
}

export function useCreateAnnotation(bookId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateAnnotationInput) => createAnnotation(bookId, input),
    onSuccess: (created) => {
      qc.setQueryData<Annotation[]>(annotationsKey(bookId), (prev) =>
        prev ? [created, ...prev] : [created]);
    },
  });
}

export function useUpdateAnnotation(bookId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateAnnotationInput }) =>
      updateAnnotation(bookId, id, input),
    onSuccess: (updated) => {
      qc.setQueryData<Annotation[]>(annotationsKey(bookId), (prev) =>
        prev?.map((a) => (a.id === updated.id ? updated : a)) ?? [updated]);
    },
  });
}

export function useDeleteAnnotation(bookId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteAnnotation(bookId, id),
    onSuccess: (_void, id) => {
      qc.setQueryData<Annotation[]>(annotationsKey(bookId), (prev) =>
        prev?.filter((a) => a.id !== id) ?? []);
    },
  });
}