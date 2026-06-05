/**
 * @jest-environment jsdom
 *
 * useFocusTrap is web-only and manipulates real DOM focus, so this suite runs in
 * jsdom and forces Platform.OS to 'web'. We render with react-dom directly (the
 * project uses @testing-library/react-native, which targets the RN renderer, not
 * the DOM) and attach the returned ref to a real <div>.
 */
import React, { act } from 'react';
import { createRoot, Root } from 'react-dom/client';

import { useFocusTrap } from '../a11y/useFocusTrap';
// react-dom/client is typed locally in src/types/react-dom-client.d.ts since the
// project doesn't depend on @types/react-dom.

// The hook only consumes Platform at runtime (View is type-only and erased).
jest.mock('react-native', () => ({ Platform: { OS: 'web' } }));

function Harness({ active }: { active: boolean }) {
  const ref = useFocusTrap(active);
  // The ref is typed for an RN View but resolves to this <div> under the DOM.
  return (
    <div ref={ref as unknown as React.RefObject<HTMLDivElement>}>
      <button>one</button>
      <button>two</button>
    </div>
  );
}

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

describe('useFocusTrap', () => {
  it('Should_FocusFirstFocusable_When_Activated', () => {
    act(() => root.render(<Harness active />));
    expect((document.activeElement as HTMLElement)?.textContent).toBe('one');
  });

  it('Should_RestoreFocusToTrigger_When_Deactivated', () => {
    const trigger = document.createElement('button');
    document.body.appendChild(trigger);
    trigger.focus();

    act(() => root.render(<Harness active />));
    // Focus moved into the panel…
    expect((document.activeElement as HTMLElement)?.textContent).toBe('one');

    act(() => root.unmount());
    // …and returns to the trigger on teardown.
    expect(document.activeElement).toBe(trigger);
    trigger.remove();
  });

  it('Should_WrapForwardTab_When_FocusOnLast', () => {
    act(() => root.render(<Harness active />));
    const [first, last] = Array.from(container.querySelectorAll('button'));
    last.focus();
    act(() => {
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
    });
    expect(document.activeElement).toBe(first);
  });

  it('Should_WrapBackwardTab_When_FocusOnFirst', () => {
    act(() => root.render(<Harness active />));
    const [first, last] = Array.from(container.querySelectorAll('button'));
    first.focus();
    act(() => {
      document.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true }),
      );
    });
    expect(document.activeElement).toBe(last);
  });
});
