---
name: apple-hig-components
description: Use when designing, building or reviewing any window, form, wizard or control against Apple's Human Interface Guidelines — which component the job calls for, how it is grouped (one hairline per group, never nested), labelled, sized and coloured, and the audit before "done". Component judgment only; apple-mudblazor is how the Desk draws it, anthropic-skills:apple-hig-web is patterns and the live HIG.
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
   one before fails. The house rule (October 2026, after a borderless pass was rejected) is
   that **a group or card is a fill with one 1px hairline edge** — macOS System Settings
   draws its grouped forms this way, and the HIG's Boxes page allows "a visible border or
   background color". What it forbids is the box inside the box: "adding nested boxes to
   define subgroups can make the interface feel busy and constrained". Rows inside a group
   are divided by **inset separators** and padding, never framed again; a card may hold
   groups and a group holds rows, and nothing below that carries an edge.

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

## Three skills, three jobs

- **This skill** decides *what is drawn*: which control the job calls for, how it is
  grouped and labelled, and the audit before "done".
- **apple-mudblazor** is *how it is drawn here*: the Desk's tokens (`--surface-*`,
  `--text-*`, `--space-*`), the `Desk*` primitives, Plain/Tinted/Filled, and the MudBlazor
  seam. Build with it; never import a second token sheet beside it.
- **anthropic-skills:apple-hig-web** (plugin) is the *patterns* layer this skill does not
  carry — navigation, loading, feedback, searching, settings, undo — plus reading any HIG
  page as Markdown by appending `.md` to its URL, and a review checklist worth running on a
  whole window. Its `hig-tokens.css`, `hig-components.css` and `hig.js` are **not** used in
  this repository and its `--color-*` names are not the Desk's. Take its judgment, not its
  files.

## The shape to copy

In any application on the Desk, the **Parts bin** window is the live reference: its Anatomy,
Form group, Steps, Key row and Inline confirm sections draw `references/forms.md` and
`references/wizards.md` with the Desk's own primitives — `FormGroup` and `FormRow` for a grouped
form (label column leading, controls trailing, the footer under the group, one hairline per
group), `DeskSteps` for a wizard, `KeyRow` for the keys and the reason the Filled one waits.
Build a new form or wizard from those primitives rather than from class names, and it lands in
the shape.

In the Desk's own repository, the LineOps wager window (`BoardWager.razor`) is the worked
example the references cite: a grouped form in three steps, a segmented control per market,
each group a fill with one hairline.

## When not to bend it

The HIG is written for Apple platforms; this desk is a browser app on Windows and Mac. Keep
macOS's density (13pt body, 28–36pt rows) rather than iOS's (17pt, 44pt rows) because the
operator sits at a desk with a pointer, but keep iOS's grouped-list *shape* because a
browser has no NSBox and the fill-and-inset-separator form reads correctly in both themes.
