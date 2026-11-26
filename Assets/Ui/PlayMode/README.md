# PlayMode modal

Sprites for the MainMenu "Chọn chế độ" modal. Build with
`Tools/KLTN/Install Play Mode Modal` (`Assets/Editor/KLTN/PlayModeModalInstaller.cs`);
`Tools/KLTN/Install Menu Polish` calls the same builder.

## Assets

- All PNGs except `mode_border.png` were supplied pre-extracted (transparent) from the
  design reference `Mode/Sample and Asset.png`. The lotus, sparks and clouds were
  intentionally left out.
- `mode_border.png` is derived from `mode_background.png`: pixels with `R > 140` and
  `R - G > 50` (the coral outline) are kept as white with their original alpha, all
  others are transparent. It is drawn over `mode_background` and tinted so the border
  colour can change on hover without darkening the card fill.
- `mode_hover_glow.png` is generated from the `mode_background` alpha inside the border
  with a radial falloff (centre 50% / 62%); tinted coral and faded in on hover beneath
  the border and artwork.
- `modal_background.png` is imported with a 48 px sprite border and drawn Sliced.

## Layout (1920 × 1080 reference)

- Frame 1300 × 780 drawn at scale 1.2 (1560 × 936 on screen), four mirrored `corner.png`, no lotus.
- Cards 480 × 446 at x = ±290 (label plate 340 × 118, text 34); hover scales the card's `Visual` child to 1.1 and
  tints `CardBorder` (`PlayModeOptionHover`). The button hit area stays 1.0.
- Close button 80 × 80 centred on the frame's top-right corner (half outside).
