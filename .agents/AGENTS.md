# AGENTS.md — KLTN

> Trạng thái: **bản nháp, còn chỉnh sửa**. Cập nhật lần cuối: 01/11/2026 (đợt 1 sau giữa kỳ đã merge vào `main`:
> plan 01 server tin cậy, plan 02 vào trận có xác minh + loadout/kết quả từ server, plan 04 client polish & resilience).
> Nguồn: báo cáo Phase 3 (14/09/2026) + đối chiếu source hiện tại (commit `272bb39`).
> Đọc kèm: `.agents/memory.md` (kỹ thuật đã áp dụng) và `.agents/rules/*.md` (quy tắc bắt buộc).

## 1. Dự án là gì

Game thẻ bài 1v1 (Unity 6 — `6000.5.4f1`), chủ đề ma quỷ dân gian Việt Nam, 19 Unit card.
Mô hình **server authoritative**: Domain C# thuần quyết định luật, client chỉ gửi intent
và nhận snapshot đã lọc theo người xem.

Stack chính:

| Thành phần | Công nghệ |
|---|---|
| Netcode | Netcode for GameObjects `2.13.0`, Unity Transport |
| UGS | Authentication (OIDC), Multiplayer Services `2.3.1` (Session/Relay, Matchmaker), Cloud Code |
| Dedicated server | `com.unity.dedicated-server`, **Edgegap** (cloud hosting) qua Edgegap Unity plugin |
| Auth | Firebase (REST) → UGS OpenID Connect |
| Persistence | Cloud Firestore qua **REST** (`(default)` DB, project `unity-kltn`), Security Rules trong `Firebase/firestore.rules` |
| Render/UI | URP 2D, uGUI + TextMeshPro |
| Test | Unity Test Framework, EditMode cho Domain |

## 2. Bản đồ thư mục

```text
Assets/
├── Scripts/
│   ├── Authentication/          # IAuthService, FirebaseRestAuthService, GoogleAuthService, AuthManager (+ quên mật khẩu)
│   │   └── Validation/          # asmdef KLTN.Authentication.Validation — AuthInputValidator (global namespace, test được)
│   ├── Audio/                   # AudioVolumeSettings (PlayerPrefs Music/SFX), UIClickManager, MenuMusicManager
│   ├── Client/                  # UGSInitializer
│   ├── Game/
│   │   ├── Domain/              # asmdef KLTN.Game.Domain (noEngineReferences) — LÕI LUẬT
│   │   │   ├── Abilities/       # definitions, trigger resolver, target validator, effect executor, AbilityResolution
│   │   │   ├── Cards/           # CardDefinition, CardInstance, PlayerState, DeckRules (25 lá), WeightedDeckBuilder, CardContentValidator
│   │   │   ├── Economy/         # Currency, CardTier, ShopEntry, Wallet, PurchaseRules, MatchRewardPolicy
│   │   │   ├── Ranking/         # RankTier/RankPolicy, RankPointsPolicy, SeasonWindow/SeasonSchedule, SeasonStanding, UsernamePolicy
│   │   │   ├── Combat/          # RoundResolution, RoundTransition
│   │   │   └── Match/           # MatchState, MatchRulesEngine.*, MatchFactory, GameEvents, RoundHistory
│   │   ├── Content/             # Authoring (ScriptableObject) / Catalogs (+ ShopCatalog, RankCatalog, RankingSettings) / Mapping (SO → Domain)
│   │   ├── Networking/          # Bridge (NGO RPC, + .Loadout) / Contracts (DTO) / Projection (snapshot, inbox, DTO mapper)
│   │   └── Presentation/        # Cards / Combat (animation, VFX) / Match (presenter, HUD, selection)
│   ├── Infrastructure/
│   │   ├── CloudCode/           # IMatchResultReporter (AdmitPlayer/ReportResult), CloudCode/Null/Stub reporter, MatchServerContext, MatchServerModuleClient (GetTicketTier)
│   │   ├── Resilience/          # asmdef KLTN.Infrastructure.Resilience — NetworkRetryPolicy (1+3 lần, 1/2/4 s + jitter)
│   │   ├── Networking/Common/   # PersistentNetworkRoot
│   │   ├── Networking/Dedicated/# Matchmaker client, DedicatedServerBootstrap (ConnectionApproval), ConnectionPayload, PublicIpResolver (Edgegap path)
│   │   ├── Networking/Listen/   # ListenSessionController (Session/Relay path)
│   │   ├── SceneFlow/           # SceneNames, MainMenuController
│   │   ├── Firestore/           # FirestoreRestClient, FirestoreValueMapper (REST + typed values)
│   │   ├── Economy/             # IEconomyGateway, FirestoreEconomyGateway, PlayerProfileService (+ username)
│   │   └── Ranking/             # ILeaderboardGateway, FirestoreLeaderboardGateway, LeaderboardService
│   ├── Networking/              # UgsSessionService (singleton)
│   └── UI/                      # AppFlowManager, Login/Register, Surrender, Graveyard, Settings
│       ├── Leaderboard/         # LeaderboardController (mùa, phân trang, dòng ghim), LeaderboardRowView
│       └── Shop/                # ShopController, ShopCardItemView, ShopTopBarView, ShopListQuery, ShopScrollRect, HoverScale, ShopConfirmDialog
├── Tests/EditMode/Domain/       # asmdef KLTN.Game.Domain.Tests (Abilities/Cards/Combat/Match/Ranking/Economy)
├── Tests/EditMode/Client/       # asmdef KLTN.Client.Tests (NetworkRetryPolicy, AuthInputValidator)
├── Editor/KLTN/                 # CardContentInstaller, ShopCatalogInstaller, ShopSceneBuilder, LeaderboardSceneBuilder, AbilityTargetLink*
├── Resources/Cards/             # 19 card asset (.asset)
├── Resources/ShopCatalog.asset  # starter/giá/tier — sinh bởi ShopCatalogInstaller
├── Prefabs/UI/ShopCardItem.prefab # sinh bởi ShopSceneBuilder
├── CloudCode/Matchmaker.ccmr    # đường dẫn tương đối → CloudCode/EdgegapAllocator (deploy thành module tên **EdgegapAllocator**, theo assembly)
├── CloudCode/MatchServerModule.ccmr # → CloudCode/MatchServerModule (GetTicketTier/AdmitPlayer/ReportResult)
├── CloudCode/SeasonModule.ccmr  # tham chiếu CloudCode/SeasonModule (reset mùa)
├── Resources/RankingSettings.asset, RankCatalog.asset # sinh bởi Tools/KLTN/Install Ranking Assets
├── Sprites/Ranks/               # 2 sheet, 7 icon rank (Đồng → Thách đấu)
├── StreamingAssets/             # firebase/google config (google-auth-config.json bị gitignore)
└── Scenes/                      # LoginScene, MainMenu, HostClientMenu, FindMatch, ServerBootstrap, GameScene, ShopScene, LeaderboardScene
Firebase/firestore.rules         # (ngoài Assets) sinh bởi Tools/KLTN/Export Firestore Rules — dán vào Firebase Console
Firebase/firestore.indexes.json  # index entries (points, wins) — sinh cùng lúc với rules
Firebase/rules.test.mjs          # test rules trên emulator: cd Firebase && npm install && npm test
CloudCode/SeasonModule/          # (ngoài Assets) module C# reset mùa + README cài đặt
CloudCode/MatchServerModule/     # GetTicketTier (client), AdmitPlayer + ReportResult (chỉ server) + Tests (emulator) + README Access Control
CloudCode/EdgegapAllocator/      # allocator Matchmaker → Edgegap (đọc tier thật, sinh MATCH_ID/MATCH_TOKEN, ghi matches/{id})
CloudCode/Shared/                # GoogleServiceAccount, FirestoreAdmin, SeasonDocuments, MatchRecords (link source vào các module)
CloudCode/.gitignore             # re-include *.sln/*.csproj (gitignore gốc chặn), bỏ bin/obj
Claude outputs/                  # báo cáo/tệp tạm do Claude tạo — đã nằm trong .gitignore, không commit
```

Dependency hợp lệ (mũi tên = "được phép phụ thuộc vào"):

```text
Presentation → Networking, Content
Networking   → Domain
Content      → Domain
Infrastructure/Auth/UI → Networking (qua facade), UGS/Firebase SDK
Infrastructure/Economy → Domain.Economy, Content (ShopCatalog), Firestore REST; nạp loadout qua MatchLoadoutRegistry
Infrastructure/Ranking → Domain.Ranking, Content (RankingSettings), Firestore REST
CloudCode/SeasonModule → link Domain/Ranking/SeasonWindow.cs (không tham chiếu Unity)
CloudCode/MatchServerModule, EdgegapAllocator → link policy Domain (MatchRewardPolicy, RankPointsPolicy, ...) + CloudCode/Shared
Infrastructure/CloudCode → Domain (MatchAdmissionPolicy, MatchResultBanner); NetworkMatchBridge gọi qua IMatchResultReporter
Networking   ↛ Economy (chỉ đọc delegate MatchLoadoutRegistry.LocalOwnedCardIds)
Tests        → Domain; Client.Tests → Resilience, Authentication.Validation
Domain       → (không gì cả ngoài System.*)
```

## 3. Luồng chính cần nắm trước khi sửa code

**Command (một thao tác người chơi):**

```text
Presenter/UI → NetworkMatchBridge.Request...()          (client, CommandId tăng dần)
  → Submit...Rpc                                        (server: map clientId → SeatId, chống trùng/cũ)
  → MatchRulesEngine.Try...(state, actor, ...)          (Domain: validate + mutate atomic)
  → CommandResult (+ GameEvents / RoundResolution / AbilityResolution)
  → Publication: revision++ → MatchSnapshotBuilder.Build(viewer) cho từng seat
  → MatchClientProjection / MatchUpdateInbox (client) → Presenter render + animation
```

**Kinh tế (shop, thưởng, deck theo thẻ sở hữu):**

```text
Login → PlayerProfileService.EnsureLoadedAsync → FirestoreEconomyGateway
  → users/{uid} chưa có? :commit tạo user (0 xu/0 bạc) + 8 ownedCards starter
Vào trận dedicated: ownedCardIds lấy từ AdmitPlayer (server đọc Firestore) — danh sách client gửi chỉ lấy giao (ServerLoadoutPolicy)
Vào trận listen-host: SubmitLoadoutRpc(ownedIds CSV) như cũ (trận riêng, không thưởng/điểm)
  → server chờ đủ 2 seat (timeout 10s → starter) → WeightedDeckBuilder (25 lá, trọng số theo tier)
  → MatchFactory.Create(hostDeck, guestDeck); matchId = env MATCH_ID (dedicated) / Guid (listen)
Hết trận dedicated: NetworkMatchBridge.Settlement → IMatchResultReporter.ReportResult (Cloud Code, idempotent theo matchId,
  retry 2/4/8/16 s) → ReceiveSettlementRpc(MatchSettlementDto) → MatchResultPresenter hiển thị + đọc lại ví/standing.
  Client KHÔNG ghi thưởng/điểm nữa (Rules chặn matchRewards/results/entries update).
Shop: ShopController → modal xác nhận → PlayerProfileService.PurchaseAsync → :commit (trừ tiền + tạo ownedCards)
```

**Xếp hạng theo mùa:**

```text
Cloud Code AdvanceSeason (Scheduler mỗi giờ) → config/season (lịch) + seasons/{id} (lịch sử)  — mùa tính theo đồng hồ
Vào trận: snapshot.isRanked (dedicated = reporter có xếp hạng; listen chỉ là cờ hiển thị, không ghi gì)
          + matchStartedUtcMs (giờ server bắt đầu trận)
Hết trận dedicated: ReportResult (Cloud Code MatchServerModule) áp RankedEligibilityPolicy + RankPointsPolicy
  (+30/−20/0/−20) → ghi entries/{uid} + results/{matchId} + matchRewards trong một commit (server-only)
LeaderboardScene: entries orderBy points desc, winRate desc, username asc → trang 50; vị trí của mình = 3 COUNT (điểm cao hơn; bằng điểm + tỉ lệ cao hơn; bằng cả hai + username nhỏ hơn) + 1. Hàng trống 0 điểm cho mọi tài khoản. Trước khi đổi thiết kế dữ liệu: nêu đánh đổi và hỏi user trước.
Matchmaker: rank_tier lấy từ Cloud Code GetTicketTier (không còn client tự khai); allocator đọc lại tier thật, lệch > 1 → từ chối
```

**Auth + vào trận dedicated:** Firebase ID token → UGS `SignInWithOpenIdConnectAsync("oidc-firebase")`
→ UGS PlayerId/access token → **GetTicketTier** (bắt buộc trước ticket: ghi `ugsPlayers/{playerId}` → uid)
→ Matchmaker ticket → Cloud Code `EdgegapAllocator` (ghi `matches/{matchId}`, truyền `MATCH_ID`/`MATCH_TOKEN`)
→ Edgegap deployment → `IpPortAssignment` → client `StartClient` với `ConnectionPayload {playerId, accessToken, matchId}`
→ server `ConnectionApproval` → `AdmitPlayer` (≤ 10 s) → đủ 2 người đã admit mới nạp GameScene.
Chi tiết: `memory.md` §6–7, §13 (bài học vận hành khi deploy).

## 4. Quy tắc bắt buộc (tóm tắt — chi tiết trong `rules/`)

1. **Domain thuần**: không `using UnityEngine`, NGO, UGS, Firebase trong `Game/Domain`. Random qua `IRandomSource`.
2. **Server quyết định**: mọi thay đổi `MatchState` phải đi qua `MatchRulesEngine.Try...`. Client không tự sửa state.
3. **Không lộ thông tin ẩn**: DTO gửi viewer khác không chứa danh tính DrawHand, thứ tự Deck.
4. **Animation là hệ quả**: Presentation chỉ tiêu thụ `RoundResolution`/`AbilityResolution` DTO; không quyết định thắng/thua, ai chết.
5. **Ability data-driven**: không `switch` theo tên/ID card trong rules. Card mới = ScriptableObject + `CardContentInstaller`.
6. **Card visual là local object**: không tạo `NetworkObject` cho từng card.
7. **Giữ GUID**: di chuyển `.cs` luôn kèm `.meta`. Không đổi tên type/namespace MonoBehaviour khi chưa có kế hoạch migration scene/prefab.
8. **Region + XML summary**: theo `rules/coding-conventions.md`.
9. **Không thêm pattern vì tên gọi**: chỉ áp dụng pattern khi có vấn đề thật (xem `rules/design-patterns.md`).
10. **Kinh tế đi qua `IEconomyGateway`**: UI/Presentation không gọi Firestore trực tiếp. Mọi ghi tiền/thẻ là một
    `:commit` batch để Security Rules kiểm bằng `getAfter/existsAfter`. Đổi giá/tier/starter → sửa bảng trong
    `ShopCatalogInstaller.cs`, chạy lại Install + Export Rules, dán rules lên Console (xem skill `adjust-shop-economy`).
11. **Shop UI là sinh mã**: `ShopScene`/`ShopCardItem.prefab` do `ShopSceneBuilder` dựng — sửa layout/style trong builder rồi
    chạy lại menu, không sửa tay scene (sẽ bị ghi đè). Tương tự `LeaderboardScene` ← `LeaderboardSceneBuilder`.
13. **Rank là dẫn xuất từ điểm**: không lưu `tier` vào Firestore; đổi điểm/ngưỡng → sửa `RankPointsPolicy`/`RankPolicy`
    (Domain) rồi Export Firestore Rules (rules nhúng bảng điểm) và dán lên Console. Thời gian mùa chỉ do Cloud Code ghi.
14. **Thưởng/điểm/tier chỉ do server ghi** (Cloud Code `MatchServerModule` với service account). Client chỉ đọc;
    đừng thêm lại đường ghi `matchRewards`/`entries`/`results` từ client. Hàm Cloud Code mới cho server → nhớ Deny Player
    trong Access Control; hàm cho client → cần statement Allow riêng (xem `memory.md` §13).
12. **Tuân thủ docs Unity 6000.5.4f1**: mọi code sinh ra phải đúng Scripting API của phiên bản đang dùng; không dùng API obsolete
    (VD `FindFirstObjectByType` → `FindAnyObjectByType`, `GetInstanceID` → `GetEntityId`). Xem `rules/unity-api-compliance.md`.

## 5. Build & kiểm chứng

- Unity Test Runner → EditMode → `KLTN.Game.Domain.Tests` + `KLTN.Client.Tests` phải pass (216/216 sau đợt 1, 01/11/2026).
  CLI: `Unity.exe -batchmode -projectPath <wt> -runTests -testPlatform EditMode -testResults <file>.xml`.
- Cloud Code: `dotnet run --project CloudCode/MatchServerModule/MatchServerModule.Tests` (40 case) và SeasonModule.Tests
  (13 case), cần Firestore emulator (`cd Firebase && npx firebase emulators:start --only firestore --project demo-kltn`).
- Kinh tế: đăng nhập tài khoản mới → log `[Profile] Loaded ... 8 thẻ`; chơi 1 trận Find Random → `matchRewards` đúng 1 doc/người (do Cloud Code ghi);
  mua 1 thẻ → trừ đúng giá. Rules: `cd Firebase && npm install && npm test` (emulator, có test batch).
- Xếp hạng: thủ công 2 tài khoản đánh 1 trận Find Random → `seasons/{id}/entries` +30/−20 (server ghi),
  LeaderboardScene hiện đúng 2 dòng; đánh lại trong < 60s → trận sau vẫn ghi (client chờ hết cooldown).
- Compile 0 warning / 0 error — đặc biệt không có `CS0618/CS0619` (API obsolete của Unity 6.5) (`.editorconfig`: CRLF, 4 space, Allman, max 90 cột).
- Thay đổi networking/presentation: test thủ công host + guest (Multiplayer Play Mode) từ Mulligan tới ≥ 2 round,
  targeted ability (confirm/cancel/retry/no-target), combat có keyword, surrender, disconnect.
- Sau khi move file: kiểm tra Missing Script ở `GameScene`, `MainMenu`, prefab card.
- Dedicated đầu–cuối: build profile **Linux Server** phải Override Global Scene List = `ServerBootstrap` (đầu tiên) + `GameScene`;
  Edgegap gói Free chỉ 1 deployment → terminate deployment cũ trước khi test lại. Chi tiết `memory.md` §13.

## 6. Nợ kỹ thuật đang mở (đừng vô tình làm tệ hơn)

| # | Vấn đề | Vị trí |
|---|---|---|
| 1 | Mulligan bypass rules engine, dùng `new System.Random()` thay vì `IRandomSource` | `NetworkMatchBridge.ServerCommands.cs` → `PlayerState.PerformMulligan` |
| 2 | Zone là `List<CardInstance>` public mutable | `PlayerState.cs` |
| 3 | Chưa có Application layer; `NetworkMatchBridge` gánh nhiều vai | `Game/Networking/Bridge` |
| 4 | Static registry/singleton | `MatchProjectionRegistry`, `MatchUpdateInboxRegistry`, `UgsSessionService.Instance`, `AuthManager.Instance`, `GoogleAuthService.Instance`, `AppFlowManager.Instance` |
| 5 | `CardPresentationCatalog` (Content) tham chiếu `CardArtworkView` (Presentation) | `Game/Content/Catalogs` |
| 6 | 29 file ngoài `Game/` còn global namespace | `Authentication`, `UI`, `Infrastructure`, ... |
| 7 | ~~Dedicated server không xác thực danh tính client~~ Đã sửa (plan 02): ConnectionApproval + AdmitPlayer | `DedicatedServerBootstrap.cs` |
| 8 | ~~`Matchmaker.ccmr` trỏ đường dẫn tuyệt đối~~ Đã sửa (plan 01): `CloudCode/EdgegapAllocator` trong repo | `Assets/CloudCode` |
| 9 | Scene trùng `GameScene Test Skills Animation.unity` | `Assets/Scenes` |
| 10 | Spell/stack/CounterSpell chưa triển khai (validator chủ động từ chối) | `EffectKind.CounterSpell` |
| 11 | ~~Dùng API obsolete `FindFirstObjectByType` (CS0618)~~ Đã sửa 18/10 → `FindAnyObjectByType` | `UI/SurrenderUI.cs:24` |
| 12 | ~~Client tự ghi thưởng~~ Đã sửa (plan 01+02): thưởng do `ReportResult` ghi; client vẫn tự ghi mua thẻ/tạo hồ sơ (Rules kiểm giá) | `FirestoreEconomyGateway`, `firestore.rules` |
| 13 | ~~Server tin danh sách thẻ client~~ Đã sửa trên dedicated (thẻ từ AdmitPlayer); listen-host vẫn tin client (trận riêng, không thưởng) | `NetworkMatchBridge.Loadout.cs` |
| 14 | Rớt mạng giữa trận → trận bị hủy, không ai nhận thưởng, không có report (plan 03 xử lý) | `NetworkMatchBridge.Connections.cs` |
| 15 | ~~`firestore.rules` chưa chạy trên emulator~~ → đã có `Firebase/rules.test.mjs` (35 case) | `Firebase/` |
| 16 | ~~Điểm rank do client ghi~~ Đã sửa (plan 01+02): `ReportResult` ghi, Rules chặn client | `CloudCode/MatchServerModule` |
| 17 | ~~`rank_tier` do client tự khai~~ Đã sửa: `GetTicketTier` + allocator đọc lại tier thật | `DedicatedMatchmakerClient.cs`, `EdgegapAllocator.cs` |
| 19 | Allocator luôn tạo deployment Edgegap mới (1 trận = 1 container), gói Free chỉ 1 deployment | `EdgegapAllocator.PlaceMatchAsync` → plan 05 |
| 20 | Chưa có EditMode test cho `ConnectionPayload`, `CloudCodeMatchResultReporter.ParseSettlement` (nằm trong Assembly-CSharp) | `Infrastructure/CloudCode`, `Networking/Dedicated` |
| 18 | Hòa điểm xếp theo wins rồi uid; vị trí của mình dùng COUNT(points > mine) nên có thể lệch vài hạng khi bằng điểm | `FirestoreLeaderboardGateway.GetPositionAsync` |

## 7. Tìm code nhanh

```powershell
rg -n "RequestSurrender|SubmitSurrenderRpc|TrySurrender" Assets/Scripts   # theo 1 command end-to-end
rg -n "Phase\s*=" Assets/Scripts/Game/Domain                              # mọi mutation phase
rg -n "thien_linh_cai_play" Assets                                        # theo 1 ability ID
rg -n "Registry\.Current" Assets/Scripts                                  # nơi dùng static registry
rg -n "SubmitLoadoutRpc|WeightedDeckBuilder|AdmitPlayer" Assets/Scripts CloudCode # luồng loadout/vào trận
rg -n "ReportResult|MatchSettlement|isRanked|rank_tier|GetTicketTier|AdvanceSeason" Assets CloudCode # kết quả/xếp hạng
```
