---
name: apple-hig-components
description: Use when designing, building or reviewing any screen, window, form, wizard or control so it follows Apple's Human Interface Guidelines for components — which component the job calls for, how it is grouped, labelled, sized and coloured, and the tells that make a screen read as blocky or generated. Distilled from developer.apple.com/design/human-interface-guidelines (components, layout, color, typography, dark mode) in October 2026. Pairs with apple-mudblazor, which supplies the tokens and MudBlazor seam; this skill supplies the component judgment.
---

# Apple HIG components, applied

Apple's guidelines are not a style; they are a set of answers to "which control is this,
and how does it sit with its neighbours". A screen reads as blocky when those answers were
skipped: every group boxed, every cell outlined, every label shouting, two buttons fighting
to be primary. This skill is the answer key, distilled from the HIG so that it can be
applied in any UI stack — the rules are stated in points, levels and roles, not in CSS.

Read `references/components.md` for the control you are about to draw, `references/forms.md`
when the screen asks the person for anything, `references/type-color-layout.md` for sizes
and colours, and run `references/audit.md` before calling a screen done.

## The method

1. **Name the job, then the component.** Picking one of a few? Segmented control (2–5
   segments, all visible) or radio buttons. Picking one of many? Pop-up button. On or off?
   Checkbox (macOS forms) or switch (a list row). Small text? Text field with a label.
   An amount that nudges? Stepper beside a field. The HIG has a component for every job in
   `references/components.md`; a grid of buttons is never the answer to "pick one".

2. **Group by the weakest device that works.** The HIG's order is: negative space, then a
   container shape (a fill), then a separator line. Reach for the next device only when the
   one before fails. A **box with a border is the strongest device and the last resort**;
   the HIG says to keep a box small relative to its view and never nest boxes — "adding
   nested boxes to define subgroups can make the interface feel busy and constrained".
   Grouped lists on iOS use **fill levels** (secondary grouped background on primary) and
   **inset separators**, not outlines, and that is the look to reach for in a form.

3. **One prominent action per view.** Prominence is style, not size. At most one or two
   prominent buttons; the rest are plain. The primary role goes to the likely, non-destructive
   choice. Destructive is red at any weight and never primary.

4. **Say hierarchy with the four label levels and the text styles, not with caps, colour
   or boxes.** Label, secondary, tertiary, quaternary; title 2 / title 3 / headline / body /
   subheadline / footnote. Title-style capitalization for control labels, sentence case for
   copy; no all-caps eyebrows.

5. **Colour is for state and for the one accent.** Don't rely on colour alone; don't reuse
   the accent for anything that is not interactive; put the accent on a primary button's
   background, not on its text; keep content layers opaque.

6. **Hit regions are 44pt; controls sit at least a space apart.** Rows in a form are 28–44pt
   tall; fields match their expected input and share widths within a group.

7. **Progressive disclosure.** Show the step being worked; fold what is done to one line
   and what is waiting to one line. Never a control that cannot be used yet.

## Where this applies in LineOps

The desk's tokens, surfaces and button emphases are the apple-mudblazor skill's
(`--surface-*`, `--text-*`, `--space-*`, Plain/Tinted/Filled). This skill decides what is
drawn with them. The wager window (`BoardWager.razor`) is the reference implementation of
`references/forms.md`: a grouped form in three steps, no outlines, a segmented control per
market, label column leading and controls trailing, footers under groups. Any new form or
wizard in this repository or a sibling copies that shape rather than inventing one.

## When not to bend it

The HIG is written for Apple platforms; this desk is a browser app on Windows and Mac. Keep
macOS's density (13pt body, 28–36pt rows) rather than iOS's (17pt, 44pt rows) because the
operator sits at a desk with a pointer, but keep iOS's grouped-list *shape* because a
browser has no NSBox and the fill-and-inset-separator form reads correctly in both themes.
