# Ability Animation Sandbox (temporary, Editor only)

Open `AbilityAnimationSandbox.unity` in this folder and press Play. No login,
UGS session, network host, opponent client, or build is required.

- Both reserves start with two Ma Lon and one Vong Nhi; both hands are empty.
- Your mana stays at 10, including while staging a summon. Only your side is
  controlled. There is one continuous round, with no pass or attack declaration.
- **THÊM THẺ** next to Settings opens a count editor for your side. Minus removes
  cards; plus adds cards. **XÁC NHẬN** applies the draft; **HỦY** discards it.
- Reserve capacity is 4 (including Board); this sandbox limits Draw Hand to 10.
  Ma Lon and Vong Nhi can be added directly to Reserve. Test units enter via
  Draw Hand; once summoned, they appear in the Reserve section for removal.
- Removing cards is fixture editing, not killing: it does not trigger Vong Nhi,
  Ma Lon, or the allied-death condition for Ma Tranh. Existing cards retain their
  stats and trigger history. If multiple copies exist, minus removes the newest.
- Drag a hand card to Reserve, press **TRIỆU HỒI**, choose targets, then confirm.
  The normal rules generate effects, and the normal animation director plays them.
- **RESET** also works during animation and restores cards, Nexus health, mana,
  deck, selection and ability history. It does not reset audio preferences.
- For Ma Tranh, first sacrifice an ally in this round. With a full Reserve,
  remove a unit through the modal to make room. Reset replenishes enemy targets.
- Draw effects draw from a deterministic 25-card deck of test units. Any draws
  beyond the sandbox hand limit are returned to the bottom of that local deck.

## Maintenance

`Tools > KLTN > Temporary > Build Ability Animation Sandbox` regenerates this
scene from GameScene, replacing sandbox scene customizations. GameScene and
Build Profiles are not modified. All sandbox classes are guarded by `UNITY_EDITOR`.

`Tools > KLTN > Temporary > Validate Sandbox Model` checks fixture limits,
ability chains, cancellation, reset and mana. Batch Play Mode validation entry:
`KLTN.Sandbox.Editor.AbilitySandboxValidation.Run` (without `-quit`). This exits
the Editor on completion; output is in `Logs/AbilitySandbox/result.txt`.

To remove the sandbox, delete this folder and its `.meta`. The three small
`UNITY_EDITOR` hooks in `MatchDebugControls.cs`, `.Submission.cs`, and
`.AbilitySelection.cs` can also be removed, but are inert without a harness and
have no dependency on this folder. No production Domain/network rules changed.
