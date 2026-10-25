# Components: which one, and its rules

Each entry: what the HIG says it is for, what it must not be used for, and the rules that
decide its shape. Source: the HIG component pages, read October 2026.

## Buttons

- A button initiates an instantaneous action. Style (size, colour, shape) + content (symbol,
  text, both) + role (normal, primary, cancel, destructive).
- **Hit region ≥ 44×44pt.** Enough space around it to tell it from neighbours. Always a press
  state.
- **Prominent style for the most likely action; one or two per view.** Use style, not size,
  to mark the preferred choice; never buttons of different sizes side by side.
- **Primary** is the Return key's button and uses the accent; **never give primary to a
  destructive button**, even when it is the likely choice.
- **Text labels**: a few words, verb first, title-style capitalization ("Add to Cart").
  Append "…" when the button opens another window or view. Symbols only for familiar
  actions. Tooltip = a brief phrase on hover (macOS).
- macOS square (gradient) buttons: symbols only, in or under the view they act on, never
  labelled. Image buttons: ~10px padding, label beneath if any. One help button per window.
- Show an activity indicator (or "Checking out…") for actions that don't complete at once.

## Segmented controls

- A linear set of 2–7 equal-width segments, each a button, offering **a single choice from
  a set of closely related options that affect an object, state or view** — or, momentary,
  a set of actions without a selection (Reply / Reply all / Forward).
- **Don't mix** selection segments and action segments in one control.
- **Text or images, not both**, content of similar size; nouns or noun phrases, title-style;
  no introductory text when segments have labels (macOS: consider one, and tooltips).
- On iOS it switches closely related subviews; a tab bar switches sections. On macOS, view
  switching in the main window is a tab view; a segmented control belongs in a toolbar or
  inspector, or inside a form for a value.
- A value matching no segment shows **no selection** rather than a wrong one.

## Toggles: switches, checkboxes, radio buttons

- A toggle chooses between **two opposing states** that affect content or a view. Not for
  choosing from a list.
- Make state differences obvious by shape (fill, checkmark, dot), **never colour alone**.
- iOS: a switch only in a list row (the row is its label); elsewhere a toggle-style button.
- macOS: **checkbox** for a single on/off, aligned leading, indent for dependency, mixed
  state when subordinates differ. **Switch** for a setting to emphasize (a group's master);
  mini switch in a grouped form row. **Radio buttons** for 2–5 mutually exclusive options,
  each labelled; more than five → pop-up button. Window body only, never toolbars.

## Text fields

- Small, specific text. Larger text → text view.
- **Placeholder plus a separate label**: the placeholder disappears on typing.
- **Width matches the expected input**; fields in a group **share widths** (first/last name
  one width, address/city another); **stack vertically**; even spacing so labels pair.
- Tab order follows reading order. Validate when it makes sense: email on leaving the field,
  a user name before leaving it. Use a number formatter for numbers (currency, decimals,
  percent) and never assume a fixed presentation.
- macOS: a field paired with a list of choices is a combo box. iOS: a clear button at the
  trailing end.

## Pickers and pop-up buttons

- A picker is scrollable lists of values for **medium-to-long lists**; a **short list is a
  pull-down/pop-up button** (a picker is too heavy); a very large set is a list or table.
- Show it in context, below or beside the field it edits; don't switch views for it.
- Values predictable and logically ordered. macOS date picker: textual when space is short,
  graphical for browsing.
- A pop-up button shows the current choice and keeps it; a pull-down button runs actions.

## Steppers

- Two segments, increase/decrease, **always beside a field showing the value** (the stepper
  shows none). Pair with a text field when large changes are likely (print copies).
  macOS: Shift-click for ×10.

## Labels

- Static text people read and may copy, not edit. System font. **Four label colours carry
  importance**: label (primary), secondary (subheading, supplemental), tertiary (unavailable
  item), quaternary (watermark). Make useful text (errors, addresses) selectable.

## Lists and tables

- Prefer text in a list or table; the row format scans. **Keep item text succinct**; show
  titles and put detail in a detail view rather than tall rows.
- **Feedback on selection**: a navigation table keeps the row highlighted; **a table of
  options highlights briefly, then marks the item (a checkmark)**.
- **Styles**: iOS grouped / inset grouped use headers, footers and space between groups;
  macOS bordered uses alternating row colours for large tables.
- Column headings: nouns, title-style, no end punctuation; a single-column list without a
  heading gets a label or header. macOS: click headings to sort (and re-sort reversed),
  resizable columns, alternating rows for wide tables, outline view for hierarchy.

## Collections

- Ordered, visual content (images). **Text belongs in a table**, not a collection. Standard
  row or grid layout; adequate padding so focus/hover is visible.

## Boxes

- A visually distinct group of related items, with a border or background and optional
  title. **Keep it small relative to the containing view**: as it approaches the window's
  size it stops communicating separation and crowds everything.
- **Use padding and alignment for subgroups inside a box; nested boxes make the interface
  busy and constrained.**
- iOS boxes use the secondary and tertiary **background colours** (fills). macOS titles sit
  above. Title: brief phrase, sentence case, no end punctuation (settings panes: a colon).

## Tab views

- Mutually exclusive panes of **closely related** content in one area; the tabbed control at
  the top edge; **inset the tab view from the window body**; label each tab with a noun or
  short noun phrase, title-style; **no more than six**; never a pop-up to switch tabs.
- Controls in a pane affect only that pane. iOS: not supported — a segmented control does
  this job.

## Materials

- Materials are for the layer that **floats**: bars, sheets, popovers, menus. Content stays
  opaque. Regular (blurs, keeps text legible) for anything with text; clear only over rich
  media with a 35% dim if the media is bright. Vibrant label levels on top of materials.
