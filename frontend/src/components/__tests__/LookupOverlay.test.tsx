import React from 'react';
import { Linking } from 'react-native';
import { fireEvent, render } from '@testing-library/react-native';

import LookupOverlay from '../LookupOverlay';
import * as useLookup from '../../hooks/useLookup';
import { DictionaryResult, WikipediaResult } from '../../types';

// useTheme throws outside its provider; stub the colors the overlay reads.
jest.mock('../../providers/ThemeProvider', () => ({
  useTheme: () => ({
    colors: {
      surface: '#fff',
      border: '#ccc',
      text: '#000',
      textMuted: '#666',
      accent: '#06f',
    },
  }),
}));

// The overlay drives its content off two cached queries; mock them so each test
// can pose the section in a specific state without a real fetch.
jest.mock('../../hooks/useLookup');

const mockUseDictionary = useLookup.useDictionary as jest.Mock;
const mockUseWikipedia = useLookup.useWikipedia as jest.Mock;

// Minimal stand-in for a react-query result; only the fields the overlay reads.
function queryState<T>(overrides: Partial<{ isLoading: boolean; isError: boolean; data: T }> = {}) {
  return { isLoading: false, isError: false, data: undefined, ...overrides };
}

const foundDictionary: DictionaryResult = {
  word: 'read',
  found: true,
  senses: [
    { partOfSpeech: 'verb', definition: 'interpret something written', examples: ['read the sign'] },
  ],
};

const missingDictionary: DictionaryResult = {
  word: 'zzznotaword',
  found: false,
  senses: [],
};

const foundWikipedia: WikipediaResult = {
  term: 'Project Gutenberg',
  found: true,
  title: 'Project Gutenberg',
  extract: 'A volunteer effort to digitize books.',
  pageUrl: 'https://en.wikipedia.org/wiki/Project_Gutenberg',
  thumbnailUrl: null,
};

const missingWikipedia: WikipediaResult = {
  term: 'asdkjfh',
  found: false,
  title: null,
  extract: null,
  pageUrl: null,
  thumbnailUrl: null,
};

function setup(opts: { dictionary?: ReturnType<typeof queryState>; wikipedia?: ReturnType<typeof queryState>; term?: string } = {}) {
  mockUseDictionary.mockReturnValue(opts.dictionary ?? queryState({ data: foundDictionary }));
  mockUseWikipedia.mockReturnValue(opts.wikipedia ?? queryState({ data: foundWikipedia }));
  const onClose = jest.fn();
  return { onClose, ...render(<LookupOverlay term={opts.term ?? 'read'} onClose={onClose} />) };
}

afterEach(() => {
  jest.clearAllMocks();
});

describe('LookupOverlay', () => {
  it('Should_RenderDictionarySenses_When_DefinitionFound', () => {
    const { getByText } = setup();
    expect(getByText('verb')).toBeTruthy();
    expect(getByText('interpret something written')).toBeTruthy();
    expect(getByText('“read the sign”')).toBeTruthy();
  });

  it('Should_ShowNotFoundCopy_When_DefinitionMissing', () => {
    const { getByText } = setup({ dictionary: queryState({ data: missingDictionary }) });
    expect(getByText('No definition found for “zzznotaword”.')).toBeTruthy();
  });

  it('Should_RenderExtractAndLink_When_WikipediaFound', () => {
    const { getByText, getByLabelText } = setup();
    expect(getByText('A volunteer effort to digitize books.')).toBeTruthy();

    const link = getByLabelText('Read on Wikipedia');
    fireEvent.press(link);
    expect(Linking.openURL).toHaveBeenCalledWith('https://en.wikipedia.org/wiki/Project_Gutenberg');
  });

  it('Should_ShowNotFoundCopy_When_WikipediaMissing', () => {
    const { getByText } = setup({ wikipedia: queryState({ data: missingWikipedia }) });
    expect(getByText('No Wikipedia article found.')).toBeTruthy();
  });

  it('Should_ShowReachError_When_WikipediaQueryErrors', () => {
    const { getByText } = setup({ wikipedia: queryState({ isError: true }) });
    expect(getByText('Couldn’t reach Wikipedia.')).toBeTruthy();
  });

  it('Should_CallOnClose_When_CloseButtonPressed', () => {
    const { onClose, getByLabelText } = setup();
    fireEvent.press(getByLabelText('Close lookup'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('Should_CallOnClose_When_EscapePressed', () => {
    // jest-expo runs in the node env (no `document`); the overlay only wires its
    // Esc listener on web. Install a minimal EventTarget-backed `document` so the
    // effect attaches and we can dispatch the keydown the web build would receive.
    const target = new EventTarget();
    (globalThis as { document?: unknown }).document = {
      addEventListener: target.addEventListener.bind(target),
      removeEventListener: target.removeEventListener.bind(target),
    };
    try {
      const { onClose, unmount } = setup();
      const event = new Event('keydown') as Event & { key: string };
      event.key = 'Escape';
      target.dispatchEvent(event);
      expect(onClose).toHaveBeenCalledTimes(1);
      // Unmount here, while the stub is still installed, so the effect's cleanup
      // (which calls document.removeEventListener) doesn't run after we delete it.
      unmount();
    } finally {
      delete (globalThis as { document?: unknown }).document;
    }
  });
});
