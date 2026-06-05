import React from 'react';
import { Text } from 'react-native';
import { render } from '@testing-library/react-native';

import AccessibleModal from '../a11y/AccessibleModal';

describe('AccessibleModal', () => {
  it('Should_RenderChildren_When_Visible', () => {
    const { getByText } = render(
      <AccessibleModal visible onClose={() => {}} label="Test dialog">
        <Text>Dialog body</Text>
      </AccessibleModal>,
    );
    expect(getByText('Dialog body')).toBeTruthy();
  });

  it('Should_CallOnClose_When_EscapePressed', () => {
    // jest-expo runs in node (no `document`); the Esc listener only attaches when
    // `document` exists. Install a minimal EventTarget-backed stub so the effect
    // wires up and we can dispatch the keydown the web build would receive.
    const target = new EventTarget();
    (globalThis as { document?: unknown }).document = {
      addEventListener: target.addEventListener.bind(target),
      removeEventListener: target.removeEventListener.bind(target),
    };
    try {
      const onClose = jest.fn();
      const { unmount } = render(
        <AccessibleModal visible onClose={onClose} label="Test dialog">
          <Text>Body</Text>
        </AccessibleModal>,
      );
      const event = new Event('keydown') as Event & { key: string };
      event.key = 'Escape';
      target.dispatchEvent(event);
      expect(onClose).toHaveBeenCalledTimes(1);
      unmount();
    } finally {
      delete (globalThis as { document?: unknown }).document;
    }
  });

  it('Should_NotCallOnClose_When_OtherKeyPressed', () => {
    const target = new EventTarget();
    (globalThis as { document?: unknown }).document = {
      addEventListener: target.addEventListener.bind(target),
      removeEventListener: target.removeEventListener.bind(target),
    };
    try {
      const onClose = jest.fn();
      const { unmount } = render(
        <AccessibleModal visible onClose={onClose} label="Test dialog">
          <Text>Body</Text>
        </AccessibleModal>,
      );
      const event = new Event('keydown') as Event & { key: string };
      event.key = 'a';
      target.dispatchEvent(event);
      expect(onClose).not.toHaveBeenCalled();
      unmount();
    } finally {
      delete (globalThis as { document?: unknown }).document;
    }
  });
});
