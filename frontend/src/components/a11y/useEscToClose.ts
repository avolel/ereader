import { useEffect } from 'react';

// Web-only Escape-to-close. Consolidates the identical keydown effect that was
// copy-pasted into SelectionMenu, AnnotationPopover, LookupOverlay and
// NoteEditor. The web check is `typeof document` (matching the originals) rather
// than Platform.OS — `document` is the real capability we depend on, and native
// RN <Modal> already routes hardware back through onRequestClose.
//
// We listen on `document` rather than the panel because RN <Modal> portals its
// content and key events don't reliably bubble to a panel-level handler; a
// document listener is the pattern the originals already used.
export function useEscToClose(onClose: () => void, active = true): void {
  useEffect(() => {
    if (!active || typeof document === 'undefined') return;
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose, active]);
}
