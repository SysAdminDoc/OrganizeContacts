# OrganizeContacts — Logo Prompts

Five candidate prompts for ChatGPT / DALL-E. Pick one or remix. All output specs end with the standard transparent-PNG (RGBA) requirements per global CLAUDE.md.

---

## 1. Minimal — abstract address-book glyph
```
A minimalist app icon: a single stylized address-book or rolodex glyph rendered
with a clean two-tone duotone in Catppuccin Mocha purple (#CBA6F7) and blue
(#89B4FA). Subtle inner stroke, no text, no shadow. Centered on a 1:1 canvas.
Geometric, balanced, recognizable at 32×32. Modern flat design with one
diagonal accent line suggesting "deduplication."

Final output: 1024x1024 PNG, RGBA, true transparent background, alpha channel
enabled, no checkerboard, no solid background, no watermark, no text, only the
main icon visible.
```

## 2. App icon — overlapping cards
```
Square app icon: two contact cards overlapping at a slight angle, the bottom
card faded to ~40% opacity to suggest "the duplicate," the top card crisp and
in focus. Top card uses Catppuccin Mocha purple/blue gradient; bottom card uses
muted Surface1 gray. A small accent dot or check mark in the top-right corner
in Mocha green (#A6E3A1). Rounded corners (~22% radius), no text, soft inner
shadow on the top card only.

Final output: 1024x1024 PNG, RGBA, true transparent background, alpha channel
enabled, no checkerboard, no solid background, no watermark, no text, only the
main icon visible.
```

## 3. Wordmark — clean type lockup
```
A horizontal wordmark for "OrganizeContacts." Modern geometric sans-serif
(Inter / Geist Sans aesthetic), tight letter-spacing, the word "Organize" in
Catppuccin Mocha text color (#CDD6F4), the word "Contacts" in Mocha purple
(#CBA6F7). A subtle separator dot or vertical bar between the two words in
Mocha blue (#89B4FA). Optical baseline alignment, no extra graphics, no
descender flourishes.

Final output: 2048x512 PNG, RGBA, true transparent background, alpha channel
enabled, no checkerboard, no solid background, no watermark, only the wordmark
text visible.
```

## 4. Emblem — circular crest with merged contacts
```
A circular emblem logo. Inside the circle: two abstract contact silhouettes
(simple head-and-shoulders glyphs) overlapping such that they merge into a
single shared outline at the bottom — suggesting deduplication and unity. The
overlapping region is rendered in Catppuccin Mocha mauve (#CBA6F7); the
non-overlapping portions are in two complementary blues (#89B4FA and #74C7EC).
Thin outer ring border, otherwise flat. No text, no inner glow.

Final output: 1024x1024 PNG, RGBA, true transparent background, alpha channel
enabled, no checkerboard, no solid background, no watermark, only the emblem
visible.
```

## 5. Abstract — folded paper / envelope hybrid
```
An abstract icon that fuses an opened envelope with a folded contact card.
Geometric origami-style folds visible. Color palette: Catppuccin Mocha base
(#1E1E2E) for the deepest fold, surface1 (#45475A) for the mid-fold, mauve
(#CBA6F7) for the top facing surface, blue (#89B4FA) for a single highlight
edge. A small dot pattern in Mocha sky (#89DCEB) suggests contact entries on
the card surface. Modern, clean, slightly isometric perspective. No text.

Final output: 1024x1024 PNG, RGBA, true transparent background, alpha channel
enabled, no checkerboard, no solid background, no watermark, only the icon
visible.
```

---

## Integration Checklist (after picking final logo)

- [ ] Save final 1024×1024 master to `branding/logo-master.png`.
- [ ] Export `branding/logo.png` (square, 512×512) → root + README reference.
- [ ] Export `branding/banner.png` (wide, 1280×400) → root + README reference.
- [ ] Generate `.ico` for WPF window (`logo.ico`, multi-resolution 16/32/48/64/128/256).
- [ ] Reference the `.ico` in `OrganizeContacts.App.csproj` via `<ApplicationIcon>`.
- [ ] Add favicon if a project page is published later.
- [ ] Update README badges section to include the banner at the top.
- [ ] Verify alpha channel: open the saved PNG over a colored background and
      confirm no checkerboard / solid bg leaks through.
