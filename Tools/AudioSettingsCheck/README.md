# Audio settings regression checks

Run `dotnet run --project Tools/AudioSettingsCheck/AudioSettingsCheck.csproj` from the repository root (.NET 10).

Compiles the actual AudioVolumeSettings and AudioPreferences sources with in-memory Unity preferences and a controllable cloud gateway. Checks account isolation, guest separation, snapping, dirty state, save failures, edits during save, cached fallback, and stale save/load replies.

Firestore authorization is tested separately with `npm test` in `Firebase/`.

Production prerequisite: authenticate Firebase CLI (`npx firebase login`), then deploy the reviewed rules with `npx firebase deploy --only firestore:rules --project unity-kltn` from `Firebase/`. Do not point the emulator tests at production.
