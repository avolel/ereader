import React from 'react';
import { renderHook, waitFor } from '@testing-library/react-native';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { useDictionary, useWikipedia } from '../useLookup';
import * as service from '../../services/lookup';
import { DictionaryResult, WikipediaResult } from '../../types';

jest.mock('../../services/lookup');

function makeWrapper(qc: QueryClient) {
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
  };
}

function newClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } });
}

afterEach(() => {
  jest.clearAllMocks();
});

describe('useDictionary', () => {
  it('Should_NotFetch_When_WordIsNull', () => {
    const qc = newClient();
    renderHook(() => useDictionary(null), { wrapper: makeWrapper(qc) });
    expect(service.defineWord).not.toHaveBeenCalled();
  });

  it('Should_CallDefineEndpoint_When_WordProvided', async () => {
    const qc = newClient();
    const result: DictionaryResult = { word: 'read', found: true, senses: [] };
    (service.defineWord as jest.Mock).mockResolvedValue(result);

    const { result: hook } = renderHook(() => useDictionary('read'), {
      wrapper: makeWrapper(qc),
    });

    await waitFor(() => expect(hook.current.isSuccess).toBe(true));
    expect(service.defineWord).toHaveBeenCalledWith('read');
    expect(hook.current.data).toEqual(result);
  });
});

describe('useWikipedia', () => {
  it('Should_NotFetch_When_TermIsNull', () => {
    const qc = newClient();
    renderHook(() => useWikipedia(null), { wrapper: makeWrapper(qc) });
    expect(service.getWikipediaSummary).not.toHaveBeenCalled();
  });

  it('Should_CallWikipediaEndpoint_When_TermProvided', async () => {
    const qc = newClient();
    const result: WikipediaResult = {
      term: 'Project Gutenberg',
      found: true,
      title: 'Project Gutenberg',
      extract: 'A volunteer effort.',
      pageUrl: 'https://en.wikipedia.org/wiki/Project_Gutenberg',
      thumbnailUrl: null,
    };
    (service.getWikipediaSummary as jest.Mock).mockResolvedValue(result);

    const { result: hook } = renderHook(() => useWikipedia('Project Gutenberg'), {
      wrapper: makeWrapper(qc),
    });

    await waitFor(() => expect(hook.current.isSuccess).toBe(true));
    expect(service.getWikipediaSummary).toHaveBeenCalledWith('Project Gutenberg');
    expect(hook.current.data).toEqual(result);
  });
});
