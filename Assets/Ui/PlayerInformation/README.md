# Player information

MainMenu's avatar opens a runtime-built modal with Personal information, Ranking
and Owned cards tabs. `PlayerInformationView` holds the scene references;
`PlayerInformationPanel` owns layout. The existing rename button is hidden in the
authored frame and moved beside username when the panel is constructed. The
rename service, confirmation and one-change restriction are unchanged.

`Avatar.png` is the user's supplied 64×64 transparent black silhouette, copied
without pixel edits. The existing AlphaTint material colors it cream for contrast.
`ProfileFrameCatalog.avatarRect` stores the measured opening for each of seven
rank frames so the portrait follows season-dependent frame changes.

Personal data comes from the signed-in Firebase session, PlayerProfileService
and the current season standing. Email falls back to the email claim in the
Firebase-issued session token after refresh-token login; claims are used only
for display, never authorization. The modal does not store email in Firestore.

History reads completed seasons newest first, five season standings at a time.
Missing entries are omitted; failed reads remain retryable and do not become
zero-point results. Rank is derived from saved points using the current RankPolicy.
Win rate uses the existing wins/matches definition, including draws in matches.

Owned cards use actual OwnedCardIds, not the gameplay starter fallback. The
shared `Assets/Prefabs/UI/CardDetailModal.prefab` is instanced by GameScene and
MainMenu. NetworkCardVisual maps current match stats to CardDetailData; collection
cards use their base content stats. Right-click opens details without gameplay input.

## Verification — 2026-11-25

- Changed C# compiled against installed Unity 6000.5.4f1 assemblies.
- Ten offline history checks passed (ordering, paging, owner, missing entries,
  failures, cancellation and win rate).
- Serialized references, the 101-record prefab hierarchy, GameScene instance,
  sorting band and exact supplied avatar bytes checked outside Unity.
- Unity startup still exits 198: no valid Editor license. Import, rendering and
  Play Mode have not been verified. When licensing is available, run
  `Tools/KLTN/Validate Player Information`, then check all three tabs, rename,
  right-click detail, Escape layering, scroll retention and account switching.

## Redesign — 2026-11-26

Layout follows `Downloads/Profile/Sample.png` in the LoginScene frame style, widened to
1240 × 980 so the ranking table and four-column card grid fit. Shared pieces live in
`Assets/Scripts/UI/Common/ModalChrome.cs` (`ModalTheme` + builders): ImproveLogin
rounded-fill / panel-outline / rounded-outline / lotus / AlphaTint, and the PlayMode
corner, title line, divider and close button. The close button is centred on the
top-right corner (half outside), as in the play-mode modal.

- Tabs are icon-only (`Sprites/TabProfile|TabRanking|TabCards.png`, supplied 64×64
  black glyphs, tinted cream through AlphaTint).
- Gold/silver use the match-result art `Assets/Art/Processed/UI/MatchResult/
  RewardCoin.png` and `RewardSilver.png` (no copies).
- "Điểm xếp hạng" shows `<Bậc> - <Điểm>`; the tier comes from `RankPolicy.TierFor`
  and the name from `RankCatalog`. No standing this season shows `Đồng - 0`.
- The rename hint shows `Đã thay đổi tên: n / 1 lần`; the one-change rule is unchanged.
- `UsernameChangeView.ApplyTheme` restyles the two authored rename modals at runtime
  (rename input, confirmation). Serialized references and the rename flow are unchanged.
- Sprite `.meta` files for the new PNGs (and the PlayMode sprites) were written by hand so
  the scene can reference their GUIDs before Unity imports them.

Verified: runtime + Editor C# compile against Unity 6000.5.4f1 assemblies
(`Logs/ProfileModalCompile`). Not verified in Unity: import, rendering, Play Mode.

### Follow-up — 2026-11-26

- Removed "Làm mới" and "Tải thêm". History pages load when the list is scrolled near
  its end or does not yet fill the view; a failed page is retried by reselecting the tab.
- The heading follows the tab: Thông tin cá nhân / Lịch sử xếp hạng / Bài đã sở hữu.
- History rows and card tiles are anchored to the scroll content's top edge (they were
  centre-anchored, which left a blank band above the cards and hid the last row).
- `CardDetailModal.prefab` `ModalBoard` is now an opaque rounded panel (rounded-fill,
  dark plum, 97 % alpha) for readability; GameScene uses the same prefab.
- Main-menu portrait: `ProfileFrame > Avatar` (black `Assets/Ui/Circle.png`) `> AvatarImage`
  (inset 20 units). `ProfileFrameCatalog.AlignAvatar` positions the holder in frame-local
  units at 90 % of the measured opening.

### Follow-up 2 — 2026-11-26

- Owned-card tiles copy the detail modal's card face (`CardDetailModal.CreateCardFace`:
  the preview `VisualRoot` with artwork and cost/damage/health badges; match-only
  overlays removed). Tiles are 228 × 319 with a 56-unit top/bottom margin so artwork
  that pops above the frame is not clipped.
- The main-menu portrait uses `Sprites/TabProfile.png`, the same user icon as the tab.
- Detail modal: no role tag; stats read `Năng lượng | Sát thương | Máu`. Passive
  keywords come from `Card.keywords` and are listed under `[Nội tại]` below the skill
  text (Phù Du, Đáng sợ, Không thể thủ, Hút máu). Bracket lines such as
  `[Fearsome], [Play]` are removed from the authored skill text. Hovering a keyword
  shows `KeywordTooltip`: rounded text tip whose top-right corner follows the cursor.

### Follow-up 3 — 2026-11-26

- Portrait icon is tinted grey (`AvatarImage` colour 0.62) through AlphaTint; the shared
  `TabProfile.png` is unchanged, so no duplicate asset is needed. The whole black disc
  (`Avatar`) is the click target.
- Card detail modal is 20 % wider (board 1200 × 600, info column 760).
- The keyword tip shows only the description.
- Authored `CardLoreProfile.skillDescription` texts (Assets/Prefabs/CardArtwork) no longer
  contain tag lines (`[Play]`, `[Last Breath]`, `[Fearsome]`…) or passive explanations;
  passives appear only under [Nội tại]. Cards left without an active skill (Ma Lai,
  Quỷ Một Giò) hide the "[Chi tiết kỹ năng]" block.
- Gold/silver pills are 90 units apart with 24 units of right padding for the value.
