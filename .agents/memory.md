# memory.md — Kỹ thuật đã áp dụng trong KLTN

> Mục đích: bộ nhớ dài hạn cho agent/người mới — **đã chọn gì, vì sao, giới hạn ở đâu**.
> Khác với `rules/` (quy tắc phải làm theo), file này ghi **quyết định và hiện trạng**.
> Cập nhật khi một quyết định kiến trúc thay đổi. Lần cuối: 01/11/2026 (đợt 1 sau giữa kỳ: plan 01/02/04 đã merge, §13–15).
> Nhãn: `[Đã kiểm chứng trong code]`, `[Chưa kiểm chứng]` (cần xác minh ngoài repo, VD dashboard UGS/Edgegap).

---

## 1. Ba cấp độ — đừng trộn

| Cấp | Câu hỏi | KLTN |
|---|---|---|
| Architectural style | Hình dạng/topology toàn hệ thống | Modular monolith; layered organization; client–server server-authoritative |
| Architectural pattern | Phân vai subsystem | Clean Architecture boundary (một phần), Ports-and-Adapters, MVP-style Presentation, Authoritative Command + Filtered Projection, Typed Domain Events |
| Design pattern | Cộng tác class cục bộ | Simple Factory, Builder-style Assembler, Command-style Intent, Explicit FSM, Observer, Strategy, Behavior Composition, Adapter, Data Mapper, Catalog, Null Object, Facade, Snapshot capture |

## 2. Architectural styles

- **Modular Monolith** — một app Unity, module chia theo `Game/{Domain,Content,Networking,Presentation}` + `Infrastructure`.
  Ranh giới thật duy nhất bằng asmdef: `KLTN.Game.Domain` (`noEngineReferences`). Lý do: game 1v1, state 1 process, deterministic.
- **Layered organization** — Domain trong cùng; Content/Networking; Presentation/Infrastructure/UI ngoài cùng. Không strict layering.
- **Client–Server, Server Authoritative** — server giữ `MatchState`, client gửi intent. Hai topology: listen (Session/Relay) và dedicated (Edgegap).

## 3. Architectural patterns

- **Clean Architecture boundary (một phần)** — dependency hướng vào Domain; chưa có Application layer, orchestration ở `NetworkMatchBridge`.
- **Ports-and-Adapters** — port `IAuthService`, `IRandomSource`, `IEconomyGateway`; adapter Firebase REST, Firestore REST, NGO RPC, UGS Matchmaker, Session/Relay, mapper content.
- **MVP-style Presentation** — model = `MatchClientProjection`/`MatchUpdateInbox`; presenter = `Match*Presenter`, `AbilityTargetSelectionController`; view = card visual, drop zone, animator.
- **Authoritative Command + Filtered Projection** — command side mutate `MatchState`; `MatchSnapshotBuilder.Build(viewer)` dựng read model riêng mỗi seat. Không phải CQRS đầy đủ.
- **Typed Domain Events** — `GameEvent`/`GameEventBatch` (`GameEvents.cs`), `AbilityResolutionEvent` (có `Sequence`) để trigger resolver và chuyển cho animation. Không có global event bus.

## 4. Design patterns (implementation thật)

| Pattern | Code | Ghi chú giới hạn |
|---|---|---|
| Simple Factory | `MatchFactory.Create` | Không phải Factory Method/Abstract Factory (không có product family/subclass) |
| Builder-style Assembler | `WeightedDeckBuilder.Build`, `DefaultDeckBuilder.Build` (fallback/test), `MatchSnapshotBuilder.Build` | One-shot, không fluent/Director |
| Command-style Intent | `Request...` → `Submit...Rpc` → `MatchRulesEngine.Try...` → `CommandResult` | Chưa có `GameCommand` object/dispatcher; Mulligan còn bypass |
| Explicit FSM | `MatchPhase`: Mulligan, RoundStart, Priority, AbilitySelection, BlockDeclaration, CombatResolution, RoundEnd, Finished | Enum + guard, không phải GoF State |
| Observer | C# event trong projection/inbox; presenter subscribe | Static registry làm lifecycle khó test |
| Strategy | `IRandomSource` / `SeededRandomSource` | — |
| Behavior Composition | `AbilityDefinition` = trigger + condition + target requirement + ordered effects | Không phải Composite GoF |
| Gateway/Adapter | `FirestoreEconomyGateway : IEconomyGateway` | Giai đoạn 2 thay bằng gateway Cloud Code, UI không đổi |
| Adapter | `FirebaseRestAuthService : IAuthService` | Hiện chỉ có adapter REST (Firebase SDK DLL vẫn nằm trong `Assets/FirebaseRef`) |
| Data Mapper | `CardDefinitionMapper`, `RoundResolutionDtoMapper`, `AbilityResolutionDtoMapper` | DTO mapper lọc thông tin ẩn |
| Catalog | `CardAssetCatalog`, `CardPresentationCatalog`, `ShopCatalog` | Không phải Repository |
| Policy (pure function) | `MatchRewardPolicy`, `PurchaseRules` | Domain thuần, có test; Rules Firestore là bản kiểm tra phía server |
| Null Object | `UnitPassiveRules.None` | — |
| Facade/Gateway | API public `NetworkMatchBridge` | Đang ôm nhiều trách nhiệm |
| Snapshot capture | `AbilityCardState.Capture` | Chụp state trước khi card đổi zone để animation đúng |
| Singleton (nợ) | `AuthManager`, `GoogleAuthService`, `AppFlowManager`, `UgsSessionService`, `Match*Registry`, `PlayerProfileService` | Không nhân rộng |

Kỹ thuật không phải pattern nhưng quan trọng: `partial class` theo use case; FIFO ability queue + giới hạn số lần resolve;
`#region`/XML summary chuẩn hóa; di chuyển script kèm `.meta` để giữ GUID.

## 5. SOLID — hiện trạng

| Nguyên tắc | Mức | Bằng chứng / nợ |
|---|---|---|
| SRP | Tốt một phần | Domain service tách rõ; `NetworkMatchBridge` còn nhiều vai; `PlayerState` zone public mutable |
| OCP | Tốt ở cấp card, một phần ở cấp effect | Card mới = data; effect mới phải sửa `EffectKind` + executor + validator |
| LSP | Ít rủi ro | Sealed + composition; contract `IAuthService` cần contract test |
| ISP | Đạt | `IAuthService`, `IRandomSource` nhỏ |
| DIP | Mạnh ở Domain, yếu ngoài Domain | asmdef `noEngineReferences`; static registry/singleton; Mulligan `new System.Random()` |

## 6. Hosting: Edgegap (cloud dedicated server)

`[Đã kiểm chứng trong code]`
- Package `com.edgegap.unity-servers-plugin` (Git) trong `Packages/manifest.json` — dùng để build Linux server + đóng container + push lên Edgegap.
- Module SDK: `com.unity.dedicated-server`, `com.unity.sdk.linux-x86_64`, toolchain `win-x86_64-linux`.
- `DedicatedServerBootstrap` (scene `ServerBootstrap`): chạy khi `UNITY_SERVER` hoặc graphics device `Null`;
  listen `0.0.0.0:7777`; đọc env `MATCH_ID`, `MATCH_TOKEN`, `UGS_*`; bật `ConnectionApproval` → `AdmitPlayer`;
  đủ **2** client đã admit → load `GameScene`; người thứ 3/trùng tài khoản/vào sau khi nạp GameScene bị từ chối;
  không có ai trong **120s** → `Application.Quit`; mọi người rời → chờ report xong (≤ 120 s) rồi tắt.
- `Assets/CloudCode/Matchmaker.ccmr` trỏ (tương đối) tới `CloudCode/EdgegapAllocator` trong repo (gốc từ repo Unity
  `matchmaker-hosting-providers`, đã sửa: đọc tier thật, sinh `MATCH_ID`/`MATCH_TOKEN`, ghi `matches/{matchId}`).
- `DedicatedMatchmakerClient` gửi `player_ip` (lấy qua `PublicIpResolver`) trong custom data của ticket
  — Edgegap dùng IP người chơi để chọn vị trí server gần nhất `[Chưa kiểm chứng: logic chọn vùng nằm trong module, ngoài repo]`.
- Client nhận `IpPortAssignment` → `UnityTransport.SetConnectionData(ip, port)` → `StartClient`.

`[Chưa kiểm chứng]` cấu hình queue/pool Matchmaker, secret Edgegap API token trong Cloud Code, app version/port mapping trên dashboard Edgegap.

Rủi ro đã biết:
- ~~`modulePath` tuyệt đối~~ đã chuyển vào repo (plan 01).
- Listen path (Session/Relay) vẫn song song → 2 bộ lifecycle/disconnect cần test riêng.
- 1 trận = 1 deployment mới (chưa reuse server — plan 05).

## 7. Auth chain: Firebase → UGS → UGS token → Edgegap

```text
[1] Firebase Auth (REST, FirebaseRestAuthService : IAuthService)
    ├─ Email/password: signUp / signInWithPassword
    ├─ Google: GoogleAuthService — OAuth 2.0 Authorization Code + PKCE (S256),
    │          loopback redirect http://127.0.0.1:<port>/ (HttpListener) → Google id_token
    │          → Firebase signInWithIdp(providerId=google.com)
    └─ Auto-login: refresh token (PlayerPrefs) → securetoken refresh
    ⇒ Firebase ID token (JWT)

[2] UGS Authentication — AuthManager.LoginFirebaseToUGS
    UGSInitializer.Initialize() → AuthenticationService.SignInWithOpenIdConnectAsync("oidc-firebase", firebaseIdToken)
    ⇒ UGS PlayerId + UGS access token (SDK giữ, tự gắn vào mọi call UGS)

[3] UGS Matchmaker — DedicatedMatchmakerClient
    CreateTicketAsync(Player(PlayerId, {player_ip}), queue)  ← xác thực bằng UGS access token
    → poll GetTicketAsync tới IpPortAssignment (Found/Failed/Timeout)

[4] Cloud Code EdgegapAllocator → Edgegap API → deployment server Linux (container)
    ⇒ IP:port trả về qua assignment

[5] Client → UTP connect thẳng tới Edgegap server (DedicatedServerBootstrap)
```

Logout: xóa PlayerPrefs (login method, refresh token) → `IAuthService.Logout` → `AuthenticationService.SignOut` → `GoogleAuthService.ClearSession`.

Điểm cần hiểu đúng (giả định ẩn):
- `[Đã kiểm chứng đầu–cuối 01/11/2026]` UGS access token **có** đi tới Edgegap server: client gửi
  `ConnectionPayload {playerId, accessToken, matchId}` (JSON, ≤ 8 KB) trong `ConnectionData`; server `ConnectionApproval`
  (Pending) → `AdmitPlayer(matchId=MATCH_ID, matchToken, playerId, playerAccessToken)` → Approved/Reason.
  Server **không** so `payload.matchId` với `MATCH_ID` (allocator sinh MATCH_ID khác matchId Matchmaker); tư cách thành
  viên do AdmitPlayer kiểm. `ConnectionApproval` nằm trong config hash NGO → client dedicated bật nó và tắt lại khi dừng.
- Firebase project phải được khai báo làm OIDC provider `oidc-firebase` trong UGS Dashboard (issuer
  `https://securetoken.google.com/<project-id>`) `[Chưa kiểm chứng: cấu hình dashboard]`.
- `google-auth-config.json` (chứa `clientSecret`) bị gitignore; `firebase-auth-config.json` chỉ chứa Web API key
  (công khai theo thiết kế Firebase, an toàn nhờ Security Rules/authorized domains, không phải secret).

## 8. Thay đổi so với báo cáo Phase 3 (14/09 → 04/10/2026)

- Script: 98 → **110** file, ~19.5k → **~23.5k** dòng. Test attribute: 97 → **103** (14 file).
- Mới: `MatchRulesEngine.Surrender` (`TrySurrender` đi đúng command pipeline), `SurrenderUI`, `MatchResultPresenter`
  (màn hình thắng/thua), `GraveyardUI`, `GameSettingsUI` + audio.
- Mới: `AbilityResolution` (semantic event + `AbilityCardState`), `AbilityResolutionDtoMapper`,
  `AbilityEffectExecutor.Resolution`, `CombatAnimationDirector.Abilities`, `CardReviveFeedback`,
  `AbilityTargetSelectionController`, `AbilityTargetLinkGraphic` (+ editor installer).
- Mới: `Infrastructure/SceneFlow` (`SceneNames`, `MainMenuController`), `Infrastructure/Networking/Common/PersistentNetworkRoot`,
  `PublicIpResolver`; UI auth tách `UI/Authentication`, `UI/Common/LoadingSpinner`.
- Login UX một bước (commit `b04b476`).
- Còn nguyên nợ: Mulligan bypass, zone mutable, chưa Application layer, static registry, Content→Presentation.
- Scene thêm `GameScene Test Skills Animation.unity` (bản duplicate khi giải conflict).

## 9. Phiên bản Unity và chính sách API

- Unity 6.5 — `6000.5.4f1`. Quyết định: **mọi code phải tuân thủ docs Unity của đúng phiên bản này**.
- Lý do: 6.5 chuyển từ `InstanceID` (int 32-bit) sang `EntityId` (64-bit) và bỏ mọi API dựa vào thứ tự ID.
  API `InstanceID` obsolete gây **lỗi compile**; `FindFirstObjectByType`, `FindObjectOfType`, `FindObjectsSortMode` bị deprecate.
- Thay thế đã chốt: `FindFirstObjectByType`/`FindObjectOfType` → `FindAnyObjectByType`; `FindObjectsByType(..., SortMode)` →
  `FindObjectsByType<T>()`/`(FindObjectsInactive)`; `GetInstanceID` → `GetEntityId`. Chi tiết: `rules/unity-api-compliance.md`.
- Vi phạm còn lại: không. `UI/SurrenderUI.cs:24` đã đổi sang `FindAnyObjectByType` (18/10).

## 10. Case/bug đang theo dõi

- Âm Binh bị kill trong Reserve bởi skill của card mới summon từ DrawHand: logic đã đúng, **thiếu animation hồi sinh**
  (kiểm tra lại với `CardReviveFeedback` mới).

## 11. Shop và kinh tế xu/bạc (05/10/2026)

`[Đã kiểm chứng trong code]` — Domain + 24 test liên quan pass (compile ngoài Unity bằng .NET 8). Rules: `[Chưa kiểm chứng trên emulator]`.

**Luật kinh tế**
- Tài khoản mới: 0 xu, 0 bạc, 8 thẻ starter. 11 thẻ còn lại bán trong Shop (8 thẻ xu, 3 thẻ bạc).
- Thưởng: thắng +75 xu +1 bạc; thua/hòa +25 xu; tự đầu hàng trước vòng 3 → 0 (chống farm). Đối thủ đầu hàng → người thắng vẫn đủ thưởng.
- Rớt mạng giữa trận → trận hủy, không thưởng (plan 03 sẽ đổi: giữ ghế 30 s, hết hạn → thua theo luật đầu hàng).

**Sức mạnh thẻ và giá** — `P = I × clamp(√R, 0.8, 1.3)`, `I = Damage + Health + K + A`, `R = I / (2·mana + 1)`
(K: Fearsome +1, Lifesteal +2, CannotBlock −1, Ephemeral −2; A = giá trị skill ước lượng theo deck "đồng minh chết").

| Nhóm | Thẻ (P) | Giá |
|---|---|---|
| Starter (Weak) | MaDa 3.6, MaDoi 5.2, MaGa 6.5, MaLon 6.5, QuyMotDo 6.6, VongNhi 7.0, OngKe 7.5, QuyCau 8.3 | — |
| Xu | MaMatMam 8.3 (Weak) 150, MaLai 8.6 (Weak) 200, ThienLinhCai 9.0 200, AmBinh 10.2 350, MaCo 10.2 350, MaCangSung 12.5 550, MaTroi 12.7 550, ThanTrung 13.0 600 (Mid) | 150–600 xu |
| Bạc (Strong) | LinhMieu 15.0 → 10, MaTranh 20.1 → 12, QuyDaXoa 34.0 → 15 | 10–15 bạc |

Nhận xét đã biết: bạc là nút thắt (37 bạc ≈ 74 trận ở 50% thắng); Quỷ Dạ Xoa lệch cân bằng; starter tối đa 4 mana.

**Deck** — `DeckRules.RequiredCardCount = 25`. `WeightedDeckBuilder`: trọng số Weak 3 / Mid 2 / Strong 1 (`CardTierWeights`),
mỗi thẻ sở hữu ≥ 1 lá, tối đa 4 (Âm Binh 1), chia phần dư bằng largest remainder, deterministic theo tập thẻ; MatchFactory shuffle.

**Firestore (REST, không dùng SDK)**
```text
users/{uid}                        coins, silver, schemaVersion, createdAt, updatedAt, lastPurchase, lastRewardMatchId
users/{uid}/ownedCards/{cardId}    acquiredAt, source: starter|coins|silver
users/{uid}/matchRewards/{matchId} result: win|loss|draw|surrender_early, coins, silver, createdAt
```
- Mọi ghi là một `:commit` batch, tiền đổi bằng transform `increment` (không ghi đè số dư cũ → không lost update).
- Rules nhúng bảng giá + danh sách starter (không cần collection `shopCatalog`); kiểm `getAfter/existsAfter`:
  mua phải trừ đúng giá và thẻ chưa sở hữu; thưởng chỉ (75,1)/(25,0)/(0,0) và mỗi `matchId` một lần; starter chỉ tạo cùng lúc tạo user.
- `AuthManager.GetValidIdTokenAsync()` tự refresh ID token khi còn < 5 phút.

**Đưa thẻ sở hữu vào trận** — một cơ chế cho cả listen-host và dedicated: client gửi `SubmitLoadoutRpc(csv)` lúc bridge spawn;
server lọc ID hợp lệ, thiếu thì dùng starter, chờ tối đa 10s. Networking không phụ thuộc Economy: đọc qua `MatchLoadoutRegistry`.

**Giai đoạn 2 — đã làm (plan 01+02, 01/11/2026)**: thưởng do Cloud Code `ReportResult` ghi, Rules chặn client ghi
`matchRewards`; server dedicated lấy ownedCards từ `AdmitPlayer`. Xem §13.

**Shop UI** — sinh bởi `ShopSceneBuilder` (menu Tools/KLTN/Build Shop Scene + Main Menu Button): lưới thẻ căn trái-trên,
cuộn chỉ bằng con lăn (`ShopScrollRect`: không kéo, clamped, không quán tính), overlay tối khi hover/đã sở hữu,
nút Mua đỏ chữ vàng có `HoverScale` 1.0→1.2, modal `ShopConfirmDialog` "Bạn có đồng ý mua thẻ {tên} không?".
7 chế độ: Bạc ↑/↓, Xu ↑/↓, Chỉ bạc, Chỉ xu, Chỉ thẻ chưa có (`ShopListQuery`).

## 12. Leaderboard theo mùa + điểm rank (16/10/2026)

`[Đã kiểm chứng ngoài Unity]` — Domain 48 test pass (.NET 8), Rules 35 case pass trên Firestore emulator,
Cloud Code 8 kiểm tra pass trên emulator, Assembly-CSharp + Editor compile được với DLL tham chiếu
Unity 2021/2021.1 (không phải 6000.5 — vẫn phải compile lại trong Unity). `[Chưa kiểm chứng]` deploy UGS thật.

**Luật (đã duyệt)** — Thắng +30, Thua −20, Hòa 0, Đầu hàng −20, điểm không âm, không bảo hộ rớt hạng, không Elo.
Ngưỡng: Đồng 0, Bạc 60, Vàng 150, Bạch kim 270, Kim cương 420, Cao thủ 600, Thách đấu 800 (cố định).
Rank luôn **tính từ điểm** (`RankPolicy.TierFor`), không lưu. Chống farm: ≥ 60s giữa hai kết quả của cùng người.

**Mùa** — mặc định 24h, khóa điểm 30 phút trước reset (`RankingSettings`: `seasonDurationHours`, `pointsLockMinutes`).
Trận được tính khi **bắt đầu** trong `[startAt, lockAt)` và kết quả **ghi trước** `endAt`. Trận bắt đầu trước giờ khóa
nhưng kéo dài quá giờ reset → không tính. Giờ dùng `ServerClock` (header `Date` của Firestore), không dùng đồng hồ máy.
Mùa mới = bảng mới (partition theo `seasons/{id}`), mùa cũ giữ nguyên để xem lại.
**Mùa tính theo đồng hồ** (`SeasonCalendar`): mùa k bắt đầu `anchorStart + (k − anchorId) × duration`, căn 00:00 giờ VN.
Lý do: UGS Scheduler tối thiểu **1 giờ/lần** → nếu reset phụ thuộc tick thì mất tới ~1h không tính điểm mỗi ngày.
Tick chỉ ghi lịch (đổi cài đặt = lịch `pending` áp dụng từ mùa sau) và doc lịch sử `seasons/{id}`.

**Firestore**
```text
config/season                         anchorSeasonId, anchorStartMs, durationMs, lockMs, pending* (chỉ Cloud Code ghi)
seasons/{id}                          seasonId, startAt, endAt, lockAt, closedAt? (lịch sử cho sidebar)
seasons/{id}/entries/{uid}            username, points, wins, losses, draws, matches, lastResultAt, createdAt
seasons/{id}/entries/{uid}/results/{matchId}  result, delta, matchStartedAt, createdAt (chống ghi trùng)
usernames/{name}                      uid  (đảm bảo username duy nhất)
users/{uid}.username                  "user" + 6 số ngẫu nhiên; user cũ được cấp lúc đăng nhập
```
Thứ tự: points DESC → winRate DESC → username ASC. `winRate` = wins*10000/matches (làm tròn xuống), LƯU trong Firestore (user quyết định 17/10 sau khi thử bỏ: không lưu thì phải đọc toàn bộ mùa để sort), Rules kiểm tra. Index: `entries` (points DESC, winRate DESC, username ASC), (points ASC, winRate ASC), (points ASC, winRate ASC, username ASC).
Mọi tài khoản luôn có hàng: đăng nhập/mở bảng xếp hạng tự tạo hàng trống (0 điểm = Đồng, 0 %); Cloud Code mỗi giờ tạo hàng trống cho mọi `users/*` và cấp username cho tài khoản cũ chưa có.

**Xếp hạng trận** — dedicated: tính (server ghi qua `ReportResult`). Host/Client: **không** tính từ plan 02
(`HostClientPointsEnabled` chỉ còn là cờ hiển thị). Matchmaker: `rank_tier` từ `GetTicketTier`, rule Dashboard Difference ≤ 1.

**UI** — `LeaderboardScene` (builder sinh): sidebar các mùa, tiêu đề "BẢNG XẾP HẠNG — MÙA N", đồng hồ đếm ngược,
cột STT | Username | Rank | Điểm | Tỉ lệ thắng, top 1–3 màu vàng/bạc/đồng, dòng của mình vàng nhạt.
Dòng ghim: khi dòng mình nằm ngoài vùng nhìn thấy thì ghim ở đáy; cuộn tới STT đó thì dòng ghim ẩn và dòng thật hiện
(chiều cao dòng cố định 76 + 6 → tính vị trí O(1), không cần đo layout).

**Giai đoạn 2 — đã làm (01/11/2026)**: server ghi kết quả qua Cloud Code, Rules chặn client cập nhật `entries`/`results`;
`rank_tier` lấy từ server. Xem §13.

## 13. Server tin cậy + vào trận có xác minh (đợt 1, merge 01/11/2026)

`[Đã kiểm chứng đầu–cuối]` Find Random 2 tài khoản trên Edgegap thật, 01/11/2026 (commit `a30cf3d`, `272bb39`).

**Cloud Code `MatchServerModule`** (`CloudCode/MatchServerModule`, code chung `CloudCode/Shared` link source)
- `GetTicketTier` (Player gọi): tính tier từ điểm mùa hiện tại; đồng thời ánh xạ PlayerId → uid bằng
  `GET player-auth /v1/users/{playerId}` (token của chính người chơi, externalIds `oidc-firebase`) và lưu
  `ugsPlayers/{playerId}` (server-only). **Client phải gọi trước khi tạo ticket**, nếu không allocator/AdmitPlayer từ chối.
- `AdmitPlayer` (chỉ server, service account): kiểm `MATCH_TOKEN`, player thuộc `matches/{matchId}`, JWT người chơi;
  trả uid + `ownedCardIds` để server dựng deck.
- `ReportResult` (chỉ server): áp `MatchRewardPolicy` + `RankPointsPolicy` + eligibility, ghi `matchRewards`, `entries`,
  `results`, `matches/{id}.settlement` trong một commit; idempotent theo `matchId` (báo lặp/đồng thời chỉ chốt một lần).
- Secret UGS: `MATCH_SERVER_SA_KEY_ID`, `MATCH_SERVER_SA_SECRET` (+ secret Firebase service account cũ).
- Client match server gọi Cloud Code REST bằng service account (`CloudCodeMatchResultReporter`): token-exchange cache
  50 phút, timeout 10 s/lần, ReportResult retry 2/4/8/16 s. Thiếu env → `NullMatchResultReporter` (chơi được, không
  thưởng/điểm); Editor/`MATCH_REPORTER_STUB=1` → `StubMatchResultReporter`.

**Firestore Rules (siết lại)** — client không ghi `matchRewards`, `results`, cập nhật/xóa `entries`, `matches/**`,
`ugsPlayers/**`, không tăng coins/silver. Còn: tạo profile, claim username, mua thẻ, tạo hàng `entries` rỗng.
`firestore.rules` sinh từ `RulesTemplate` trong `Assets/Editor/KLTN/ShopCatalogInstaller.cs` → sửa generator, không sửa tay.

**Listen-host (Host/Join)** — trận riêng: không thưởng, không điểm, loadout vẫn tin client; màn kết quả ghi rõ.
Màn kết quả có **HÒA** riêng (`MatchResultBanner`).

**Bài học vận hành khi test tay (01/11/2026)**
1. Module Cloud Code được đặt tên theo **assembly**, không theo tên file `.ccmr`: `Matchmaker.ccmr` deploy thành module
   **`EdgegapAllocator`**. Matchmaker pool phải trỏ đúng tên này.
2. **Access Control**: statement có resource kiểu "Modules" (Deny Player) chặn **mọi** module, kể cả `GetTicketTier`.
   Phải thêm statement **Allow** riêng cho `/**/modules/MatchServerModule/GetTicketTier`. Dashboard không xóa được
   statement cũ, nên cấu hình hiện tại = Deny mọi module + Allow riêng hàm trên. Thêm hàm Player-callable mới → thêm Allow.
3. Build profile **Linux Server** phải bật *Override Global Scene List* với `ServerBootstrap` **đứng đầu** + `GameScene`;
   nếu dùng scene list chung, server mở `LoginScene` và không listen → client không vào được.
4. Edgegap **gói Free chỉ 1 deployment** chạy cùng lúc; allocator luôn tạo deployment mới → terminate deployment cũ
   trước mỗi lần test (reuse server là plan 05).
5. **Cold start** Cloud Code ~10–12 s (module > 15 phút không dùng): `GetTicketTier` timeout phía client **15 s**
   (`MatchServerModuleClient`); lỗi/timeout → **dừng tìm trận** và báo người chơi (không còn fallback Bronze, vì thiếu
   `ugsPlayers/` thì AdmitPlayer sẽ từ chối). AdmitPlayer cũng có thể chậm → `ClientConnectionBufferTimeout` 15 s.
6. `DedicatedMatchmakerClient` tự dừng tìm trận sau **180 s** (`MaxSearchSeconds`), hủy ticket và báo lỗi.
7. `Claude outputs/` (báo cáo/tệp tạm do Claude tạo) đã vào `.gitignore`.

## 14. Client polish & resilience (plan 04, merge 01/11/2026)

- **Quên mật khẩu**: `IAuthService.SendPasswordResetEmailAsync` → `accounts:sendOobCode` (PASSWORD_RESET, locale vi);
  `EMAIL_NOT_FOUND` coi như thành công (không lộ email tồn tại). Nút thêm bằng menu
  `Tools/KLTN/Install Forgot Password Button (LoginScene)`.
- **Retry**: `NetworkRetryPolicy` (asmdef `KLTN.Infrastructure.Resilience`, 1 + 3 lần, 1/2/4 s + jitter ≤ 25 %, trần 8 s).
  Firestore đọc retry khi lỗi mạng/408/429/5xx; **commit** chỉ retry khi mọi write có precondition `updateTime` hoặc lỗi
  429/ABORTED (không retry mua hàng sau timeout — tránh trừ tiền 2 lần).
- **Auth timeout** 15 s mọi request Firebase/Google; chờ trình duyệt Google ≤ 180 s; không retry đăng ký và gửi email reset.
  Auto-login khi mất mạng không xóa phiên đã lưu.
- **Secret OAuth**: `clientSecret` chỉ ở `StreamingAssets/google-auth-config.json` (gitignore); có file `*.example.json`.
  Đã kiểm lịch sử git: chưa từng lộ.
- **Âm thanh**: `AudioVolumeSettings` (PlayerPrefs `MusicVolume`/`SFXVolume`, mặc định 0.7, event `Changed`);
  click UI theo SFX, nhạc menu theo Music; SFX ability hit dùng `abilityHitSfx` (trống → `attackSfx`).

## 15. Kế hoạch sau giữa kỳ (31/10/2026)

| Plan | Branch | Trạng thái |
|---|---|---|
| 01 Server tin cậy | `feature/server-trusted-backend` | Đã merge (01/11) |
| 02 Vào trận có xác minh | `feature/match-admission-loadout` | Đã merge (01/11) |
| 04 Client polish | `feature/client-polish-resilience` | Đã merge (01/11) |
| 03 Giữ ghế khi mất kết nối | `feature/reconnect-grace-period` | Đợt 2 |
| 05 Nhiều trận / reuse server | `feature/multi-match-server` | Đợt 3 |
| 06 Integration/system tests | `feature/integration-system-tests` | Đợt 3 |

Quy ước: mỗi plan một worktree + branch tách từ `main` mới nhất; agent **không commit, không merge**, người dùng tự merge.
