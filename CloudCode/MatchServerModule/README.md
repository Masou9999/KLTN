# MatchServerModule — trận đấu server-trusted (UGS Cloud Code, C#)

Thưởng và điểm xếp hạng **không còn do client ghi**. Match server (Edgegap) báo kết quả cho Cloud Code,
Cloud Code tính bằng chính policy của game (link source từ `Assets/Scripts/Game/Domain`) và ghi Firestore
bằng service account. Firestore Rules chặn mọi client ghi thưởng/điểm (`Firebase/firestore.rules`).

```text
Client ──GetTicketTier()──────────────► MatchServerModule  (UGS player token)
   │   tier thật từ seasons/{id}/entries/{uid}.points → rank_tier của ticket
   ▼
Matchmaker ──► EdgegapAllocator.Matchmaker_AllocateServer
                 đọc lại tier 2 người (lệch > 1 bậc → từ chối), sinh MATCH_ID + MATCH_TOKEN,
                 ghi matches/{matchId} {players, tokenHash, serverId, createdAt, ranked}
                 → Edgegap deployment với MATCH_ID, MATCH_TOKEN, UGS_* (service account)
Match server ──AdmitPlayer(matchId, matchToken, playerId, playerAccessToken)──► loadout
Match server ──ReportResult(matchId, matchToken, host, guest, outcome, ...)──► settlement
```

## Hàm

| Hàm | Ai gọi | Tham số | Kết quả |
|---|---|---|---|
| `GetTicketTier` | Người chơi | không | `{ tier, seasonId }` |
| `AdmitPlayer` | Match server (service account) | `matchId, matchToken, playerId, playerAccessToken` | `{ admitted, reason, uid, username, ownedCardIds, tier }` |
| `ReportResult` | Match server (service account) | `matchId, matchToken, hostPlayerId, guestPlayerId, outcome ("HostWon"/"GuestWon"/"Draw"), surrenderedPlayerId (null nếu không), roundNumber, matchStartedUtcMs` | `{ alreadyReported, players: [ { playerId, result, coins, silver, ranked, rankDelta, pointsBefore, pointsAfter, tierBefore, tierAfter, notScoredReason } ] }` |

- `AdmitPlayer` trả `admitted = false` kèm `reason`: `unknown_match`, `bad_token`, `not_in_match`,
  `match_finished`, `bad_player_token:<lý do>` (`bad_signature`, `expired`, `wrong_player`, `wrong_project`,
  `unknown_key`, ...), `no_firebase_link`, `no_profile`.
- `ReportResult` từ chối vĩnh viễn bằng exception có message `rejected:<lý do>` (`unknown_match`, `bad_token`,
  `not_in_match`, `bad_outcome`, `bad_players`, `bad_surrendered_player`, `unknown_player:<id>`, `same_account`,
  `no_profile:<id>`). Gặp `rejected:` thì match server **không retry**; lỗi khác (mạng, 5xx) thì retry được vì
  hàm idempotent.
- `result`: `win`, `loss`, `draw`, `surrender_early` (tự đầu hàng trước hiệp 3: 0 xu), `surrender`.
- `rankDelta = pointsAfter - pointsBefore` (thua khi còn 10 điểm → `-10`, điểm không âm).
- `notScoredReason` (null khi có tính điểm): `unranked`, `no_active_season`, `started_before_season`,
  `started_after_lock`, `finished_after_season_end`, `cooldown` (< 60 s từ trận tính điểm trước, FR-RANK-06),
  `already_scored`. Trận không tính điểm vẫn được thưởng.

### ReportResult ghi gì (một commit Firestore)

- `users/{uid}`: `coins`/`silver` tăng bằng *increment* phía server, `lastRewardMatchId`, `updatedAt`.
- `users/{uid}/matchRewards/{matchId}` `{ result, coins, silver, createdAt }` (precondition chưa tồn tại).
- `seasons/{id}/entries/{uid}` `{ username, points, wins, losses, draws, matches, winRate, lastMatchId, updatedAt }`
  và `.../results/{matchId}` `{ result, delta, matchStartedAt, createdAt }` — chỉ khi trận ranked, bắt đầu trong
  `[start, lock)` của mùa đang chạy lúc báo cáo, và cách trận tính điểm trước ≥ 60 s.
- `matches/{matchId}`: `reportedAt`, `outcome`, `roundNumber`, `matchStartedAt`, `settlement` (chuỗi JSON của kết quả trả về).

Báo cáo lặp lại (match server retry) trả lại `settlement` đã lưu với `alreadyReported = true` (FR-SHOP-08).
Hai báo cáo đồng thời: precondition `updateTime` của `matches/{matchId}` chỉ cho một commit; cái còn lại đọc lại
và trả `alreadyReported`. `matchStartedUtcMs` vô lý (trước lúc cấp phát − 120 s hoặc sau hiện tại + 120 s) được
thay bằng `createdAt` của bản ghi cấp phát.

## UGS PlayerId → Firebase uid (phương án đã chọn)

Người chơi đăng nhập UGS bằng OpenID Connect `oidc-firebase` (`AuthManager.LoginFirebaseToUGS`), nên tài khoản UGS
có `externalIds` chứa Firebase uid. Module đọc nó bằng **token của chính người chơi** qua
`GET https://player-auth.services.api.unity.com/v1/users/{playerId}` (header `ProjectId`), là lời gọi phía sau
`AuthenticationService.GetPlayerInfoAsync`. Không cần quyền Admin API.

Kết quả được lưu `ugsPlayers/{playerId} = { uid, updatedAt }` (chỉ server đọc/ghi). `GetTicketTier` (token người
chơi do Cloud Code xác thực) và `AdmitPlayer` (sau khi đã kiểm JWT) ghi bản ghi này; allocator và `ReportResult`
chỉ đọc. Vì vậy **client phải gọi `GetTicketTier` trước khi tạo ticket** (plan 02), nếu không allocator từ chối.

`playerAccessToken` trong `AdmitPlayer` được kiểm offline: chữ ký RS256 theo JWKS
`https://player-auth.services.api.unity.com/.well-known/jwks.json` (cache 1 giờ), `iss`, `sub == playerId`,
`exp`/`nbf` (lệch 30 s), project (`project_id` hoặc `aud` chứa `upid:{projectId}` / `idd:{projectId}`).

## Cài đặt (làm một lần)

1. **Secret** (Dashboard → Administration → Secrets): `FIREBASE_SERVICE_ACCOUNT` (đã có từ SeasonModule).
2. **Deploy**: Unity → Services → Deployment → tick `MatchServerModule.ccmr` → Deploy Selected
   (hoặc `ugs deploy Assets/CloudCode/MatchServerModule.ccmr`). Cần .NET SDK 6+.
3. **Service account cho match server**: Dashboard → Administration → Service Accounts → tạo account
   (ví dụ `match-server`), thêm quyền gọi Cloud Code của project/environment, tạo key. Lưu key id/secret
   vào Secret Manager `MATCH_SERVER_SA_KEY_ID`, `MATCH_SERVER_SA_SECRET` (allocator chuyển vào container,
   xem `CloudCode/EdgegapAllocator/README.md`).
4. **Chặn người chơi gọi hàm server** (Access Control → policy), giống bước 6 của SeasonModule:

   ```json
   {
     "statements": [
       {
         "Sid": "deny-player-match-server-functions",
         "Action": ["*"],
         "Effect": "Deny",
         "Principal": "Player",
         "Resource": "urn:ugs:cloud-code:/v1/projects/*/modules/MatchServerModule/AdmitPlayer"
       },
       {
         "Sid": "deny-player-report-result",
         "Action": ["*"],
         "Effect": "Deny",
         "Principal": "Player",
         "Resource": "urn:ugs:cloud-code:/v1/projects/*/modules/MatchServerModule/ReportResult"
       }
     ]
   }
   ```

   Hàm cũng tự từ chối khi context có PlayerId. **Không** chặn `GetTicketTier`.
5. **Firestore Rules**: dán `Firebase/firestore.rules` vào Firebase Console → Firestore → Rules (hoặc
   `firebase deploy --only firestore:rules`). Deploy Cloud Code + Rules **trước** khi phát hành client mới,
   và merge cùng plan 02 (client cũ tự ghi thưởng sẽ bị Rules chặn).

Match server gọi hàm bằng service account: đổi key lấy token qua
`POST https://services.api.unity.com/auth/v1/token-exchange?projectId={UGS_PROJECT_ID}&environmentId={UGS_ENVIRONMENT_ID}`
(Basic `keyId:secret`), rồi
`POST https://cloud-code.services.api.unity.com/v1/projects/{projectId}/environments/{environmentId}/modules/MatchServerModule/{hàm}`
với body `{ "params": { ... } }` (phần này do plan 02 cài đặt).

## Test trên emulator (không cần UGS)

```bash
cd Firebase && npm install && npx firebase emulators:start --only firestore --project demo-kltn
# terminal khác:
cd CloudCode/MatchServerModule && dotnet run --project MatchServerModule.Tests
```

40 kiểm tra: tier cho ticket và bản ghi `ugsPlayers`, quy tắc lệch bậc của allocator; admission với token trận sai,
trận lạ, người chơi lạ, JWT của người khác / hết hạn / sai project / chữ ký giả, loadout đúng; báo cáo với token
sai, người chơi lạ, outcome sai; thắng/thua (+30/−20, Vàng → Bạc), thưởng 75 xu + 1 bạc / 25 xu, báo cáo lặp lại
không trả thưởng 2 lần, 3 báo cáo đồng thời chỉ chốt 1 lần, hòa, chặn 60 s, đầu hàng sớm/muộn, sàn điểm 0, ngoài
cửa sổ mùa (trước mùa, sau khóa, sang mùa sau, chưa có lịch), `matchStartedUtcMs` vô lý, trận không xếp hạng.
UGS được giả lập: JWT ký bằng khóa RSA cục bộ, tra cứu `oidc-firebase` bằng dictionary.

## Code dùng chung

- Domain (link source, không copy): `MatchRewardPolicy.cs`, `EconomyModels.cs`, `RankPointsPolicy.cs`,
  `RankTier.cs`, `SeasonWindow.cs`, `SeasonStanding.cs`, `Match/SeatId.cs`, `Cards/CardModels.cs` (`MatchOutcome`).
- `CloudCode/Shared` (link source vào cả 3 module): `GoogleServiceAccount`, `FirestoreAdmin`,
  `SeasonDocuments` (đọc `config/season`, tier theo điểm), `MatchRecords` (`matches/{id}`, token, `ugsPlayers`).

## Chưa kiểm chứng trên UGS thật

- Endpoint `GET /v1/users/{playerId}` của player-auth với token người chơi và tên trường `externalIds[].providerId`
  / `externalId` (giả định `oidc-firebase` / Firebase uid). Nếu khác, chỉ cần sửa `UgsPlayerAuthLookup`;
  phương án thay thế là Player Authentication Admin API (cần service account có quyền Admin).
- Claim trong JWT của UGS (`iss`, `aud` dạng `upid:`/`idd:`, `project_id`) và URL JWKS.
- Tên thuộc tính JSON trả về: đã gắn `[JsonProperty]` (Newtonsoft, mà Cloud Code Core phụ thuộc) để giữ camelCase.
- Cloud Code bind tham số `long matchStartedUtcMs` và `string surrenderedPlayerId = null` từ `params`.
- Quyền tối thiểu của service account để gọi Cloud Code, cú pháp Resource của Access Control.
