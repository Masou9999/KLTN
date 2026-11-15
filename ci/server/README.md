# KLTN: Unity Dedicated Server → Docker → Edgegap

Pipeline chỉ giữ **một App Version**. Không cần backend theo dõi GitHub.
GitHub Actions nhận job; PC Windows của bạn chạy self-hosted runner để build.

## Luồng phát hành

1. Checkout commit vào workspace riêng của runner.
2. Chạy kiểm thử offline của script release và allocator.
3. Unity 6000.5.4f1 build Linux x86_64 Dedicated Server, chỉ gồm:
   - `Assets/Scenes/ServerBootstrap.unity` (scene khởi động).
   - `Assets/Scenes/GameScene.unity`.
4. Docker build, chạy thử local và kiểm tra log đã mở UDP 7777.
5. Push image tag `sha-<12 ký tự commit>` lên Edgegap registry.
6. Kiểm tra API và tag mới tồn tại; nếu lỗi thì chưa đụng version cũ.
7. Disable app `kltn`, chặn allocator nhận match mới, dừng deployment thuộc app.
8. Xóa version cũ → chờ xóa xong → tạo version mới.
9. Enable app; version mới có `KLTN_RELEASE_READY=false`, allocator vẫn bị chặn.
10. Deploy thử, chờ Edgegap báo `Status.READY` và có mapping `gameport` UDP 7777.
11. Dừng deployment thử để trả slot free, đổi marker thành `true` cho matchmaking.
12. Xóa tag image của version cũ nếu cùng registry/repository. Giữ image mới.

**Sau khi workflow thành công, Versions có một bản mới; Deployments không giữ server thử.**
Find Match sẽ gọi allocator để tạo deployment thật cùng MATCH_TOKEN/UGS credentials.
Kiểm tra này không thay thế một trận thật với hai client (admission, Cloud Code, kết quả trận).
Dừng deployment cũ sẽ ngắt các trận đang chạy. Đây là pipeline cho môi trường test,
không phải triển khai production không gián đoạn.

## Những việc bạn làm thủ công, theo thứ tự

### 1. Chuẩn bị máy runner

- Unity **6000.5.4f1**, license đã kích hoạt cho tài khoản chạy runner.
- Cài module **Linux Dedicated Server Build Support** trong Unity Hub.
  Nếu cấu hình scripting backend là IL2CPP, cần cả toolchain Linux tương ứng.
- Docker Desktop đã khởi động, chọn **Linux containers**.
- Git + Git LFS, PowerShell 7 (`pwsh`), Python 3.10+ có trong PATH của runner.
- .NET **9 SDK** để build/publish Cloud Code allocator một lần ở bước 3.
- Cài runner tại thư mục riêng, ví dụ `D:\actions-runner-kltn`; không trỏ `_work`
  vào project bạn đang mở trong Unity. Không mở checkout của runner trong Editor.
- GitHub repo → Settings → Actions → Runners → New self-hosted runner → Windows x64.
  Làm theo lệnh GitHub cung cấp; thêm custom label **`kltn-server`**.
- Ban đầu chạy `run.cmd` bằng tài khoản có license Unity và quyền Docker.
  Nếu chuyển sang Windows service, phải kiểm tra lại license/Docker/PATH của service account.
- Chỉ dùng runner này cho code tin cậy. Workflow không chạy trên pull request/fork.

Máy phải bật trong lúc chạy job. `Library` giữ trên đĩa runner để tái sử dụng lần sau;
`Builds/LinuxServer` được xóa trước mỗi build nhằm không đóng gói file cũ.

### 2. Kiểm tra Edgegap và cấu hình version

- App phải có tên **`kltn`**, còn không quá một version trước lần chạy đầu.
  Nếu có hai version, tự xác định bản cần giữ rồi xóa bản không dùng. Script không tự đoán.
- Mở `ci/server/version.json`, đối chiếu cấu hình đang chạy được trong dashboard:
  CPU 1536 units (1.5 vCPU), RAM 3072 MiB, UDP 7777, port name **`gameport`**.
- File này là cấu hình nguồn cho version mới; không tự sao chép những cấu hình đặc biệt
  từ version cũ. Cần thêm lại endpoint storage hoặc biến môi trường không bí mật nếu bạn đang dùng.
- Không đưa service account, MATCH_TOKEN hoặc bí mật vào JSON/Docker image.
  Allocator truyền các biến bí mật theo từng deployment giống luồng hiện tại.
- Registry phải còn chỗ cho **image cũ + image mới** trong lúc push. Quota App Version
  và dung lượng registry là hai giới hạn riêng. Nếu hết chỗ, push thất bại và bản cũ giữ nguyên.

### 3. Deploy allocator đã cập nhật một lần

`CloudCode/EdgegapAllocator/Project/Client/ReleasedVersion.cs` đọc version trực tiếp từ
Edgegap. Nó yêu cầu app enabled, đúng một version active và biến môi trường của version
`KLTN_RELEASE_READY=true` (không hidden). Không còn hardcode tên version timestamp.

Để chuyển đổi mà vẫn dùng bản server hiện tại:

1. Trong version đang hoạt động trên Edgegap, thêm env **`KLTN_RELEASE_READY=true`**, không hidden.
2. Build/publish module **EdgegapAllocator** đã sửa lên đúng UGS environment bằng cách bạn đang dùng.
   Có thể kiểm tra compile trước bằng:

   ```powershell
   dotnet build CloudCode/EdgegapAllocator/Project/EdgegapAllocator.csproj -c Release
   ```

3. Giữ nguyên Unity secret **`EDGEGAP_API_TOKEN_KXD`** đang dùng, có quyền đọc app/version
   và deploy. Token ở module hiện tại cần đúng định dạng Authorization Edgegap (`token ...`).
4. Thử Find Match bằng server hiện tại. Sau khi xác nhận module mới hoạt động, mới đặt
   GitHub variable `EDGEGAP_ALLOCATOR_READY=true` ở bước sau.

Không cần tạo hay cập nhật Unity secret `EDGEGAP_VERSION_NAME` mỗi lần release.
Unity không công bố API quản trị Secret Manager trực tiếp, nên cơ chế đọc version từ Edgegap
thay thế đề xuất cập nhật secret trong kế hoạch ban đầu.

### 4. Thêm GitHub Actions Secrets và Variables

Repo → **Settings → Secrets and variables → Actions**.

**Repository secrets:**

| Tên | Giá trị |
| --- | --- |
| `EDGEGAP_API_TOKEN` | Token Edgegap có quyền đọc/sửa app/version, deploy/stop và quản lý registry tags; script chấp nhận có/không tiền tố `token ` |
| `EDGEGAP_REGISTRY_USERNAME` | Username registry có quyền push và pull, không phải username đăng nhập dashboard |
| `EDGEGAP_REGISTRY_PASSWORD` | Password/token registry tương ứng |

**Repository variables:**

| Tên | Ví dụ/giá trị |
| --- | --- |
| `UNITY_EDITOR_PATH` | `C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe` |
| `EDGEGAP_APP_NAME` | `kltn` |
| `EDGEGAP_IMAGE_REPOSITORY` | `registry.edgegap.com/<project>/<image>`; không thêm `:tag` |
| `EDGEGAP_REGISTRY_IMAGE_NAME` | Identifier image mà API `/v1/container-registry/images/{image_name}/tags` sử dụng; lấy đúng image tương ứng trong registry dashboard/API, không tự giả định bằng tên app |
| `EDGEGAP_ALLOCATOR_READY` | `true`, chỉ sau khi hoàn thành bước 3 |

`EDGEGAP_REGISTRY_IMAGE_NAME` phải trỏ cùng image với `EDGEGAP_IMAGE_REPOSITORY`.
Script kiểm tra tag mới qua API này trước khi xóa version. Không commit các credential.

### 5. Commit/push và chạy lần đầu

Commit các file CI, `Assets/Editor/CI.meta`, thư mục `Assets/Editor/CI` và thay đổi allocator.
Đừng vô tình commit các thay đổi scene/UI khác nếu chưa muốn phát hành chúng.

Workflow `workflow_dispatch` cần có trên default branch để GitHub hiện nút chạy.
Vào **Actions → Build and deploy KLTN server → Run workflow**, chọn nhánh/commit cần build,
để trống **deploy_existing**. Runner phải có đủ labels `self-hosted`, `Windows`, `X64`, `kltn-server`.

Theo dõi từng job. Sau khi thành công, mở game và thử Find Match với hai client.
Log build/local container được upload thành artifact giữ 3 ngày; lưu trữ Actions có quota riêng.
Không upload toàn bộ build hay Library lên GitHub.

### 6. Bật tự chạy khi push sau khi đã thử thành công

Hiện chỉ có nút Run workflow để chưa tự phát hành trong lúc cấu hình.
Khi muốn tự chạy, thêm `push` bên cạnh `workflow_dispatch`:

```yaml
on:
  push:
    branches: [deploy/server]
    paths:
      - 'Assets/**'
      - 'Packages/**'
      - 'ProjectSettings/**'
      - 'ci/server/**'
      - '.github/workflows/deploy-server.yml'
  workflow_dispatch:
    inputs:
      deploy_existing:
        description: 'Recovery: existing tag sha-xxxxxxxxxxxx'
        required: false
        type: string
```

Push thay đổi allocator không tự publish Cloud Code; cần publish module nếu sau này sửa nó.
Không tự hủy job đang deploy; concurrency group `edgegap-kltn-server` tuần tự hóa release.
Không deploy thủ công trên dashboard cùng lúc workflow đang chạy.

## Khi workflow thất bại

- **Build/local smoke/push/preflight lỗi:** version Edgegap cũ chưa bị thay.
- **Đã bắt đầu thay version:** script cố disable app, stop deployment của app,
  giữ image cũ; không công bố marker ready. Xem dashboard nếu mất mạng hoặc job bị hủy cứng.
- **Lỗi tạo/deploy v2:** sửa cấu hình, Run workflow với `deploy_existing=sha-...` của image
  đã push để thử lại; không cần build Unity. Thao tác này vẫn dừng deployment và thay version.
- **Cần quay lại image cũ:** nếu tag là `sha-...` và vẫn còn trong registry, nhập tag đó ở
  `deploy_existing`. Bản legacy tên timestamp cần phục hồi thủ công qua dashboard với cấu hình
  cũ, rồi đặt `KLTN_RELEASE_READY=true` và enable app sau khi kiểm tra.
- **Tag SHA đã tồn tại:** không build/push đè. Chọn deploy_existing hoặc tạo commit mới.
- **Dọn registry lỗi:** release vẫn thành công và có cảnh báo; tự xóa tag cũ không còn dùng.
  Chỉ tag của version vừa thay được dọn. Các image mồ côi từ lần release lỗi/alias khác không
  bị xóa hàng loạt. Cần xóa hết alias của artifact cũ mới giải phóng toàn bộ dung lượng.
- **Sau release thành công:** image trước đó có thể đã bị xóa, không có rollback tự động.
  Muốn phục hồi phải build lại commit cũ (và kiểm tra cấu hình version tương thích).

Không đặt marker true cho bản chưa kiểm tra. Trường hợp bị kill/cancel cứng có thể còn smoke
deployment hoặc app disabled; kiểm tra dashboard rồi chạy recovery. Pipeline không hứa atomic
transaction giữa Edgegap, Firestore và UGS; match đang cấp phát đúng lúc đổi bản có thể phải retry.

## Kiểm tra offline và preflight

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python -m unittest discover -s ci/server/tests -v
pwsh -File ci/server/tests/Test-ReleasedVersion.ps1
```

Với các env cấu hình/credential đã đặt và image đã push, lệnh dưới chỉ đọc API:

```powershell
python ci/server/edgegap_release.py --version sha-123456789abc
```

Chỉ khi thêm `--apply` mới dừng/xóa/tạo tài nguyên. Không chia sẻ output credential.

## Tài liệu API

- [Unity Dedicated Server build](https://docs.unity.com/en-us/engine/6000.5/script-reference/unityeditor/buildplayeroptions)
- [Edgegap versions và registry tags](https://docs.edgegap.com/docs/api/versioning)
- [Edgegap deployments](https://docs.edgegap.com/docs/api/dedicated-servers)
- [Unity Secret Manager](https://docs.unity.com/en-us/services/secret-manager)
