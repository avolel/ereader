import { useMutation, useQueryClient } from '@tanstack/react-query';

import { UploadInput, uploadBook } from '../services/books';
import { BookDetail } from '../types';

export function useUploadBook() {
  const queryClient = useQueryClient();
  return useMutation<BookDetail, Error, UploadInput>({
    mutationFn: uploadBook,
    onSuccess: () => {
      // Invalidate every books list variant — different sort/filter combos
      // would otherwise miss the newly added book.
      void queryClient.invalidateQueries({ queryKey: ['books'] });
    },
  });
}