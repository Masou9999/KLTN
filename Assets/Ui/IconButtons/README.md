# Icon buttons

Round icon buttons in MainMenu and GameScene share one border: the GameScene icon ring
("ingame - SETTING.png", supplied again as a 90 × 91 reference): grey `#A59998` edge
11 % of the radius over a black disc.

- `IconRing.png` (256 px): the ring alone; MainMenu Logout draws `LogoutIcon` over it.
- `Store.png`, `Leaderboard.png` (256 px): the cream glyphs of
  `Assets/ConMaConMa/UI/Menu/Store.png` / `Leaderboard.png`, resampled from their 122 px
  disc into the new 114 px disc and placed over the ring. Generated with Pillow
  (4× supersampled ellipses, bicubic resample); the originals are unchanged.
- MainMenu shortcut `Artwork` images now fill the button (the old 6.5 % overscan that
  hid the opaque square corners is no longer needed).
- `MatchLogIcon` (GameScene log button, drawn procedurally) uses the same radius,
  ring ratio and colour.
