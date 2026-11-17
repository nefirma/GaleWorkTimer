# Copy-name / scrollbar layout evidence

## Delivered layout

`src/GaleWorkTimer/Views/MainWindow.axaml` places both active and archived rows
inside the same vertical `ScrollViewer`. Its content `StackPanel` now has a
32-DIP trailing margin. That value is not a claimed scrollbar measurement: it
matches the existing copy button's 20-DIP icon plus 6-DIP padding on each side,
reserving one complete copy-target footprint at the content's trailing edge.
This leaves a target-sized content gutter at the edge where an overlay scrollbar
could otherwise cover row content. It is not asserted to exceed a platform rail
width; whether it fully clears actual scrollbar bounds remains NOT_YET_PROVEN
without a renderer.

The outer `ScrollViewer` keeps its default automatic vertical scrolling and has
no forced scrollbar. The active `ListBox` continues to disable its own scrollbars,
so the existing outer viewer owns list scrolling. The archived `Expander` and
its items remain in that same scroll content. The 32-DIP margin reduces the row
content width; it does not move the copy button outside the viewport. Active
title text remains in the star-sized Grid column between its selection control
and copy button, and archived title text remains in the star-sized column before
its copy button. `TextBlock.timerTitle` retains `TextWrapping=Wrap` and has no
minimum width in either row template.

## Static condition coverage

| Condition | Static consequence |
| --- | --- |
| Scrollbar hidden / no vertical overflow | Default auto visibility does not force a bar; the trailing gutter remains unused space. |
| Scrollbar visible / vertical overflow | The ScrollViewer still owns scrolling; row content has a 32-DIP trailing inset. Actual overlap geometry is not rendered/proven. |
| Scrollbar expanded | The content gutter remains 32 DIP; whether that fully clears the expanded platform rail is NOT_YET_PROVEN. |
| Archive collapsed / expanded | Both states remain children of the same outer scroll content; expansion adds height and uses the same gutter. |
| Short / long names | Star-sized title columns use remaining row width; existing wrapping retains the full bound name without widening the grid. |
| Minimum / normal window width | The window minimum remains 760 DIP and normal width 1020 DIP. The same star-column constraint applies; at minimum width titles wrap before copy targets are displaced. |
| Light / Dark theme | The change only constrains layout. Existing dynamic title foreground/background and Fluent button theme resources are unchanged. |

This is static reasoning from the XAML and resources, not a rendered-geometry
result. Actual overlap removal without a renderer remains NOT_YET_PROVEN under
Phil's explicit UI-evidence waiver; no screenshots, UI smoke, manual interaction,
or Windows runtime claim is made.

## Wiring and behavior preserved

Both templates retain `TextBlock.timerTitle` bound to the row `Name` and the
same `Button.copyName` with `Click="CopyTimerName"`. The button retains the
existing `CopyIcon.Geometry` PathIcon, tooltip `Copy name`, accessible name
`Copy timer name`, and normal focusability (it is not hidden or disabled).
`Row(sender)` continues to take the button's row `DataContext`; the existing
handler passes that row to `CopyTimerNameAsync`, which writes exactly `timer.Name`
and reports failure through the existing error message. No handler, view model,
service, clipboard, timer, or snapshot code was changed. Existing T2.5 tests
cover exact-name clipboard writes and synchronous/asynchronous clipboard failure
without refreshing data; they do not prove layout or rendered hit bounds.

## Validation record

Observed 2026-11-17 from the repository root:

| Command | Result | Coverage / limits |
| --- | --- | --- |
| `dotnet build` | PASS, exit 0; 0 warnings, 0 errors | Existing solution builds. |
| `dotnet test` | PASS, exit 0; application suite 80 passed, packaging suite 57 passed; 0 failed, 0 skipped (137 total) | Existing unit/integration suites only; not rendered layout, Windows runtime, or actual overlap geometry. |

An earlier exploratory build on the same scoped source emitted two warnings
(`AVLN3001` on `MainWindow.axaml`, `xUnit2013` on
`InstallManagerTests.cs:337`). Neither warning appeared in the final build
recorded above. The delivery pack records the source digest for the final
validation runs. These checks establish build/test behavior only. They do not
establish UI geometry or Windows runtime behavior.
