import React from 'react';
import { renderHook, waitFor } from '@testing-library/react-native';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { useCreateAnnotation, annotationsKey } from '../useAnnotations';
import * as service from '../../services/annotations';
import { Annotation } from '../../types';

jest.mock('../../services/annotations');

const BOOK_ID = 'book-1';

function makeAnnotation(overrides: Partial<Annotation> = {}): Annotation {
  return {
    id: 'a-new',
    bookId: BOOK_ID,
    chapterId: 'ch-1',
    type: 'highlight',
    colour: 'yellow',
    textAnchor: '{}',
    selectedText: 'hello',
    noteBody: null,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

function makeWrapper(qc: QueryClient) {
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
  };
}

describe('useCreateAnnotation', () => {
  it('Should_PrependAnnotation_When_CreateSucceeds', async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const existing = makeAnnotation({ id: 'a-existing', selectedText: 'old' });
    qc.setQueryData<Annotation[]>(annotationsKey(BOOK_ID), [existing]);

    const created = makeAnnotation({ id: 'a-new', selectedText: 'new' });
    (service.createAnnotation as jest.Mock).mockResolvedValue(created);

    const { result } = renderHook(() => useCreateAnnotation(BOOK_ID), {
      wrapper: makeWrapper(qc),
    });

    result.current.mutate({
      type: 'highlight',
      chapterId: 'ch-1',
      colour: 'yellow',
      textAnchor: '{}',
      selectedText: 'new',
      noteBody: null,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Optimistic cache update prepends the created annotation, keeping the old one.
    expect(qc.getQueryData<Annotation[]>(annotationsKey(BOOK_ID))).toEqual([created, existing]);
  });
});
