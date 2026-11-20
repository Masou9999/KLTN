# Host / Client menu

The editable layout is authored in `Assets/Scenes/HostClientMenu.unity` on a
1920 × 1080 design surface. CanvasScaler uses Expand to keep the controls visible
at narrower and wider aspect ratios. The overlay stretches over the entire canvas.

- `controls.png` is the supplied `asset.png`, unchanged. Named Sprite Editor
  rectangles extract the buttons, input frame, room-code frame, copy icon and divider.
- `modal-paper.png` is an edited version of the supplied panel with the upper-left
  and lower-right flowering branches removed. The transparent exterior is retained.
- `checked.png` is the supplied confirmation icon, unchanged.
- Back references the existing `ConMaConMa/UI/icon/ingame - RETURN.png` sprite.
- Patrick Hand is bundled under SIL OFL 1.1; see `Fonts/OFL.txt`. Source:
  https://github.com/google/fonts/tree/main/ofl/patrickhand
  The dynamic TMP atlas includes Vietnamese. Room codes use the existing
  Be Vietnam Pro SemiBold font to keep alphanumeric codes clear.

`HostClientMenuView` owns the dialog, focus, copy feedback and control availability.
`ListenSessionController` owns session operations. All button listeners are wired
once by the view and removed on disable; do not add duplicate persistent listeners.
`PaperButtonVisual` uses separate art layers so the hover rays do not move the label.

Run **Tools → KLTN → Validate Host Client Menu**, or batch execute
`KLTN.Editor.HostClientMenuValidation.Run`. It opens an isolated preview scene,
validates assets and local interactions, and writes results/renders to
`Logs/HostClient/`. It does not open a UGS session, enter Play Mode, change the
clipboard, or save the user's open scenes. Two-instance Relay testing and actual
clipboard interaction remain separate integration checks.
