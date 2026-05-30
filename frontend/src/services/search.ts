import { SearchResponse } from '../types';
import { api } from './api';

export type SearchParams = {
  q: string;
  bookId?: string;
  cursor?: string;
  pageSize?: number;
};

export async function search(params: SearchParams): Promise<SearchResponse> {
  const { data } = await api.get<SearchResponse>('/api/v1/search', { params });
  return data;
}