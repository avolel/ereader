import { api } from './api';
import type { DictionaryResult, WikipediaResult } from '../types';

export async function defineWord(word: string): Promise<DictionaryResult> {
  const { data } = await api.get<DictionaryResult>('/api/v1/lookup/define', { params: { word } });
  return data;
}

export async function getWikipediaSummary(term: string): Promise<WikipediaResult> {
  const { data } = await api.get<WikipediaResult>('/api/v1/lookup/wikipedia', { params: { term } });
  return data;
}