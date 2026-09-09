# Modality — when to interrupt

Read this **before** adding any dialog, sheet, or alert.

---

## The decision rule

Verbatim, as it is written into `AppleSheet.razor` where anyone adding a sheet
will read it:

> Use a sheet when the task is self-contained and completing it is the only goal.
> Use a non-modal floating panel when the operator needs to see or compare other
> things while working. **Comparison is never modal. If you are unsure, do not be
> modal.**

Modality is a claim that nothing else in the application matters until this is
answered. That claim is true far less often than dialogs are built.

---

## The three tiers

| Tier | Modal? | For | Template |
|---|---|---|---|
| **Floating panel** | no | anything the operator compares, references, or works alongside | your own; not shipped here |
| **Sheet** | yes | one self-contained task — a form that must complete or cancel, a multi-step configuration | `AppleSheet.razor` |
| **Alert** | yes | the operator must decide *before anything else happens* | `AppleAlert.razor` + `AppleAlerts.cs` |

The modal tier sits **beside** a floating layer; it does not replace it. An
application whose core workflow is comparing two things has no business making
either of them modal — that is a workflow you have broken, not a dialog you have
styled.

---

## Recipe: the floating panel

Not shipped as a template because its geometry is application-specific, but the
rules that make it work with this system:

- Opaque body for anything holding data; a material only if it is chrome.
- It does **not** take the scrim, and it does not trap focus.
- Multiple can be open at once. That is the point.
- If you find yourself adding a scrim "just for this one", you wanted a sheet.

---

## Recipe: the sheet

```csharp
var options = new DialogOptions
{
    MaxWidth = MaxWidth.Small,
    CloseOnEscapeKey = true,
    BackdropClick = true      // see the tradeoff below
};

var dialog = await Dialogs.ShowAsync<MyTaskSheet>(string.Empty, parameters, options);
var result = await dialog.Result;

if (result is { Canceled: false, Data: true }) { /* committed */ }
```

Inside, `AppleSheet` gives you `Title`, `Subtitle`, body content and a `Footer`.

- **Exactly one `Filled` button in the footer** — the one that completes the task.
  Cancel is `Plain` beside it.
- **A sheet that cannot be cancelled is a trap.** Always leave a way out: the
  close button, Escape, or both.
- **Width**: `MudDialog` owns its outer element, so the sheet cannot wrap itself in
  a sized container. The real width comes from `DialogOptions.MaxWidth`. The
  `Width` parameter applies `min-width` to the inner body, which only keeps a thin
  sheet from collapsing to a tooltip. Do not try to size the dialog from inside.
- The entrance rises from slightly below and settles — the motion says it came
  from the task, not from the edge of the screen.

---

## Recipe: the alert

One `await` at the call site. That is the entire design goal:

```csharp
if (!await Alerts.ConfirmAsync(
        heading: "Delete this run?",
        message: "Its output and logs are removed. This cannot be undone.",
        confirmLabel: "Delete run",
        cancelLabel: "Keep it",
        destructive: true))
{
    return;
}
```

Without a service like this, every confirmation is six lines of
`DialogParameters` and a cast — enough friction that call sites quietly **skip the
confirmation** instead. The service exists so guarding a destructive action costs
one `await`.

An alert is confirmations and destructive gates, **nothing else**. It is not a
place to show information the operator did not ask about.

---

## The four conventions that are load-bearing

### 1. Destructive confirms are red and are never the default

Making the dangerous button answer Enter is how people delete things they meant to
keep. `Destructive="true"` renders the confirm red **and** refuses it the default.

### 2. The default button is focused on open — literally

"Default" must mean *focused*, not *painted*.

A `box-shadow` copied from `--focus-ring`, drawn on the confirm button while focus
sits somewhere else entirely and Enter activates nothing, is indistinguishable
from a real ring to a keyboard operator. That is **worse than no marking at all**,
and it shipped once before it was caught.

So `AppleAlert` focuses the default button in `OnAfterRenderAsync(firstRender)`.
Enter then activates it *because it is focused*, and the ring that appears is the
browser's own `:focus-visible` — earned rather than drawn. It appears when a
keyboard opened the alert and stays away when a pointer did.

A destructive alert focuses **Cancel**, so convention 1 holds in the keyboard
channel too and not only in the paint. With no cancel to focus, a one-button
acknowledgement takes it back.

Two mechanics behind this:

- MudBlazor's dialog traps the tab ring but **does not choose a starting point**,
  and the HTML `autofocus` attribute is not honoured for content the renderer
  inserts after load. So focus must be set in code.
- **`AppleButton` exposes a `FocusAsync()` forwarder** for this. `@ref` on a
  component hands back the *component*, not an element, so a parent has no other
  route through the boundary. MudBlazor already holds the underlying
  `ElementReference`; the forwarder just passes it along, and is a safe no-op
  before the first render has produced an element.

  ```csharp
  private MudButton? _button;
  public ValueTask FocusAsync() => _button?.FocusAsync() ?? ValueTask.CompletedTask;
  ```

### 3. Labels name the consequence, not the acknowledgement

**"Delete run"**, not **"OK"**. A button that says OK is asking the operator to
remember what they clicked — and by the time they are reading the buttons, they
have often stopped reading the heading.

The cancel label is worth writing too: "Keep it" beats "Cancel" when the heading
is a question about deleting.

### 4. Read the `DialogResult`. Never rely on `OnCancel`

This is the one that produces silent data loss.

**Escape and a backdrop click go through MudBlazor's own cancel path and never
reach your component's handlers.** An `OnCancel` callback, an `OnClosed` on the
component, a flag the sheet sets on its way out — all of them are bypassed by the
two dismissal routes operators actually use.

So the outcome is read from the result, at the call site:

```csharp
var result = await dialog.Result;
return result is { Canceled: false, Data: true };
```

An unanswered question is a **no**. Only an explicit confirm reads as true.

`OnCancel` on `AppleSheet` is still useful — for cleanup the sheet itself owns. It
is not useful for knowing what happened.

---

## `BackdropClick` — the tradeoff

`DialogOptions.BackdropClick` decides whether clicking outside dismisses.

| | `BackdropClick = false` | `BackdropClick = true` |
|---|---|---|
| **Use for** | alerts, and any sheet holding unsaved input | sheets that are read-mostly or trivially reopened |
| **Why** | a decision is not dismissed by missing the alert; a mis-click must not discard a half-filled form | trapping an operator behind a panel they only wanted to glance past is its own hostility |

`AppleAlerts` sets `BackdropClick = false` and `CloseOnEscapeKey = true`. That
pairing is deliberate: Escape is a **deliberate** dismissal and reads as "no";
a stray click near the edge of a modal is not a decision at all.

For a sheet, decide per sheet. If it holds unsaved input, turn it off.

---

## Before you add a modal, in order

1. Does the operator need to see anything else while doing this? → **not modal**.
2. Is it a question with two answers? → **alert**.
3. Is it one self-contained task that must complete or cancel? → **sheet**.
4. Still unsure? → **not modal.**
