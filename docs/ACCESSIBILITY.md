# EReader — Accessibility (a11y) Deep-Dive

A full walk-through of how accessibility is built into the EReader frontend, written
as a **tutorial for an engineer who knows JavaScript/React but has never deliberately
implemented accessibility before**. It teaches the concepts (the accessibility tree,
ARIA, live regions, focus management, the keyboard contract) as it walks the actual
code, so you can both understand what's here and extend it correctly.

> This is a companion to [CODE_OVERVIEW.md](CODE_OVERVIEW.md) §12 (the frontend
> deep-dive) and [LOOKUP.md](LOOKUP.md). If React Native is new to you, read
> CODE_OVERVIEW §12.0 (the 5-minute RN mental model) first — this doc assumes you
> know what `<View>`, `<Text>`, `<Pressable>` and the `.web.tsx`/`.native.tsx`
> platform split are.

All of the reusable accessibility primitives live in one folder:
[`frontend/src/components/a11y/`](../frontend/src/components/a11y/). The feature was
landed as a single "WCAG 2.1 AA pass" across the reader, the overlays, and every
screen.

---

## 0. The mental model: what "accessibility" actually means here

Accessibility (abbreviated **a11y** — "a", 11 letters, "y") means the app is usable
by people who don't interact with it the way you do in development: people using a
**screen reader** (software that speaks the UI aloud — VoiceOver on macOS/iOS,
NVDA/JAWS on Windows, TalkBack on Android), people navigating **by keyboard only**
(no mouse/touch), people who need **large text or high contrast**, and people who get
motion-sick from animations.

The thing that makes this tractable is the **accessibility tree**. Alongside the DOM
(on web) or the native view hierarchy (on iOS/Android), the platform builds a
parallel tree of *semantic* nodes: "this is a button named 'Close lookup'", "this is
a heading", "this is a dialog". Screen readers and other assistive technology (**AT**)
read *that* tree, not your pixels. Almost all a11y work is really one task:

> **Make the accessibility tree describe your UI correctly.**

On the web, the vocabulary for annotating that tree is **ARIA** (Accessible Rich
Internet Applications) — attributes like `role="dialog"`, `aria-label`,
`aria-modal`, `aria-live`. In React Native, the same concepts have RN-native prop
names: `accessibilityRole`, `accessibilityLabel`, `accessibilityState`,
`accessibilityViewIsModal`, `accessibilityLiveRegion`. React Native Web translates
the RN props into ARIA where it can. Because EReader runs on **both** web and native
from one component tree, you'll constantly see this pattern: set the RN
`accessibility*` prop for native, and *also* spread raw ARIA attributes when
`Platform.OS === 'web'` for the cases RNW doesn't cover. That dual-write is the
single most important thing to understand about this codebase's a11y.

The target standard is **WCAG 2.1 Level AA** — the Web Content Accessibility
Guidelines. You'll see specific success criteria cited in code comments (e.g. "WCAG
2.4.1", "WCAG 2.4.7"). You don't need to memorise them; the relevant ones are
explained inline below.

### The four pillars this codebase implements

1. **Semantics** — every interactive element has a role and an accessible name.
2. **Focus management** — keyboard focus is trapped inside modals, returned on
   close, and there's a "skip to content" escape hatch.
3. **Live announcements** — things that happen without a visible focus change
   (a highlight is added, a chapter loads, a lookup resolves) are spoken aloud.
4. **Respecting user preferences** — reduced motion, language, visible focus rings.

The rest of this doc is one section per primitive, in dependency order.

---

## 1. The keyboard & focus model (start here — it underpins everything)

Before any component, internalise the keyboard contract, because every primitive
exists to satisfy some part of it:

- **Tab / Shift+Tab** move focus forward/backward through *focusable* elements
  (links, buttons, inputs — anything in the "tab order").
- **A visible focus indicator** must show which element currently has focus (WCAG
  2.4.7 *Focus Visible*). Sighted keyboard users need to see where they are.
- **When a dialog opens**, focus must move *into* it, stay *trapped* inside while
  it's open (Tab at the last element wraps to the first, not out to the page
  behind), and **return to the trigger** when it closes (WCAG 2.4.3 *Focus Order* /
  2.1.2 *No Keyboard Trap* — the trap must be escapable via the dialog's own close,
  not a dead end).
- **Escape** closes overlays.
- **There must be a way to skip repetitive chrome** and jump to the main content
  (WCAG 2.4.1 *Bypass Blocks*).

"Focusable" on the web means the element is in the tab order. Natively focusable
elements are `<a href>`, `<button>`, `<input>`, `<textarea>`, `<select>`, and
anything with a non-negative `tabindex`. `tabindex="-1"` is a special value:
*programmatically* focusable (you can call `.focus()` on it) but **skipped by Tab**.
That distinction shows up everywhere below.

---

## 2. `IconButton` — the accessible button contract

[a11y/IconButton.tsx](../frontend/src/components/a11y/IconButton.tsx)

**The problem it solves.** Most of the reader's chrome is glyph buttons — `←`, `Aa`,
`✎`, `⋯`, colour swatches. To a screen reader, a `<Pressable>` wrapping the character
`⋯` is "button, ⋯" or worse, silence. It has no readable name. And a bare
`<Pressable>` paints no focus ring, so keyboard users can't see where they are.
`IconButton` centralises the fix so call sites stop re-deriving it.

```tsx
<IconButton label="More options" onPress={() => setConfirmDelete(true)}>
  <Text style={{ color: colors.accent }}>⋯</Text>
</IconButton>
```

What it guarantees on every button:

- **A required `label` prop → accessible name.** It's *required* in the type, not
  optional, precisely because the visible child is usually a glyph with no readable
  text. This is the most common a11y bug in any codebase ("icon button with no
  label") designed out at the type level. It maps to `accessibilityLabel` (RN) which
  RNW renders as `aria-label`.
- **A role** via `accessibilityRole` (default `'button'`), overridable to
  `'link'`, `'menuitem'`, `'radio'`, etc. for items inside lists/groups. Roles tell
  AT *how* to announce and operate the element ("button, double-tap to activate"
  vs "link").
- **State** via `accessibilityState={{ disabled, selected, busy }}`. A toggle passes
  `selected`; a saving button passes `busy`. AT announces "selected" / "busy" /
  "dimmed". This is how a screen-reader user knows a segmented control's current
  choice without seeing the highlight.
- **A visible focus ring** while focused, and *only* while focused (see §3).
- **`hitSlop`** (default 8px) — expands the touch target beyond the glyph's tiny
  bounds. This is WCAG 2.5.5 *Target Size* in spirit: a 16px glyph is too small to
  reliably hit.

The focus ring is gated on a local `focused` state flag (`onFocus`/`onBlur`), not
left to CSS — the next section explains why.

---

## 3. `focusStyles` — the visible focus ring, and a real CSS gotcha

[a11y/focusStyles.ts](../frontend/src/components/a11y/focusStyles.ts)

`focusRing(accent)` returns a web-only style object that draws a 2px solid outline
offset 2px from the control. On native it returns `{}` — the OS draws its own focus
cursor, so we don't fight it.

Two things worth understanding here, because they're the kind of detail that bites
you later:

1. **The `as unknown as ViewStyle` cast.** `outlineColor`, `outlineStyle`,
   `outlineWidth`, `outlineOffset` are real CSS, and React Native Web understands
   them, but they are **not** in React Native's `ViewStyle` TypeScript type (RN has
   no `outline`). So the object is cast through `unknown` to satisfy the compiler.
   The cast is deliberately confined to this one helper so call sites receive a clean
   `ViewStyle` and never see the hack.

2. **Why the ring is gated on a `focused` flag instead of a CSS pseudo-class.** On
   the real web you'd write `:focus-visible { outline: … }` — the browser only paints
   the ring for *keyboard* focus, not mouse clicks. But you can't express a
   pseudo-class from an inline React Native Web style object. So this codebase
   applies the ring style *imperatively* while `onFocus`/`onBlur` say the control is
   focused (see `IconButton`). The documented trade-off: the ring shows on **any**
   focus, including a mouse click, not just keyboard focus. If you ever apply
   `focusRing()` unconditionally (not gated on a focus flag), you'll draw a permanent
   outline — the comment in the file warns about exactly this.

**Takeaway for extending:** any new focusable control should track its own
`focused` state and apply `focusRing(colors.accent)` only while focused — or just use
`IconButton`, which already does it.

---

## 4. `useFocusTrap` — keeping Tab inside an open dialog

[a11y/useFocusTrap.ts](../frontend/src/components/a11y/useFocusTrap.ts)

This is the most intricate piece, and it's worth reading slowly because focus traps
are subtle. It's **web-only**: on native, RN's `<Modal>` plus
`accessibilityViewIsModal` already scope focus to the modal, so the hook is a no-op
there and the returned ref simply goes unused by the DOM logic.

The hook returns a ref you attach to the modal panel `<View>`. Under React Native
Web that `<View>` is a real DOM node at runtime, which is why the code reads it as an
`HTMLElement` (via an `as unknown as HTMLElement` bridge — same dual-nature trick as
`focusStyles`).

What it does, in order, when the panel becomes active:

1. **Record the trigger to restore focus to later** — but *only if it's outside the
   panel*. This guards a real bug: a child with `autoFocus` (e.g. the note editor's
   `TextInput`) fires its focus in a mount effect *before* this parent effect runs,
   so by the time the trap initialises, focus may already be inside the panel.
   Recording that as the "previously focused" element would, on close, try to return
   focus to a node that's being unmounted. So it only remembers `document.activeElement`
   if the panel doesn't contain it.

2. **Move focus into the dialog** — unless something inside already has it (respect an
   `autoFocus` child). Otherwise focus the first focusable descendant; if there are
   none, give the panel itself `tabindex="-1"` and focus that, so focus is at least
   *inside* the dialog and not stranded on the page behind.

3. **Install a `keydown` listener that implements the wrap.** On Tab: get the current
   list of focusables *fresh each time* (the panel's contents can change — async-loaded
   sections, conditional buttons — between open and the keypress). If Shift+Tab is
   pressed while on the first element (or focus has somehow escaped the panel), wrap to
   the last. If Tab is pressed on the last element (or focus escaped), wrap to the
   first. If there are no focusables at all, `preventDefault` and pin focus to the
   panel. This is the "no keyboard escape" half of the trap.

4. **On cleanup (close/unmount): restore focus** to the recorded trigger, *if it's
   still in the document* (`previouslyFocused.isConnected`). Note the comment: it does
   **not** gate restore on "focus is still inside the panel", because on unmount React
   detaches the panel *before* the effect cleanup runs, so that check would spuriously
   fail. The trap kept focus inside while open, so an unconditional restore is safe.

The `FOCUSABLE_SELECTOR` at the top is the standard WAI-ARIA dialog recommendation:
`'a[href], button, input, textarea, select, [tabindex]:not([tabindex="-1"])'`. The
`:not([tabindex="-1"])` clause is exactly the "programmatically focusable but
Tab-skipped" exclusion from §1.

**Why a custom hook and not a library?** It's ~90 lines, web-only, and integrated
with the RN `<View>`-ref/DOM-node duality that an off-the-shelf DOM focus-trap
library wouldn't understand. There's a dedicated test
([useFocusTrap.test.tsx](../frontend/src/components/__tests__/useFocusTrap.test.tsx))
that exercises the wrap and the restore.

---

## 5. `useEscToClose` — Escape dismisses overlays

[a11y/useEscToClose.ts](../frontend/src/components/a11y/useEscToClose.ts)

A tiny web-only hook: while `active`, a `document`-level `keydown` listener calls
`onClose` on `Escape`. Two decisions explained in its comments:

- **It checks `typeof document` rather than `Platform.OS === 'web'`.** The real
  capability it depends on is `document` existing; native RN `<Modal>` already routes
  the hardware back button through `onRequestClose`, so it doesn't need this.
- **It listens on `document`, not on the panel.** RN `<Modal>` *portals* its content
  (renders it elsewhere in the tree), so key events don't reliably bubble up to a
  panel-level handler. A document listener is reliable.

This consolidates an identical effect that had been copy-pasted into four overlays
(`SelectionMenu`, `AnnotationPopover`, `LookupOverlay`, `NoteEditor`).

---

## 6. `AccessibleModal` — the one dialog wrapper everything uses

[a11y/AccessibleModal.tsx](../frontend/src/components/a11y/AccessibleModal.tsx)

This is the keystone. Before it, the codebase repeated a hand-rolled "RN `<Modal>` +
full-screen backdrop `<Pressable>` + inner `<Pressable>` that swallows taps" pattern
across **eight** overlays, each re-implementing (or forgetting) focus, Esc, and dialog
semantics. `AccessibleModal` centralises all of it. Every overlay — `SettingsDrawer`,
`TableOfContents`, `AnnotationsDrawer`, `ConfirmDialog`, `SelectionMenu`,
`AnnotationPopover`, `NoteEditor`, `LookupOverlay` — now renders through it.

What it standardises:

- **Focus trap** (`useFocusTrap(visible)`) and **Esc-to-close**
  (`useEscToClose(onClose, visible)`), wired once.
- **The backdrop dismiss pattern.** A full-screen `<Pressable>` calls `onClose` on
  tap; an inner `<Pressable onPress={() => {}}>` wraps the panel to *swallow* taps so
  a press on the dialog itself doesn't fall through and dismiss it. (This is the
  pattern that was duplicated everywhere; now it lives in one place.)
- **Dialog semantics, dual-written for web vs native:**
  - **Web:** spreads `role` (`'dialog'` or `'alertdialog'`), `aria-modal={true}`,
    `aria-label={label}`, and optionally `aria-describedby`. `aria-modal` tells AT
    that everything *behind* the dialog is inert — the screen reader won't wander into
    the page behind it. `alertdialog` (used by `ConfirmDialog`) is a stronger cue for
    destructive confirmations that demands the user's attention.
  - **Native:** `accessibilityViewIsModal` on the panel already scopes the native
    accessibility tree to the dialog, so adding the ARIA props there would be
    redundant — hence the `Platform.OS === 'web'` conditional spread. This is the
    dual-write pattern from §0 made concrete.
- **`onRequestClose={onClose}`** routes the Android hardware back button to close.
- **Positioning** via an `align` prop (`center` / `bottom` / `custom`). `custom`
  leaves placement entirely to `panelStyle`, which is how anchored popovers like
  `SelectionMenu` position themselves at a selection rect.
- **`dimmed`** — anchored popovers (`SelectionMenu`, `AnnotationPopover`) pass
  `dimmed={false}` so they don't darken the reader behind them; full dialogs keep the
  default 40%-black scrim.

**The accessible name (`label`) is required.** When focus enters the dialog, AT
announces this name — "Lookup: photosynthesis", "Selection actions", "Display
settings". Without it the user hears "dialog" and nothing else.

When you add a new overlay, you should **never** hand-roll `<Modal>` again — render
`AccessibleModal` and you inherit focus, Esc, backdrop dismiss, dialog role, and the
Android back-button wiring for free. There's a test
([AccessibleModal.test.tsx](../frontend/src/components/__tests__/AccessibleModal.test.tsx))
covering the role/label/aria-modal output and the backdrop behaviour.

---

## 7. `useAnnouncer` — speaking things that have no visible focus change

[a11y/useAnnouncer.tsx](../frontend/src/components/a11y/useAnnouncer.tsx)

**The problem.** A lot happens in the reader without focus moving: you add a
highlight, a chapter finishes loading, a dictionary lookup resolves, a bookmark is
set. A sighted user sees the result. A screen-reader user, whose attention follows
*focus*, gets nothing — the change happened "off-screen" as far as AT is concerned.

**The solution: a live region.** A live region is an element the screen reader
*watches*; when its text content changes, AT announces the new text automatically,
without focus moving there. On the web this is `aria-live`; in RN it's
`accessibilityLiveRegion`. There are two urgencies:

- **`polite`** — wait for a pause in current speech, then announce. Used for
  non-urgent confirmations: "Highlight added", chapter changes, "Definition found".
- **`assertive`** — interrupt immediately. Reserved for errors: "Failed to load
  chapter", "Could not load the dictionary".

`AnnouncerProvider` is mounted **once**, app-wide (in
[app/_layout.tsx](../frontend/app/_layout.tsx), inside `ThemeProvider`), so any screen
can announce without rendering its own region. It holds two visually-hidden `<Text>`
nodes — one polite, one assertive — and exposes `announce(message, assertive?)` via
context. Consume it with `const { announce } = useAnnouncer()`.

Two implementation details that are easy to get wrong and are handled here:

1. **The clear-then-set cycle.** `announce` sets the region text to `''`, then sets
   the real message on the *next animation frame* (`requestAnimationFrame`).
   Why: if you announce the same string twice (two identical errors in a row), the
   node's text doesn't actually *change* the second time, so the screen reader stays
   silent. Emptying it first guarantees a real change and forces a re-read.

2. **`visuallyHidden` is a clipped 1×1 box, not `display: none`.** `display: none`
   (and RN's equivalent) removes the node from the **accessibility tree** entirely —
   a hidden live region announces nothing. The 1px-overflow-hidden technique keeps the
   node present and readable by AT while invisible on screen. This same "visually
   hidden but present" pattern is reused by `SkipToContent`.

Examples of `announce` in use, from
[ReaderScreen](../frontend/src/screens/ReaderScreen.tsx):
`announce('${colour} highlight added')`, `announce('Bookmark set')`,
`announce('Note saved')`, `announce('Annotation deleted')`,
`announce('Failed to load chapter', true)` (assertive), and the chapter-change
announcement `announce('${title}, chapter ${idx + 1} of ${toc.length}')`.

---

## 8. `SkipToContent` — the keyboard bypass link (WCAG 2.4.1)

[a11y/SkipToContent.tsx](../frontend/src/components/a11y/SkipToContent.tsx)

A keyboard user landing on any screen would otherwise have to Tab through the entire
header chrome (back button, TOC, annotations, settings, ⋯) on *every* page before
reaching the content. The "skip to content" link is the standard fix and the very
first WCAG criterion most audits check.

How this one works:

- It's **web-only** (returns `null` on native — there's no equivalent affordance) and
  rendered as the **first focusable element in the app** (in `app/_layout.tsx`, before
  `<Slot />`), so it's the first thing Tab lands on.
- It's **visually hidden until focused** (the same clipped-box technique as the
  announcer), then slides into the top-left corner with a visible border — so it only
  appears for the keyboard user who actually Tabs to it, never for mouse/touch users.
- On activation it finds the element with id `main-content`
  (`document.getElementById('main-content')`), ensures it can receive focus (adds
  `tabindex="-1"` if absent — remember: programmatically focusable, Tab-skipped), and
  `.focus()`es it. Focus jumps past all the chrome to the content.

The other half of the contract is on the screens: the reader's root `<View>` tags
itself `nativeID="main-content"` and `role="main"`
([ReaderScreen.tsx](../frontend/src/screens/ReaderScreen.tsx)). `nativeID` is how you
set a DOM `id` from React Native Web. `role="main"` is the ARIA landmark that lets AT
users jump to the main region directly. **Any new top-level screen should tag its main
container the same way** or the skip link has nothing to target.

---

## 9. Accessibility inside the reader WebView

The chapter HTML renders inside a WebView (native) / `<iframe>` (web). That's a
separate document with its *own* accessibility tree, so a11y has to be injected into
the document EReader builds for it. This lives in
[lib/webviewScripts.ts](../frontend/src/lib/webviewScripts.ts) (`buildChapterDocument`).
The pieces:

- **`<html lang="…">`** — the chapter's language (from the book's metadata, default
  `en`) is set on the root element (WCAG 3.1.1 *Language of Page*). Screen readers use
  this to pick the right pronunciation/voice. The value is HTML-attribute-escaped.
- **`prefers-reduced-motion` respected** (WCAG 2.3.3 *Animation from Interactions*).
  Newly-created highlights flash briefly to draw the eye; inside a
  `@media (prefers-reduced-motion: reduce)` block that flash animation is dropped
  entirely for users who've asked their OS to minimise motion.
- **The content container is a labelled landmark:**
  `<div id="er-content" role="document" aria-label="Chapter content" tabindex="-1">`.
  The `tabindex="-1"` makes it programmatically focusable so scroll-restore / skip
  targeting can land focus there.
- **Highlights are exposed semantically.** Each `<mark>` highlight gets
  `tabindex="0"` (so a keyboard user can Tab *to* a highlight) and an `aria-label`
  describing it — so highlights aren't just a background colour invisible to AT, but
  navigable, announced elements.

The host-side WebView wrappers
([ReaderWebView.tsx](../frontend/src/components/ReaderWebView.tsx) /
[ReaderWebView.web.tsx](../frontend/src/components/ReaderWebView.web.tsx)) also carry
accessible labels for the surface itself, so the reading pane announces as a named
region rather than an anonymous box.

---

## 10. The reader keyboard shortcuts (and how they avoid hijacking input)

[ReaderScreen.tsx](../frontend/src/screens/ReaderScreen.tsx) (the keydown effect)

Web-only chapter paging: **←/Page Up** = previous chapter, **→/Page Down** = next.
The interesting part is the **guards** — getting these wrong is how you ship an app
that won't let people type:

- **Bail if any modal is open** (`tocOpen || settingsOpen || … || lookupTerm`). When
  an overlay is up, its own keys (and the focus trap) own the keyboard; the reader
  must not also page chapters underneath it.
- **Bail if focus is in a text field** (`INPUT` / `TEXTAREA` / `isContentEditable`).
  Otherwise typing an arrow key inside the note editor would flip the chapter.
- **It deliberately doesn't fire when focus is inside the chapter iframe** — key
  events don't cross the frame boundary, so the iframe keeps its native arrow-key
  scrolling there. That's intentional, noted in the comment.

This is the general lesson for any global keyboard handler: scope it tightly, and
always exclude text-entry contexts and open overlays.

---

## 11. Screen-level semantics

The a11y pass also went through every screen and added the "boring but essential"
semantics. Patterns you'll see and should copy:

- **Headers** are tagged `accessibilityRole="header"` (RNW → `role="heading"`) so AT
  users can jump heading-to-heading. The reader title block, the lookup term, and the
  dictionary/Wikipedia section labels all do this.
- **Landmarks**: the main content container gets `role="main"` +
  `nativeID="main-content"` (§8).
- **Links vs buttons**: things that navigate *out* (e.g. "Read on Wikipedia" opening
  an external page) use `accessibilityRole="link"`; things that perform an in-app
  action use the default button role. Getting this right changes how AT announces and
  operates the control.
- **Form fields** on the login/register screens get proper labels and the inputs are
  associated with them.
- **Every glyph/icon affordance goes through `IconButton`** so it can never ship
  without a label.

---

## 12. How it all fits together (the dependency graph)

```
app/_layout.tsx
 ├─ AnnouncerProvider   ← mounts the two live regions once, provides announce()
 │   └─ SkipToContent   ← first focusable element; targets #main-content
 │   └─ <Slot/> (screens)
 │        ReaderScreen / LibraryScreen / …
 │          ├─ root View: nativeID="main-content" role="main"
 │          ├─ IconButton (every chrome button: label + role + state + focus ring)
 │          │     └─ focusRing()  ← visible focus indicator (web)
 │          ├─ useAnnouncer().announce(...)  ← "Highlight added", errors, chapter changes
 │          └─ AccessibleModal (every overlay)
 │                ├─ useFocusTrap()   ← trap + restore (web)
 │                ├─ useEscToClose()  ← Escape closes (web)
 │                └─ role/aria-modal/aria-label (web) | accessibilityViewIsModal (native)
 └─ reader WebView document (lib/webviewScripts.ts)
       lang=…, prefers-reduced-motion, role="document", <mark> tabindex/aria-label
```

The shape to remember: **three app-level singletons** (announcer, skip link, and the
modal wrapper used everywhere) plus **two contracts screens must honour** (tag your
main region; route every button through `IconButton`). Everything else is detail.

---

## 13. Extending it — a checklist

When you add UI, run through this:

- [ ] **New button or tappable glyph?** Use `IconButton` with a `label`. Don't
      hand-roll a `<Pressable>` with a bare glyph child.
- [ ] **New overlay/dialog/popover?** Render it through `AccessibleModal` with a
      `label`. Never re-implement `<Modal>` + backdrop. Use `role="alertdialog"` for
      destructive confirmations.
- [ ] **Something happens without focus moving** (save, delete, async result, error)?
      Call `announce(...)` — `assertive: true` for errors only.
- [ ] **New top-level screen?** Tag the main container `nativeID="main-content"` and
      `role="main"` so the skip link works.
- [ ] **New focusable custom control?** Track a `focused` flag and apply
      `focusRing(colors.accent)` while focused — or just use `IconButton`.
- [ ] **Web-specific ARIA the RN prop doesn't cover?** Spread it under a
      `Platform.OS === 'web'` guard, mirroring `AccessibleModal`.
- [ ] **Headings** get `accessibilityRole="header"`; **outbound links** get
      `accessibilityRole="link"`.
- [ ] **Animation?** Wrap it in `@media (prefers-reduced-motion: reduce)` (or the RN
      equivalent) so it can be turned off.

### Testing a11y

Unit tests query **by role/label/text, never by test ID** (a project rule) — which
means the tests only pass if the accessibility tree is correct, so they double as a11y
regression coverage. See
[IconButton.test.tsx](../frontend/src/components/__tests__/IconButton.test.tsx),
[AccessibleModal.test.tsx](../frontend/src/components/__tests__/AccessibleModal.test.tsx),
[useFocusTrap.test.tsx](../frontend/src/components/__tests__/useFocusTrap.test.tsx),
and [webviewScripts.test.ts](../frontend/src/lib/__tests__/webviewScripts.test.ts)
(asserts the injected `lang`, roles, and reduced-motion CSS). Beyond unit tests,
verify manually: Tab through each screen watching the focus ring, open every overlay
and confirm Tab stays trapped + Esc closes + focus returns, and run an actual screen
reader (VoiceOver: ⌘+F5) over the reader to hear the announcements.
