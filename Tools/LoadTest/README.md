# Test tải nhiều trận / một server (plan 05 D, đầu vào cho I6)

Mục tiêu: xác nhận `MATCH_CAPACITY` đề xuất (SDS HLD 10.2: "the capacity is confirmed by the load test in I6")
bằng cách chạy N cặp người chơi giả vào **một** process server và đo CPU/RAM theo số trận.

## Chuẩn bị

1. Build **Windows Dedicated Server** (Build Profiles → Windows Server; scene list: `ServerBootstrap` rồi `GameScene`,
   giống build Linux Server). Cùng một bản build dùng làm server và làm bot.
2. Bot là `LoadTestClient` (`Assets/Scripts/Infrastructure/Networking/Dedicated/LoadTestClient.cs`), bật bằng tham số
   `-loadTestClient host:port`: vào trận bằng danh tính giả, giữ bài mở đầu, cứ `-loadTestActionSeconds` giây
   bấm Pass, sau `-loadTestMatchSeconds` giây thì đầu hàng rồi thoát. Server phải chạy `MATCH_REPORTER_STUB=1`
   (script tự đặt) nên không ghi gì vào Firestore và nhận mọi người chơi.
3. Server ở chế độ không có Cloud Code (không có `SERVER_ID`): người chơi được ghép theo thứ tự vào (bot 1+2 → trận 1,
   bot 3+4 → trận 2, ...). Quá `MATCH_CAPACITY` thì bị từ chối "Trận đấu đã đủ người".

## Chạy

```powershell
cd Tools/LoadTest
./run-loadtest.ps1 -Build "D:\Builds\KLTN-Server\KLTN.exe" -Pairs 1 -Capacity 1   # mốc 1 trận
./run-loadtest.ps1 -Build "D:\Builds\KLTN-Server\KLTN.exe" -Pairs 2 -Capacity 2
./run-loadtest.ps1 -Build "D:\Builds\KLTN-Server\KLTN.exe" -Pairs 4 -Capacity 4
./run-loadtest.ps1 -Build "D:\Builds\KLTN-Server\KLTN.exe" -Pairs 8 -Capacity 8
```

Mỗi lần chạy tạo `results-<thời gian>/`: `server.log`, `bot-*.log`, `server-usage.csv` (CPU % trên toàn bộ core,
RAM working set/private theo thời gian) và `summary.json` (số trận đã bắt đầu, CPU trung bình/đỉnh, RAM đỉnh, số dòng lỗi).

Bot chạy cùng máy sẽ tranh CPU với server. Để số đo sạch hơn: chạy server trên máy A
(`MATCH_CAPACITY=8`, `MATCH_REPORTER_STUB=1`, `KLTN.exe -batchmode -nographics`), rồi trên máy B chạy
`./run-loadtest.ps1 -Build ... -Pairs 8 -ServerHost <IP máy A>` (khi đó script chỉ chạy bot; đo CPU/RAM của server
bằng Task Manager/`Get-Process` trên máy A).

## Trên Edgegap

Container Edgegap dùng build Linux. Cách nhanh: deploy thủ công một phiên bản với biến môi trường
`MATCH_CAPACITY=4`, `MATCH_REPORTER_STUB=1` (không có `MATCH_ID`), rồi chạy script với `-ServerHost <IP>` và
`-Port <cổng ngoài>`; đọc CPU/RAM ở trang Deployment → Metrics của Edgegap. So với giới hạn vCPU/RAM của gói đang dùng.

## Đọc kết quả

- Capacity hợp lý là số trận lớn nhất mà CPU đỉnh của server còn dưới ~70 % phần vCPU được cấp và RAM còn dư,
  và log server không có lỗi/timeout. Trận chủ yếu chờ người chơi nên CPU thấp; chi phí lớn nhất là dựng và gửi
  snapshot JSON sau mỗi lệnh (tỉ lệ thuận với số lệnh/giây).
- Ghi bảng: số trận | CPU TB | CPU đỉnh | RAM đỉnh | lỗi, rồi chọn `MATCH_SERVER_CAPACITY` (secret của allocator)
  và `MATCH_CAPACITY` tương ứng.
