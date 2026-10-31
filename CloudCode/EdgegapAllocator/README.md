# EdgegapAllocator — cấp phát match server cho UGS Matchmaker (Cloud Code, C#)

Dựa trên mẫu Unity *Matchmaker Hosting Providers* (`modules/EdgegapAllocator`, giấy phép trong `LICENSE.md`,
hướng dẫn gốc trong `CONFIGURATION.md`). Trước đây module nằm ở thư mục Downloads; nay `Assets/CloudCode/Matchmaker.ccmr`
trỏ tương đối tới `CloudCode/EdgegapAllocator/EdgegapAllocator.sln`.

## Thay đổi so với mẫu

`Matchmaker_AllocateServer`:

1. Lấy 2 UGS PlayerId từ `MatchProperties.Players`.
2. **Kiểm tra lại tier** (FR-RANK-12): đọc `ugsPlayers/{playerId}` → uid → `seasons/{mùa}/entries/{uid}.points`
   → `RankPolicy.TierFor`. Lệch > 1 bậc (`RankMatchmakingPolicy`) hoặc người chơi chưa gọi
   `MatchServerModule/GetTicketTier` → trả `AllocateStatus.Error` (ticket thất bại). `rank_tier` client khai
   trong ticket chỉ còn là gợi ý cho rule của pool.
3. Sinh `matchId` (GUID "N") và `MATCH_TOKEN` (32 byte ngẫu nhiên, base64url), ghi
   `matches/{matchId} = { players, tiers, tokenHash (SHA-256 hex), serverId: "", ranked: true, matchmakerMatchId, createdAt }`
   **trước** khi tạo deployment (server có thể admit ngay khi khởi động), rồi cập nhật `serverId` = Edgegap `request_id`.
4. Tạo deployment Edgegap với biến môi trường (biến bí mật để `is_hidden`):

   | Biến | Giá trị |
   |---|---|
   | `MATCH_ID` | `matchId` ở trên (khác `MatchId` của Matchmaker, được lưu ở `matchmakerMatchId`) |
   | `MATCH_TOKEN` | token trận (ẩn) |
   | `UGS_PROJECT_ID`, `UGS_ENVIRONMENT_ID` | lấy từ context Cloud Code |
   | `UGS_SERVICE_ACCOUNT_KEY_ID`, `UGS_SERVICE_ACCOUNT_SECRET` | từ secret `MATCH_SERVER_SA_KEY_ID`, `MATCH_SERVER_SA_SECRET` (ẩn) |

5. `AllocationData` có thêm `matchId`. `Matchmaker_PollAllocation` giữ nguyên.

Việc chọn server tách ra `PlaceMatchAsync` (hiện luôn tạo deployment mới) để plan 05 mở rộng sang dùng lại server còn chỗ.

## Secret cần có (Dashboard → Administration → Secrets)

| Secret | Nội dung |
|---|---|
| `EDGEGAP_API_TOKEN_KXD` | token API Edgegap (đã có) |
| `FIREBASE_SERVICE_ACCOUNT` | JSON service account Firebase (đã có từ SeasonModule) |
| `MATCH_SERVER_SA_KEY_ID` | key id của UGS service account cho match server (mới) |
| `MATCH_SERVER_SA_SECRET` | secret của key đó (mới) |

Không cần khai báo biến trên Dashboard Edgegap: allocator truyền theo từng deployment. Chỉ cần đảm bảo app version
`kltn` / `VersionName` trong `Project/EdgegapAllocator.cs` không đặt sẵn biến trùng tên.

## Deploy

Unity → Services → Deployment → tick `Matchmaker.ccmr` → Deploy Selected (hoặc `ugs deploy Assets/CloudCode/Matchmaker.ccmr`).
Cần .NET SDK 9+ (module target `net9.0` như mẫu). Publish profile đã sửa từ `net5.0` (lỗi sẵn trong mẫu) thành `net9.0`.

## Chưa kiểm chứng trên UGS thật

- Deploy module từ đường dẫn tương đối mới và việc Matchmaker gọi allocator sau khi đổi.
- Kiểu `Players` trong `MatchProperties` (mẫu dùng `JArray` → `List<Player>`; `Player.Id` là UGS PlayerId).
- Edgegap ẩn đúng các biến `is_hidden` trên dashboard.
- Logic tier/`matches` được test qua `CloudCode/MatchServerModule/MatchServerModule.Tests` (code dùng chung
  `CloudCode/Shared`); bản thân hàm allocator chưa có test chạy với Matchmaker thật.
