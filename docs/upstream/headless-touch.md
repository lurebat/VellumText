# Upstream draft 2 — feature request

**Status: DRAFT. Not filed.** Avalonia's `CONTRIBUTING.md` has an explicit AI Guidelines section
telling autonomous agents not to open issues or PRs. A human must read, own and submit this, and
answer review comments afterwards.

**Where:** https://github.com/AvaloniaUI/Avalonia/issues/new (Feature Request form)

**Searched before drafting (2026-08-16):** no existing issue for headless touch input.

---

## Title

`Avalonia.Headless`: no way to simulate touch input, so touch behaviour cannot be unit tested

## Is your feature request related to a problem?

`Avalonia.Headless` can simulate mouse, keyboard, wheel and drag-drop, but not touch. The full
input surface in 12.1.1 is:

```
IHeadlessWindow            KeyPress, KeyRelease, TextInput,
                           MouseDown, MouseMove, MouseUp, MouseWheel,
                           DragDrop, SetRenderScaling, GetLastRenderedFrame

HeadlessWindowExtensions   KeyPress, KeyPressQwerty, KeyRelease, KeyReleaseQwerty,
                           KeyTextInput, MouseDown, MouseMove, MouseUp, MouseWheel,
                           DragDrop, SetRenderScaling,
                           CaptureRenderedFrame, GetLastRenderedFrame
```

There is no `Touch*` member anywhere in the assembly, and no way to inject a pointer event carrying
a `TouchDevice`. The consequence is that any control behaviour conditioned on
`PointerEventArgs.Pointer.Type == PointerType.Touch` — or on `TouchDevice` specifically — is
unreachable from a headless test.

This is not a niche path. Controls routinely diverge on it: touch drag thresholds, gesture
recognisers (`ScrollGestureRecognizer`, `PullGestureRecognizer`), long-press context menus, and
touch-only affordances such as selection handles. Simulating those with `MouseDown`/`MouseMove`
exercises a different branch, so a passing test can coexist with broken touch.

Concretely, in a rich text editor we ship touch selection handles that only appear for
`PointerType.Touch`. Their whole reason to exist is untestable headlessly, so the logic behind them
is covered only by tests that call internal methods directly — which verifies our own code but not
that the events ever arrive.

## Describe the solution you'd like

Touch verbs on `IHeadlessWindow` and `HeadlessWindowExtensions`, mirroring the existing mouse ones
and taking a pointer id so multi-touch is expressible:

```csharp
void TouchDown(Window window, Point point, int touchPointId = 0, RawInputModifiers modifiers = default);
void TouchMove(Window window, Point point, int touchPointId = 0, RawInputModifiers modifiers = default);
void TouchUp  (Window window, Point point, int touchPointId = 0, RawInputModifiers modifiers = default);
```

Internally these would raise `RawTouchEventArgs` against a `TouchDevice` in the same way the mouse
helpers raise `RawPointerEventArgs` against the headless `MouseDevice`, so no new input plumbing
should be needed — `TouchDevice` and the raw touch event types already exist and are already
exercised by the real backends.

A pointer id parameter matters: without it, pinch/zoom and two-finger scroll — the cases most
likely to regress — stay untestable even after touch is added.

## Describe alternatives you've considered

- **Driving `MouseDown`/`MouseMove`/`MouseUp` instead.** Exercises the mouse branch, so it does not
  test the code in question and can give false confidence.
- **Calling the control's internal handlers directly.** What we do now. It tests our logic but not
  the event routing, hit testing or gesture recognition in between — the parts most likely to be
  wrong.
- **A custom `IHeadlessWindow`.** The interface is not designed for external implementation, and
  duplicating the platform's raw-event construction in every consumer's test project is exactly the
  kind of thing that belongs upstream once rather than downstream many times.

## Additional context

- Avalonia 12.1.1, `Avalonia.Headless` 12.1.1.
- Happy to contribute a PR if the shape above is acceptable — worth agreeing the signatures first,
  particularly whether the pointer id belongs in the public API from the start.
