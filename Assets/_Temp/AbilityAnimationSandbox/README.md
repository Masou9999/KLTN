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
- **TEST MA TRÀNH** next to RESET prepares a fresh fixture with one allied death
  recorded this round and Ma Trành already in hand. Drag it to Reserve and press
  **TRIỆU HỒI** to see the bite. Press the shortcut again to prepare another test.
  RESET restores the ordinary fixture with no allied deaths. This shortcut only
  exists in the Editor sandbox and does not change production ability rules.
- For a manual Ma Tranh test, first sacrifice an ally in this round. With a full Reserve,
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
have no dependency on this folder. No production Domain/network rules changed.

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
