# Ability Animation Sandbox (temporary, Editor only)

## Quỷ Cẩu adjacent Support flames

Production loads `Assets/Resources/VFX/QuyCauFlame.asset` automatically. The original
4 x 6 atlas uses frames 1–4 for intro, 5–16 for the loop, and 17–24 for outro at
110 ms/frame. A single shared looping SFX source respects the SFX volume setting.
The marker indicates the adjacent right slot, including local draft placement;
it neither applies nor revokes the existing round-long Support stat modifier.

`KLTN.Sandbox.QuyCauFlameValidationRunner.RunBatch` is a separate batch Play Mode
entry (without `-quit`). It constructs an unsaved isolated scene with the real
card prefab, draft controls, board presenter and combat animator. It does not
overwrite GameScene or this sandbox. Results: `Logs/QuyCauFlame/result.txt`;
capture: `Logs/QuyCauFlame/flame-board.png`. It checks slot/card anchors, sorting,
snapshot rebuild, intro/outro, spam, both sides, combat/ability death hooks,
survivor return, shared audio and reset. It is not a two-client network test.

2026-11-22: runtime/Editor .NET compilation passed against the local Unity
references (two existing CS0649 warnings in MatchServerModuleClient). Standalone
playback tests passed 52,229 assertions including 50,000 random toggles; asset
validation passed. Unity Play Mode execution was attempted but blocked before
import by missing Editor license (exit 198). The Play Mode fixture and human
visual/audio review have **not** passed yet.

## Ma Co and Linh Mieu shared resurrection

Both cards are available in the count editor without rebuilding the scene.
Record an allied death (the Ma Tranh preview provides one), make room in Reserve,
then add and summon Ma Co or Linh Mieu. The source moves from Reserve to center,
plays the Thien Linh Cai spirit ribbon/healing sound, then returns to Reserve.
Linh Mieu revives into Reserve. Ma Co reconstructs a new copy in Draw Hand;
the opponent receives only a card-back animation, never the copied identity.
The copy does not also fly in from the deck when the final snapshot arrives.

Batch Play Mode entry (no `-quit`):
`KLTN.Sandbox.Editor.SharedReviveValidation.RunBatch`.
Results: `Logs/SharedRevive/result.txt`. Covers both cards/viewers, hidden copy
projection, source centered during VFX, final zones, duplicate draw suppression,
and reset during the spirit stream. This is an offline real-pipeline fixture,
not a two-process network test.

Verified 2026-11-20: Unity 6000.5.4f1 batch Play Mode passed all eight scenarios;
259 Domain NUnit cases passed through the .NET harness. Runtime/Editor compilation
passed. No separate network-client run or human visual/audio review was performed.

Open `AbilityAnimationSandbox.unity` in this folder and press Play. No login,
UGS session, network host, opponent client, or build is required.

- Both reserves start with two Ma Lon and one Vong Nhi; both hands are empty.
- Your mana stays at 10, including while staging a summon. Only your side is
  controlled. There is one continuous round, with no pass or attack declaration.
- **THÊM THẺ** next to Settings opens a count editor for your side. Minus removes
  cards; plus adds cards. **XÁC NHẬN** applies the draft; **HỦY** discards it.
- Reserve capacity is 4 (including Board); this sandbox limits Draw Hand to 10.
  Ma Lon, Vong Nhi and Ma Da can be added directly to Reserve. Other test units enter via
  Draw Hand; once summoned, they appear in the Reserve section for removal.
- Removing cards is fixture editing, not killing: it does not trigger Vong Nhi,
  Ma Lon, or the allied-death condition for Ma Tranh. Existing cards retain their
  stats and trigger history. If multiple copies exist, minus removes the newest.
- Drag a hand card to Reserve, press **TRIỆU HỒI**, choose targets, then confirm.
  The normal rules generate effects, and the normal animation director plays them.
- **RESET** also works during animation and restores cards, Nexus health, mana,
  deck, selection and ability history. It does not reset audio preferences.
- **TEST MA TRÀNH** next to RESET prepares a fresh fixture with one allied death
  recorded in the previous round and Ma Trành already in hand. Drag it to Reserve and press
  **TRIỆU HỒI** to see the bite. Press the shortcut again to prepare another test.
  RESET restores the ordinary fixture with no allied deaths. This shortcut only
  exists in the Editor sandbox and does not change production ability rules.
- Ma Trành only triggers when an ally died in the previous round. The sandbox has
  no round transition, so use **TEST MA TRÀNH**; a death in the current sandbox
  round does not trigger it. Reset replenishes enemy targets.
- Draw effects draw from a deterministic 25-card deck of test units. Any draws
  beyond the sandbox hand limit are returned to the bottom of that local deck.

### Ma Da

Existing scenes add the Ma Da rows automatically in Play Mode; no rebuild is needed.
In **THÊM THẺ**, remove one Ma Lon, add one Ma Da to Reserve, and add one Thien Linh
Cai to Draw Hand. Confirm, summon Thien Linh Cai, and select Ma Da: its water death
plays before revival. The dead Ma Da's Last Breath remains recorded independently.
The minus button only edits the fixture; it does not play death or record Last Breath.
This ability-only sandbox still has no attack declaration; direct Nexus/combat tests
must be performed in GameScene.

## Maintenance

`Tools > KLTN > Temporary > Build Ability Animation Sandbox` regenerates this
scene from GameScene, replacing sandbox scene customizations. GameScene and
Build Profiles are not modified. All sandbox classes are guarded by `UNITY_EDITOR`.

`Tools > KLTN > Temporary > Validate Sandbox Model` checks fixture limits,
ability chains, cancellation, reset and mana. Batch Play Mode validation entry:
`KLTN.Sandbox.Editor.AbilitySandboxValidation.Run` (without `-quit`). This exits
the Editor on completion; output is in `Logs/AbilitySandbox/result.txt`.

Thần Trùng uses `Assets/Resources/VFX/ThanTrungLightning.asset` for its hand-painted
atlas, timing, width and strike SFX. The normal director loads this profile in both
GameScene and the sandbox; rebuilding the sandbox does not lose the effect.
The SFX start offset skips the supplied clip's 0.2-second quiet lead-in.
Dedicated batch Play Mode validation entry (without `-quit`):
`KLTN.Sandbox.Editor.ThanTrungLightningValidation.Run`. It checks both strikes,
death-trigger chains, SFX gain and reset during charge/impact. Results are written
to `Logs/ThanTrungLightning/result.txt`, with impact captures in `Logs/MenuPolish`.

Ma Trành uses `Assets/Resources/VFX/MaTranhBite.asset`. The original supplied sheet
is sampled through nine pixel rectangles (frames 1–9 only, excluding the labels).
`frameMilliseconds` controls the individual holds; frame 7 triggers the bite SFX,
and frame 9 starts one expanding, fading copy behind the held jaw. The recording's
0.45-second quiet lead-in is skipped during playback. No projectile is spawned.
Batch validation entry: `KLTN.Sandbox.Editor.MaTranhBiteValidation.Run` (no `-quit`).
For an already-open Editor, create `Logs/MaTranhBite/request.txt`; validation waits
for a saved, idle sandbox scene, runs Play Mode, then returns to Edit Mode without
closing Unity or saving scene changes. Results: `Logs/MaTranhBite/result.txt`.
Cases cover two/one/zero enemies, an unmet death condition, and reset during the
opening and final-frame echo. Captures are written to `Logs/MenuPolish/ma-tranh-*`.

To remove the sandbox, delete this folder and its `.meta`. The three small
`UNITY_EDITOR` hooks in `MatchDebugControls.cs`, `.Submission.cs`, and
`.AbilitySelection.cs` can also be removed, but are inert without a harness and
have no dependency on this folder. Production ability rules and VFX remain in place.

Ông Kẹ uses `Assets/Resources/VFX/OngKeBonk.asset`. A random one of three wood
sticks swings locally beside the selected allied card and contacts its top edge
at 400 ms. The full whoosh/hit recording starts with the swing sequence. BONK
pops at contact and fades by 770 ms, followed by existing death feedback. Grip
and tip coordinates are calibrated per atlas cell; the swing mirrors at the
right canvas edge. Production kill/draw rules are unchanged.

Manual preview: add Ông Kẹ to hand with the sandbox card modal, drag to Reserve,
press TRIỆU HỒI, select an allied card, and confirm. No prior allied death is
required. Repeat after RESET to sample different sticks.
While the sandbox is in Play Mode, run
`Tools > KLTN > Temporary > Validate Ong Ke Bonk (Play Mode)` for live regression
checks. It resets the local fixture, tests three target positions and two resets
(windup/impact), and writes `Logs/OngKeBonk/result.txt`. It never opens/saves scenes
or exits Unity. Visually check all three sticks, Board/Reserve positions, canvas
edges and the sound-to-contact alignment separately.

Ma Đói uses `Assets/Resources/VFX/MaDoiFeast.asset`. Its confirmed sacrifice
shrinks the target beneath a 24-frame smoke ring (6x4, original sheet), reveals
a chicken, then winds up and flies along an accelerating arc to the source.
The source leans right during windup/flight. The chicken disappears at contact;
the blocking animation ends after a 200 ms hold. Chewing starts 300 ms after
contact at pitch 1.5 on an independent audio source, without delaying Reserve
placement or input. Snapshot cleanup preserves chewing; reset/disable stops it. Pop/swish/chew silence is skipped at
runtime, leaving original audio files intact. The generic lethal preview is
cleared at transformation, and shattering is replaced for this ability only.
The target stays hidden until the authoritative snapshot; death triggers still
replay normally. Reset restores transforms/visibility and stops all feast audio.

Manual preview: add Ma Đói to hand, drag to Reserve, summon, choose an ally and
confirm. In Play Mode, `Tools > KLTN > Temporary > Validate Ma Doi Feast (Play Mode)`
checks three target positions and resets during transform/flight/chew. Results
are written to `Logs/MaDoiFeast/result.txt`. It only changes the offline test
fixture; it does not open/save scenes or exit Unity. Also visually inspect Board
targets, source lean, smoke frame alignment and sound timing.

Ma Càng Sùng is now available in the hand selector (existing scenes upgrade in
memory). Summon it, then summon Ma Đói/Ông Kẹ and sacrifice another ally. It drains
the enemy Nexus and heals its owner's Nexus; at full health no false +1 is shown.
The profile `Assets/Resources/VFX/MaCangSungBats.asset` controls the two-second
12-bat swarm (6+4+2), surround/dispersal/arrival timing and sound gains. The four
original poses animate at 16 fps. Damage/heal popups use confirmed health deltas;
Nexus heal events are now recorded by the shared Domain executor, without changing
healing rules. Rebuild/deploy the updated server with clients for online use.

Run `Tools > KLTN > Temporary > Validate Ma Cang Sung Bats (Play Mode)` in the
sandbox to test real drain/heal, full-health suppression, 12 bats, phase timing
and reset during biting/return. Output: `Logs/MaCangSungBats/result.txt`. This
prepares fixtures and never saves scenes. Inspect both viewer perspectives and
HUD coverage visually as well.

Thiên Linh Cái uses `Assets/Resources/VFX/ThienLinhCaiSpirit.asset`. Charge plays
the five nonempty blue-flame cells at the source card center. Projectile plays
the 12 supplied poses with per-frame heading/pivot calibration. Impact plays
the four-cell blue-gray explosion; no aftermath flame/embers remain. Original
SFX are preserved. Kill/death/revive gameplay order stays authoritative.

Revive follows Healing state 1-4: one continuous textured path leaves the source,
bends into a clockwise orbit, then the finite tail detaches and flows into that
same orbit. Source feed duration is derived from path/circumference lengths.
Front/back ribbons use the same path, only splitting occlusion around the card.
The existing reconstruction begins during coiling. No disconnected ring sprite
is spawned. Sound tails do not block final projection; reset stops them.

Run `Tools > KLTN > Temporary > Validate Thien Linh Cai Spirit (Play Mode)`. It
checks centered charge, populated charge cells, sprite explosion, no aftermath,
kill/revive, source cutoff/tail drain, reset during charge/stream/tail and sound
cleanup. Results: `Logs/ThienLinhCaiSpirit/result.txt`. Visually inspect frame
alignment, continuous stream-to-circle motion and both viewer perspectives.

### Ma Gà and Ma Trơi

Both cards are available in **THÊM THẺ** → Draw Hand, including existing scenes
without rebuilding. Ma Gà reduces another allied hand card's mana: add Ma Gà and
Ma Trơi, summon Ma Gà and select Ma Trơi. The supplied 24-frame blue swirl plays
at the target's mana badge, without a projectile; the digit changes at frame 20.
With no other hand card, Ma Gà summons without an effect. Cancel preserves mana
and costs; a target already at zero does not play a false reduction.

For Ma Trơi, add Ma Trơi and Ma Đói to hand, then summon Ma Đói and sacrifice a
Ma Lợn. While Ma Trơi remains in DrawHand, each actual allied death triggers the
same badge effect and SFX, plus a smooth 1 → 1.3 → 1 card pulse over 280 ms. The
original scale is restored. Reserve, Board, Deck and Graveyard do not receive
this discount or animation. Drawing Ma Trơi later includes prior allied deaths
in its hand cost without replaying old effects. Enemy deaths do not reduce it.

`Assets/Resources/VFX/ManaReduction.asset` holds the shared atlas, 50 ms frame
timing, badge width (40.3% of card width, increased by 30%), frame-20 cost switch and original SFX.
Audio tails continue after the 1.2-second visual and stop on reset/disable.
Original PNG/MP3 bytes are preserved. Client and server must both be updated
for the new captured cost events and DrawHand-only passive rule.

In Play Mode, run **Tools > KLTN > Temporary > Validate Mana Reduction (Play Mode)**.
It exercises the real director, before/after digits, badge anchor, pulse/scale
restoration, no projectile, audio tail and reset during early/late frames. It
also checks owner/opponent DTO and journal privacy using actual card resources.
Batch entry (without `-quit`):
`KLTN.Sandbox.Editor.ManaReductionValidation.RunBatch`. Batch opens only this
offline scene and exits on completion; the Play Mode menu keeps Unity running.
Neither path saves or rebuilds scenes. Output: `Logs/ManaReduction/result.txt`.
These runtime checks were added but could not be executed on 19 November 2026:
Unity batch returned 198 because no valid Editor license was available. Domain
and pure projection checks were executed separately; visual/audio inspection
and host/guest Play Mode checks remain to be run in a licensed Editor.

### Source staging and Host/Client timing

Ma Trành now moves an existing local Reserve visual to the same center used by
the newly revealed opponent visual, after the authoritative summon resolves.
Preparation preserves the actual position, rotation and world size. A source
already centered for target selection does not fly again. One preparation is
shared by consecutive effects of the same ability/source; return to Reserve
uses the final authoritative order after the last effect.

Newly revealed opponent sources start at the consumed hand back in Draw, preserving
its world position, size and rotation, then move to center, play the skill and return
to Reserve. If the back is unavailable, they start at the opponent hand root. Existing
Reserve visuals and already-centered selection sources retain their current pose.
The staging validator also checks Draw origin, exactly one consumed back, repeated
preparation, missing-back fallback and existing opponent Reserve sources. These new
checks are compile-verified only: the 19 November 2026 batch attempt still exits 198
without a valid Unity Editor license. Verify the actual commit/target-selection flow
from both Host and Client perspectives in a licensed Editor before signing off visuals.

Host-local match update RPC delivery is deferred using NGO `DeferLocal`. This
prevents local animation setup from running inside the server's send loop before
the remaining viewer update has been sent. It does not synchronize VFX clocks or
prove that the reported full-duration delay is resolved. Two-Editor Host/Client
verification is still required; Unity batch remains blocked by license exit 198.

Run **Tools > KLTN > Temporary > Validate Ability Source Staging (Play Mode)**
in the offline sandbox. It tests both viewer perspectives with three, one and
zero enemy targets (at most two bites), source pose preservation, one staging,
unchanged target parent, and final Reserve placement. Batch entry without
`-quit`: `KLTN.Sandbox.Editor.AbilitySourceStagingValidation.RunBatch`.
Output: `Logs/AbilitySync/source-staging-result.txt`. This validator has been
compiled but has not been executed in native Unity. It is not a two-client
network timing test and does not save or rebuild the scene.

For the timing report, enable **Tools > KLTN > Temporary > Trace Ability
Presentation** in both Editors after entering Play Mode. Reproduce Thiên Linh
Cái/Ma Đói alongside Ông Kẹ/Thần Trùng, swap the committing side, then save both
Editor logs and disable tracing. Traces are development-only and off by default;
they omit player/card/target identities and payloads. See
`Tools/AbilitySyncCheck/README.md` for the log comparison command and limitations.


### Multi-step ability / Ma Trơi ordering (2026-11-20)

Each effect-list execution now finishes before publishing its captured allied-death
hand-cost notifications. Death history and effective costs still update after each
operation; the buffered notifications never recompute old deltas. Publication
sequences remain monotonic. Execution IDs survive viewer projection so two consecutive
invocations of the same source/ability do not share one staging group.

Manual scenarios (both local and opponent perspectives):
- Ma Trơi + Thiên Linh Cái in DrawHand: summon, select an ally; the source stays
  centered through kill and revive, returns once, then Ma Trơi's badge animates.
- Ma Trơi + Thần Trùng in DrawHand: kill the ally and enemy while the source stays
  centered, return once, then animate the hand-cost reduction.
- Repeat with two Ma Trơi, cost 1/0, combat deaths and end-round expiry. Opponent
  must never see hidden hand identities or cost notifications. Ma Gà uses the same
  enlarged badge, with unchanged timing, sound, center and card pulse.

Verification: the two ordering regressions first failed with the passive at index 1;
258 Domain cases now pass, including per-death deltas and distinct execution IDs.
Projection checks cover both viewers, both multi-step shapes and hidden-hand privacy.
Runtime/Editor compile was checked separately. Actual Play Mode motion has not been
observed in this change; do not treat the .NET checks as Unity animation verification.
