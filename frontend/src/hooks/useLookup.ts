import { useQuery } from '@tanstack/react-query';
import { defineWord, getWikipediaSummary } from '../services/lookup';

export function useDictionary(word: string | null) {
  return useQuery({
    queryKey: ['lookup', 'define', word],
    queryFn: () => defineWord(word!),
    enabled: !!word,
    staleTime: 5 * 60_000,
  });
}

export function useWikipedia(term: string | null) {
  return useQuery({
    queryKey: ['lookup', 'wikipedia', term],
    queryFn: () => getWikipediaSummary(term!),
    enabled: !!term,
    staleTime: 5 * 60_000,
  });
}