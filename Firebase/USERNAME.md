# Đổi username một lần

Đã triển khai source ngày 21/11/2026. Đăng nhập email/password và Firebase UID không đổi.
Đây không phải xác nhận đã deploy lên Firebase hoặc UGS.

## Hành vi

- MainMenu có nút cạnh tên, dùng nguyên asset người dùng cung cấp tại
  `Assets/Resources/UI/ChangeUsername.png`. Nhập tên ở modal đầu, chuyển sang modal xác nhận riêng rồi gửi. Hủy quay lại và giữ input.
- Tên mới dài 4–16 ký tự ASCII `A-Z`, `a-z`, `0-9` và dấu cách, bắt đầu bằng chữ.
  Giữ hoa/thường ở tên hiển thị; bỏ dấu cách đầu/cuối, gộp dấu cách liên tiếp.
  `usernameKey` và document ID trong `usernames` là tên hiển thị chuyển thành chữ thường.
  Tên cũ có `_` hoặc dài tới 20 ký tự vẫn đọc được, không cấp mới theo luật cũ.
  Không chọn `admin`, `support`, `system`, `moderator` hoặc mẫu `user` + số.
  Cấp tên ban đầu vẫn dùng cơ chế `user` + 6 chữ số và reservation duy nhất hiện có.
- Chỉ một lần đổi thực sự; reservation tên cũ được xóa trong cùng transaction để tái sử dụng.
  Gửi chính tên hiện tại khi còn lượt là no-op, không tốn lượt.
- Modal được lưu trong MainMenu tại `Canvas/UserNameChangeModal` (mặc định inactive).
  Khi đã dùng lượt, vẫn mở được modal: ô nhập điền tên hiện tại và chỉ đọc,
  hiện “Bạn đã thực hiện đổi tên rồi.”, khóa nút xác nhận. Backend vẫn thực thi luật cho mọi caller.

## Schema và migration tương thích

`users/{uid}`:

| Field | Trước đổi | Sau đổi |
|---|---|---|
| username | tên được cấp | tên hiển thị, giữ hoa/thường |
| usernameKey | có thể chưa có | khóa chữ thường, khoảng trắng chuẩn hóa |
| usernameVersion | 0 | 1 |
| usernameChangeCount | 0 | 1 |
| initialUsername | chưa cần | tên trước đổi |
| usernameChangedAt | chưa cần | thời điểm hiệu lực do backend gán |

Tài khoản cũ thiếu version/count được đọc là 0; không cần ghi lại toàn bộ database.
Lần đổi đầu tiên bổ sung history và version/count trong transaction. Profile mới của
client mới ghi version/count bằng 0. `schemaVersion` hiện tại không bị dùng lại làm
`usernameVersion`. Nếu reservation tên cũ không trỏ về đúng UID, backend báo lỗi
`profile_inconsistent`; cần audit/sửa dữ liệu quản trị, không chiếm tên hoặc ghi đè im lặng.

`usernames/{name}`: giữ `uid`, `createdAt`; rename tạo tên mới với
`status = active`, xóa reservation tên cũ nếu canonical key thay đổi. Đổi casing
trên cùng key không xóa reservation. Receipt replay không thực hiện lại thao tác xóa.
Mọi document đã tồn tại vẫn ngăn tài khoản khác lấy tên, kể cả reserved từ bản cũ.

`users/{uid}/usernameChangeRequests/{requestId}`:
`normalizedUsername`, `requestedDisplayName` (receipt mới), `success`, `error`, `username`, `usernameVersion`,
`usernameChangeCount`, `completedAt`.

Request ID là GUID dạng N, được scoped theo UID. Backend kiểm tra receipt trước quyền
đổi tên. Cùng ID/cùng payload trả lại kết quả đã lưu; khác payload bị từ chối.
Cả kết quả thành công lẫn từ chối nghiệp vụ được lưu; malformed request ID/payload quá
128 ký tự bị loại trước khi nhận xử lý. Receipt thành công, reservation, profile và
standing đều được commit cùng transaction. Không xóa receipt hoặc đặt TTL nếu còn cần
bảo đảm replay lâu dài.

Client giữ ID + tên trong PlayerPrefs theo UID; không chứa password/token. Timeout giữ
ID để retry sau khi mất mạng hoặc restart. Kết quả từ chối đã xác định kết thúc attempt;
lần thử tiếp theo tạo ID mới. Receipt thành công vẫn được giữ để replay.

## Backend và concurrency

- Endpoint player `MatchServerModule.ChangeUsername(requestId, username)`.
  UID được resolve từ UGS identity đã xác thực và link Firebase; không nhận UID từ payload.
- `FirestoreAdmin.RunTransactionAsync` dùng beginTransaction, batchGet có token,
  commit và rollback. Conflict retry toàn bộ read set, tối đa 5 attempts, backoff có jitter.
- Dedicated và listen settlement cùng đi qua transaction đọc profile/config/standing;
  không lấy tên từ entry cũ. Các cơ chế thưởng đúng một lần và quyền server giữ nguyên.
- SeasonRoster đọc lại profile trong transaction trước tạo row. Sửa winRate legacy
  có updateTime precondition để tránh ghi đè thống kê vừa settlement.
- Rename chỉ patch tên/version của entry hiện có: không sửa điểm, thống kê hoặc
  `updatedAt` (đang dùng cho cooldown xếp hạng).

## Chính sách mùa

- Thời điểm hiệu lực rename lấy từ đồng hồ backend trong attempt transaction thành công,
  không nhận từ client. Đây là mốc nghiệp vụ `usernameChangedAt`, không phải Firestore
  metadata updateTime. Transaction có thể hoàn tất sau mốc hiệu lực này.
- Rename cập nhật/tạo row của mùa chứa mốc hiệu lực, theo lịch active/pending hiện có.
- Mùa kết thúc giữ nguyên tên đã có. Khi phải tạo/ghi row lịch sử, helper
  `UsernameAtEnd`/`UsernameVersionAtEnd` dùng history một lần đổi:
  đổi tại/sau end → tên ban đầu; đổi trước end → tên mới.
- Mùa mới tạo row bằng tên mới. Không sửa eligibility hoặc mở lại điểm mùa cũ:
  settlement đến muộn vẫn theo luật xếp hạng hiện có.
- Rename làm mất cache own-row và cursor trang đang mở. Cần tải lại bảng/vị trí vì
  username vẫn là tiêu chí phụ sau points và winRate.

## Triển khai lên môi trường thật

1. Build/test hai module từ source mới. Deploy **SeasonModule trước**, rồi
   **MatchServerModule** trước khi phát hành client có nút rename. Không để SeasonRoster
   cũ chạy cùng feature mới: validator cũ chỉ chấp nhận tên dạng user + 6 số.
2. Cho player đã xác thực gọi `MatchServerModule.ChangeUsername` trong UGS Access Control
   nếu policy hiện tại là allowlist. Không mở các endpoint server-only
   `AdmitPlayer`, `ReportResult`, `GetAssignedMatches` cho player.
   Endpoint mới dùng secret service account sẵn có; không đưa secret vào client.
3. Deploy `Firebase/firestore.rules` đã đồng bộ với
   `Assets/Editor/KLTN/ShopCatalogInstaller.cs`. Không có index mới.
   Rules cho client đọc receipt của chính mình, cấm ghi receipt/history/version/count
   ngoài khởi tạo count/version bằng 0. Tên tùy chọn chỉ do backend cấp.
4. Phát hành client mới. Trong Editor, menu **Tools/KLTN/Install Username Change**
   dựng và serialize modal/kiểm tra references/render preview; button đã có trong
   MainMenu cùng modal và các references; runtime helper chỉ dựng modal nếu còn thiếu.
5. Kiểm tra với tài khoản thử: đổi tên, reload/logout/login, retry request, vào leaderboard,
   chơi trận, kiểm tra tên mùa trước và sau ranh giới mùa. Không rollback về client/module
   validator cũ sau khi đã có tên tùy chọn nếu chưa chuẩn bị phương án tương thích.

## Kiểm chứng local

- `Firebase/rules.test.mjs`: chạy `npm test` trong Firebase (cần Java).
- MatchServerModule.Tests: dùng Firestore Emulator demo-kltn, chạy
  `dotnet run --project CloudCode/MatchServerModule/MatchServerModule.Tests/MatchServerModule.Tests.csproj`.
  Bao gồm tranh tên, tranh lượt đổi, request replay/payload mismatch, no-op, boundary/pending
  calendar, giữ wallet/cooldown và rename đồng thời settlement.
- SeasonModule.Tests: runner tương tự trong `CloudCode/SeasonModule/SeasonModule.Tests`,
  gồm late roster tên lịch sử và tên tùy chọn ở mùa sau.
- `dotnet run --project CloudCode/UsernameClient.Tests/UsernameClientCheck.csproj`: kiểm tra
  production client giữ request ID qua lỗi mạng/replay, tách UID, đổi payload và xử lý
  kết quả từ chối. Dùng SDK/PlayerPrefs giả, không gọi tài khoản thật.
- Hai runner emulator xóa dữ liệu **demo-kltn local** để tạo fixture; không chạy đồng thời
  với nhau hoặc rules tests trên cùng emulator.
- Máy kiểm chứng có .NET 9; tests target net8 được chạy với
  `DOTNET_ROLL_FORWARD=Major`. Java 21 dùng riêng cho tiến trình test.
- C# client/editor/server đã được kiểm tra qua các DLL Unity 6000.5.4f1 trong workspace;
  không thay thế Unity build hoặc Play Mode. Server có hai warning unreachable-code
  từ bootstrap `if (true)` có sẵn.
- MainMenu: kiểm tra ID không trùng, references mới hợp lệ, parent/child kết nối,
  asset có cùng SHA-256 với ảnh gốc. Rules username khớp nguồn sinh.
- Unity batch không chạy được vì license Editor chưa hợp lệ; chưa render preview hoặc
  xác minh tương tác UI thực tế. Không dùng computer-use.

## Cập nhật định dạng 21/11/2026

- Modal 1 kiểm tra validation rồi GET `usernames/{canonicalKey}` bằng Firebase token; có reservation thì báo tên đã dùng, lỗi mạng/quyền thì ở lại modal 1. Không cần endpoint UGS mới.
- Backend kiểm tra trùng lại trong transaction. `username`/standing/history giữ tên hiển thị; reservation dùng khóa chữ thường. Receipt mới có `requestedDisplayName` để ràng buộc cả casing; receipt cũ vẫn replay tương thích qua `normalizedUsername`.
- Deploy SeasonModule rồi MatchServerModule trước client mới. Rules không đổi: quyền get reservation đã tồn tại và usernameKey chỉ backend ghi được. Không cần migration các tên lowercase cũ.
- Thông báo validation màu đỏ; đổi lại màu bình thường khi nhập lại hoặc hiện trạng thái/thành công. Giới hạn input không cắt âm thầm ở 16 để người dùng nhận được lỗi độ dài.

Kiểm chứng cập nhật: 118 backend checks, 63 Firestore Rules checks, 15 season checks,
38 policy/ranking checks và 8 client retry checks thành công. C# client/editor compile
không warning/error. Chưa kiểm tra UI trong Play Mode hoặc deploy bản định dạng mới.

## Giải phóng tên cũ

Deploy lại MatchServerModule để các lần đổi tiếp theo tự xóa reservation cũ trong
cùng transaction với profile, reservation mới, standing và receipt. UID/history không đổi.
Các document status=reserved đã tồn tại từ bản cũ không tự biến mất sau deploy.
Chỉ dọn bằng quyền quản trị sau khi kiểm tra users/{uid} của reservation đang dùng
một canonical key khác; không xóa nếu hồ sơ thiếu hoặc còn dùng chính key đó.
Nếu dọn bằng script, phải đọc reservation và profile rồi xóa trong cùng transaction
để tránh xóa nhầm tên vừa được người khác lấy. Chưa dọn dữ liệu production.
Tên được giải phóng vẫn phải đáp ứng quy tắc tên mới khi chọn thủ công; mẫu user+số
vẫn dành cho bộ cấp tên tự động, tên legacy chứa underscore không được chọn mới.
