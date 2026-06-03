import React from 'react';
import { fireEvent, render } from '@testing-library/react-native';

import AnnotationsDrawer from '../AnnotationsDrawer';
import { Annotation, Bookmark, TocEntry } from '../../types';

// useTheme throws outside its provider; stub the colors the drawer reads.
jest.mock('../../providers/ThemeProvider', () => ({
  useTheme: () => ({
    colors: {
      surface: '#fff',
      border: '#ccc',
      text: '#000',
      textMuted: '#666',
      accent: '#06f',
      background: '#eee',
      error: '#c00',
    },
  }),
}));

const toc: TocEntry[] = [
  { chapterId: 'ch-1', title: 'Chapter One', spineOrder: 0 },
  { chapterId: 'ch-2', title: 'Chapter Two', spineOrder: 1 },
];

const annotations: Annotation[] = [
  {
    id: 'an-1',
    bookId: 'b',
    chapterId: 'ch-1',
    type: 'highlight',
    colour: 'yellow',
    textAnchor: '{}',
    selectedText: 'highlighted text',
    noteBody: 'my note',
    createdAt: '2026-01-02T00:00:00Z',
    updatedAt: '2026-01-02T00:00:00Z',
  },
];

const bookmarks: Bookmark[] = [
  {
    id: 'bm-1',
    bookId: 'b',
    chapterId: 'ch-2',
    textAnchor: '{}',
    label: 'My bookmark',
    createdAt: '2026-01-03T00:00:00Z',
  },
];

function setup(overrides: Partial<React.ComponentProps<typeof AnnotationsDrawer>> = {}) {
  const props = {
    visible: true,
    toc,
    annotations,
    bookmarks,
    currentChapterId: 'ch-1',
    onNavigate: jest.fn(),
    onDeleteAnnotation: jest.fn(),
    onDeleteBookmark: jest.fn(),
    onClose: jest.fn(),
    ...overrides,
  };
  return { props, ...render(<AnnotationsDrawer {...props} />) };
}

describe('AnnotationsDrawer', () => {
  it('Should_GroupItemsByChapter_When_Rendered', () => {
    const { getByText } = setup();
    // Section headers from toc.
    expect(getByText('Chapter One')).toBeTruthy();
    expect(getByText('Chapter Two')).toBeTruthy();
    // Row content: highlight text + note snippet + bookmark label.
    expect(getByText('highlighted text')).toBeTruthy();
    expect(getByText('my note')).toBeTruthy();
    expect(getByText('My bookmark')).toBeTruthy();
  });

  it('Should_FireOnDeleteAnnotation_When_DeletePressed', () => {
    const { props, getAllByLabelText } = setup();
    // ch-1 renders first (spine order), so its annotation's Delete is first.
    fireEvent.press(getAllByLabelText('Delete')[0]);
    expect(props.onDeleteAnnotation).toHaveBeenCalledWith('an-1');
  });

  it('Should_NavigateWithMarkId_When_AnnotationRowTapped', () => {
    const { props, getByLabelText } = setup();
    fireEvent.press(getByLabelText('Go to highlighted text'));
    expect(props.onNavigate).toHaveBeenCalledWith('ch-1', { id: 'an-1' });
  });
});
