# The type face

## Why there is a font to download at all

Apple's system face is **SF Pro**. It cannot be licensed for use off Apple
platforms — the licence permits it in apps for Apple operating systems, not on a
web page a Windows or Linux browser will load. So the stack is split:

- On macOS, iOS and iPadOS, `-apple-system` / `BlinkMacSystemFont` resolve to the
  real SF at no download cost. That is the correct, licensed way to get SF.
- Everywhere else, a self-hosted **Inter** is the closest legal match on metrics —
  same humanist-grotesque skeleton, near-identical cap height and x-height, so a
  layout tuned on a Mac does not reflow on Windows.

Without the Inter fallback, Windows falls through to Segoe UI, which is narrower
and lighter and makes the whole interface look like a different product on half
your machines.

Inter is licensed **SIL Open Font License 1.1**, which permits bundling and
self-hosting. Keep the OFL text alongside the font files.

The font binaries are deliberately **not** shipped inside this skill — a skill
carrying a megabyte of woff2 is a skill nobody wants to sync. Download them.

## Get the files

Two variable-font files cover every weight the type ramp asks for, so it is one
request per style rather than four per weight.

```bash
mkdir -p wwwroot/fonts
curl -L -o wwwroot/fonts/InterVariable.woff2 https://github.com/rsms/inter/raw/master/docs/font-files/InterVariable.woff2
curl -L -o wwwroot/fonts/InterVariable-Italic.woff2 https://github.com/rsms/inter/raw/master/docs/font-files/InterVariable-Italic.woff2
```

Roughly 350 KB and 390 KB respectively. If those URLs 404, take the official
release zip from <https://github.com/rsms/inter/releases/latest> and extract
`InterVariable.woff2` and `InterVariable-Italic.woff2` from `web/`.

Do not substitute a different family. The stack below depends on Inter's metrics
matching SF closely; a substitute that does not will shift every measured layout.

## The `@font-face` block

Already present in `apple-tokens.css`. Repeated here so this file is complete:

```css
@font-face {
    font-family: 'Inter var';
    font-style: normal;
    font-weight: 100 900;
    font-display: swap;
    src: url('../fonts/InterVariable.woff2') format('woff2');
}

@font-face {
    font-family: 'Inter var';
    font-style: italic;
    font-weight: 100 900;
    font-display: swap;
    src: url('../fonts/InterVariable-Italic.woff2') format('woff2');
}
```

`font-weight: 100 900` declares the variable axis range, which is what lets one
file answer every weight in the ramp. `font-display: swap` renders in the
fallback immediately and swaps when the file lands, rather than holding the page
blank.

## The stack

```css
--face-ui: -apple-system, BlinkMacSystemFont, 'Inter var', 'Inter', 'Segoe UI', system-ui, sans-serif;
```

Order matters: real SF first where it exists, the bundled variable Inter next,
static Inter for anyone who has it installed, then the platform fallbacks.

**One family for everything.** Apple sets numbers in SF too, so there is no
second "data" or "mono" face to name. A column of figures is this same face plus
`font-variant-numeric: tabular-nums` — which is what `.num` and `.mono` set in
`apple-tokens.css`, and what the bridge sets on every table cell.

## If you cannot bundle a font

Delete the two `@font-face` blocks and the two `'Inter'` entries from
`--face-ui`. Everything still works; non-Apple platforms simply render in
`Segoe UI` / `system-ui`. Re-check any layout you tuned by eye afterwards.
