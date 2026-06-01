import { useMutation, useQueryClient } from '@tanstack/react-query';
import { deleteBook } from '../services/books';

export function useDeleteBook() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, string>({
    mutationFn: (bookId) => deleteBook(bookId),
    onSuccess: () => {
      // Invalidate every books list variant (sorts/filters) so the deleted book
      // disappears regardless of which list is mounted — same as useUploadBook.
      void queryClient.invalidateQueries({ queryKey: ['books'] });
    },
  });
}