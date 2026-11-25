# Credits scene

Open `Assets/Scenes/CreditsScene.unity`. This is an authored, editable uGUI scene.
MainMenu's former Quit button now opens Credits. Back returns to MainMenu using
the same return sprite as FindMatch. Menu music and click audio use the existing
persistent services when entering from MainMenu.

- `Background.png` is an unchanged copy of the user's supplied Credits background.
- `CreditsIcons.png` was derived from the supplied `Assets.png` with built-in
  imagegen to isolate the three badges on transparency. It is a reconstructed
  variant, not a pixel-exact crop. Sprite import rectangles separate the badges.
- The logo, lotus, frame, corners, material and Vietnamese body font reuse Login assets.
- Cinzel Bold is a static weight-700 instance of the Google Fonts Cinzel variable
  font. Its OFL license is included in `Fonts/OFL.txt`.
  Source: https://github.com/google/fonts/tree/main/ofl/cinzel
- The background fits the viewport, adds overscan and drifts slowly. Select
  `BackgroundViewport/Background` to toggle motion or adjust amplitude/period.
- Change the text and layout directly in the scene. The one-time builder refuses
  to overwrite an existing CreditsScene.

## Verification (2026-11-25)

Changed C# compiled against the installed Unity 6000.5.4f1 assemblies. Serialized
scene references, asset GUIDs, original background bytes, menu callback and client
build lists were checked outside Unity.

Unity batch and normal command-line startup both failed with “No valid Unity
Editor license found”. Unity import, rendering and Play Mode are **not verified**.
After licensing is restored, run `Tools/KLTN/Validate Credits Scene` to check
bindings/text overflow and render four resolutions to `Logs/Credits/`. Then check
MainMenu → Credits → Back, hover/click feedback, continuous menu music and drift
in Play Mode. Opening Credits directly does not create persistent audio services.
