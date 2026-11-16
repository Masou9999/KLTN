# Audio settings regression checks

Run `dotnet run --project Tools/AudioSettingsCheck/AudioSettingsCheck.csproj` from the repository root (.NET 10).

Compiles the actual AudioVolumeSettings and AudioPreferences sources with in-memory Unity preferences and a controllable cloud gateway. Checks account isolation, guest separation, snapping, dirty state, save failures, edits during save, cached fallback, and stale save/load replies.

Firestore authorization is tested separately with `npm test` in `Firebase/`.

Production prerequisite: authenticate Firebase CLI (`npx firebase login`), then deploy the reviewed rules with `npx firebase deploy --only firestore:rules --project unity-kltn` from `Firebase/`. Do not point the emulator tests at production.


## Inline audio icons (16 November 2026)

`python Tools/AudioSettingsCheck/validate_assets.py` checks both authored panels,
sprite pairs, channel wiring, removed labels, slider dimensions and local references.
The audio harness also covers combat mute/unmute, restoration of the selected
volume, preview rollback, account separation and legacy documents.

The optional `combatMuted` boolean preserves the selected combat volume while
muting. Missing flags in old documents/cache default to false; combat volume zero
is always effectively muted. Deploy `Firebase/firestore.rules` before using the new
client's audio Save. The rules continue to accept the original five-field payload;
an older client replacing the document can remove the new flag.

Implementation verification: 31 harness checks passed using the installed .NET 9
SDK with a temporary project linking the same production and test sources (the
checked-in project targets .NET 10). Runtime and Editor compiled against the
project's Unity references; an existing CS4014 in LoginStateCheckingController
remains unrelated. Static scene/sorting checks passed. Emulator execution was
blocked by missing Java, and Play Mode visuals were not verified. No desktop UI
automation or production deployment is required by these checks.


The scene validator now also verifies the shared 900×620 frame, stretched surface
and all four border anchors, authored Vietnamese Save/Close buttons and callbacks,
and the three authored resolution choices. Runtime initialization reuses these
objects; the Editor installer uses the same initialization and layout routines.
