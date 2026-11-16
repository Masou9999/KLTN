# Main menu profile frames

These are lossless copies of the seven supplied `Completed` PNGs. Source file 5
(`5 - Platinum - Cleaned.png`) maps to Diamond; file 4 maps to Platinum.
The originals in Downloads are unchanged.

Normalization happens in Unity, not by resampling the PNG canvas. Each entry in
`Assets/Resources/ProfileFrameCatalog.asset` records a safe information rectangle
in source pixels, measured from the top left. `ProfileFrameCatalog.Apply` maps it
to the same 360 × 100 UI-unit rectangle at (275, 85) within ProfileGroup.

The left/right sprite borders protect the avatar ornament and end cap. Height
sets a uniform scale for those ornaments; only the middle horizontal strip is
stretched to fill the information width. All textures use FullRect sprite meshes,
100 PPU, no mipmaps, no compression, and no non-power-of-two resizing. Do not call
SetNativeSize or change the frame Image to Simple: that bypasses normalization.

The previous season's final points select the tier through RankPolicy. There is
no new Firestore field or client write for cosmetic frames. Season 1, no active
season, or a missing previous-season row use Bronze. Read errors retain the last
confirmed frame for the same signed-in account. Avatar loading is not implemented;
the old placeholder AvatarFrame object is disabled.

The scene and catalog are already wired. Optional editor commands:

- `Tools > KLTN > Install Main Menu Profile` rebuilds the same setup.
- Batch execute `KLTN.Game.Editor.MainMenuProfileInstaller.ValidateAndPreview`
  validates references and writes seven scene previews under `Logs/MenuPolish`.

Validation during implementation: .NET compilation of runtime/editor code and
the standalone service checks passed. Unity batch rendering was unavailable
because the local editor reported no valid license. Live authenticated Firestore
and Play Mode rendering still need an integration check in a licensed editor.
