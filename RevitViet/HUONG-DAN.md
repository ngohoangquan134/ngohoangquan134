# KẾT CẤU VIỆT — plugin Revit bằng tiếng Việt

Bản cập nhật 2.0 cho **Revit 2023 trên Windows x64**, .NET Framework 4.8. Mã nguồn vẫn giữ cấu hình tham chiếu riêng cho 2022.1/2024; gói phát hành của lần nâng cấp này là Revit 2023. Có 27 lệnh tương ứng các nút nhìn thấy trong ảnh tham khảo, tổ chức thành tab **KẾT CẤU VIỆT**. Đây là mã triển khai độc lập; ảnh không mô tả thuật toán, tùy chọn trong các menu xổ xuống hoặc quy tắc bóc khối lượng của phần mềm gốc.

## Cài bản đã build

1. Chọn gói đúng phiên bản Revit và giải nén toàn bộ ZIP vào một thư mục.
2. Lưu dự án rồi đóng Revit.
3. Mở PowerShell tại thư mục vừa giải nén, chạy (thay năm đúng bản đang dùng):

   ```powershell
   .\Install.ps1 -RevitVersion 2023
   ```

4. Mở Revit. Nếu Revit hỏi tin cậy add-in chưa ký số, kiểm tra nguồn gói trước khi cho phép nạp. Tab **KẾT CẤU VIỆT** xuất hiện khi nạp thành công.

Cài theo người dùng, không cần quyền quản trị:
- DLL và hướng dẫn: `%APPDATA%\Autodesk\Revit\Addins\2023\KetCauViet\`.
- Manifest: `%APPDATA%\Autodesk\Revit\Addins\2023\KetCauViet.addin`.

Gói 2023 phải dùng cho đúng Revit 2023. Không dùng chung DLL giữa các đời Revit. Muốn gỡ, đóng Revit và xóa đúng manifest/thư mục KetCauViet trên. Installer không sửa các add-in khác hoặc tự đóng Revit.

## Build từ mã nguồn

Cài **.NET SDK 8.0.4xx**. Mã plugin vẫn target **net48**, không yêu cầu Revit chạy .NET 8.

```powershell
cd RevitViet
.\scripts\Build.ps1 -RevitVersion 2023
.\scripts\Install.ps1 -RevitVersion 2023
```

Hoặc dùng Revit API chính chủ trên máy đã cài Revit:

```powershell
.\scripts\Build.ps1 -RevitVersion 2023 -RevitApiPath 'C:\Program Files\Autodesk\Revit 2023'
```

Mặc định NuGet dùng các gói **tham chiếu** Nice3point.Revit.Api.RevitAPI / RevitAPIUI; các DLL Autodesk này không được đóng gói hoặc cài đè lên Revit. Phiên bản tham chiếu được ghim riêng: 2022.1.1, 2023.0.1 và 2024.0.0. Nếu dùng API cục bộ, chọn thư mục đúng đời Revit. Các dependency khôi phục qua NuGet giữ kiểm tra TLS và chữ ký mặc định.

Build tạo `artifacts/KetCauViet-Revit2023.zip` có DLL, manifest `.addin` tương đối, script cài và hướng dẫn; DLL cũng có trong `artifacts/2023/`. Thay tham số để build các bản khác. Có thể biên dịch và chạy test Core trên Linux, nhưng **chỉ Revit trên Windows mới chạy được plugin**.

## Sử dụng chung

- Mở dự án .rvt (không phải Family Editor). Chọn đối tượng rồi bấm lệnh, hoặc chọn theo yêu cầu trên thanh trạng thái. Nhấn **Finish** khi chọn nhiều đối tượng; **ESC** để hủy.
- Phạm vi mặc định là đối tượng được chọn; bảng schedule thép được ghi rõ là toàn dự án.
- Các lệnh sửa mô hình có thể **Undo**. Riêng rải thép dùng transaction độc lập theo host và gom thành một bước Undo; lỗi hoặc cảnh báo ở commit sẽ hoàn tác host đó. Có chế độ kiểm tra thử rồi hoàn tác toàn bộ. Kết quả phân biệt thành công, giữ nguyên thép cũ và lỗi.
- Đơn vị nhập chiều dài: **mm**. Chấp nhận dấu phẩy/chấm thập phân; không nhập dấu tách hàng nghìn. Báo cáo dùng m, m², m³, kg và dấu phẩy thập phân.
- CSV có BOM UTF-8, dấu phân cột `;`, mở bằng Excel. Nếu Excel chưa tự tách cột, chọn Data → From Text/CSV → UTF-8 → dấu `;`.
- Nút báo cáo cho xem tổng trước, bấm **Lưu báo cáo CSV** khi cần tệp.

## 27 công cụ

| Nhóm | Công cụ tiếng Việt | Chức năng thực hiện |
|---|---|---|
| Kết cấu | Nối hình học | Tự nối các cấu kiện được chọn có hộp bao giao nhau. |
| Kết cấu | Dựng từ CAD | Dựng dầm hoặc tường từ đường thẳng/cung tròn trên layer CAD được chọn. |
| Kết cấu | Đổi kiểu dầm | Đổi family type cho các dầm được chọn. |
| Kết cấu | Tọa độ cọc | Xuất tọa độ các móng/cọc dạng FamilyInstance theo hệ nội bộ hoặc hệ chia sẻ. |
| Kết cấu | Đánh số cấu kiện | Đánh Mark theo thứ tự cao độ, Y, X; tự tránh mã đã dùng. |
| Kết cấu | Bê tông lót | Tạo sàn lót bên dưới mặt đáy nằm ngang của sàn được chọn. |
| Kết cấu | Cho/không nối | Cho phép hoặc cấm nối đầu dầm/tường. |
| Kết cấu | Cắt dầm theo mặt | Chọn mặt của khối cắt; áp dụng SolidSolidCut cho các dầm Revit cho phép cắt. |
| Kết cấu | Tiện ích kết cấu | Cô lập cấu kiện, bỏ cô lập hoặc xuất kiểm tra mã và thể tích. |
| Cốt thép | Đánh mã thép | Gán Mark chung cho thép cùng type, shape và chiều dài thanh. |
| Cốt thép | Bảng thống kê thép | Tạo Revit schedule cốt thép cho toàn dự án. |
| Cốt thép | Thép dầm | Thép trên/dưới, tăng cường và đai ba vùng; xét tiết diện danh nghĩa và Join/Cut thực tế. |
| Cốt thép | Thép cột/vách | Thép dọc, đai, đai phụ của cột chữ nhật; lưới hai lớp theo lõi bê tông và lỗ mở vách. |
| Cốt thép | Thép sàn | Lưới hai phương trên/dưới theo biên mặt phẳng, có xét lỗ mở và lớp bê tông kết cấu. |
| Cốt thép | Tiện ích cốt thép | Chỉnh hiển thị thép hoặc chọn thép theo host. |
| Bản vẽ | Nạp family | Nạp một hoặc nhiều family .rfa. |
| Bản vẽ | Mặt cắt dầm | Tạo mặt cắt dọc/ngang của các dầm được chọn. |
| Bản vẽ | Hình dạng thép 2D | Chuyển hình dạng một thanh thép phẳng thành detail curves đúng chiều dài. |
| Bản vẽ | Ghi kích thước | Tạo chuỗi kích thước từ các mặt phẳng song song được chọn. |
| Khối lượng | Diện tích ván khuôn | Đo mặt bên/đáy theo hình học mô hình hiện tại. |
| Khối lượng | Bê tông cột/tường | Thống kê thể tích cột và tường được chọn. |
| Khối lượng | Bê tông vách lõi | Thống kê thể tích toàn bộ các tường vách lõi do bạn chọn. |
| Khối lượng | Đo mặt | Đo diện tích, chu vi các mặt được chọn. |
| Khối lượng | Cộng tham số | Cộng tham số số cùng đơn vị, xuất CSV. |
| Khối lượng | Khối lượng thép | Tính khối lượng theo tổng chiều dài, đường kính danh nghĩa và khối lượng riêng. |
| Khối lượng | Chống thấm | Diện tích các mặt được chọn × số lớp × hệ số bù. |
| Trợ giúp | Hướng dẫn | Hướng dẫn tiếng Việt, phạm vi và cách cài đặt. |

## Phạm vi hình học và cốt thép

### Kết cấu và bản vẽ

- **CAD**: chọn một CAD Import/Link, một layer đường tim, kiểu cấu kiện và Level. Nhận Line, Arc và các đoạn PolyLine nằm ngang. Dựng **dầm hoặc tường** từ đường tim, không tự nhận dạng cột/sàn từ hatch hay các đường biên. Loại trùng đường trong một lần chạy; không tìm/xóa cấu kiện đã dựng ở lần trước.
- **Bê tông lót**: nhận Floor có mặt đáy phẳng ngang; tạo Floor mới với Type do bạn chọn. Chiều dày theo Type, mặt trên nằm dưới đáy gốc theo khe hở đã nhập. Không tự nới biên, không tự thay đổi Type của sàn gốc, không chống tạo trùng qua nhiều lần chạy.
- **Cắt theo mặt**: mặt dùng để chọn **khối cắt**. Dùng SolidSolidCutUtils với toàn bộ khối cắt; không cắt theo mặt phẳng vô hạn, không tự tạo family void. Khối/loại cấu kiện phải được Revit cho phép Solid-Solid Cut.
- **Mặt cắt dầm**: tạo Section view dọc/ngang, có tỷ lệ và khoảng bao. Không tự đặt lên sheet, tự tag/dimension hoặc phát hành hồ sơ.
- **Hình thép 2D**: một thanh/bộ thép shape-driven phẳng; tạo Detail Curves tại điểm đặt trong view. Đường cong giữ chiều dài/kích thước, hình 2D độc lập với thép nguồn. Không hỗ trợ thanh free-form hoặc hình nhiều mặt phẳng.
- **Kích thước**: chọn từ hai mặt phẳng song song, pháp tuyến trong mặt phẳng view, rồi chọn vị trí đường kích thước. Tạo một chuỗi Dimension bằng tham chiếu thật, không dùng Text giả số đo.

### Dựng thép

Bản 2.0 phân tích hình học danh nghĩa của Family để nhận diện tiết diện và đọc riêng solid thực tế sau Join/Cut để kiểm tra thanh. Không còn điều kiện thể tích solid phải bằng thể tích hộp bao. Dầm/cột có trục thẳng, tiết diện thân chữ nhật có thể xoay/nghiêng hoặc bị cắt đầu nếu đường rải và lớp bảo vệ còn hợp lệ.

Có thép tăng cường dầm, đai ba vùng, đai phụ cột, móc 135°, lưới hai phương, neo thẳng theo thông số nhập và phân đoạn nối chồng thành hai làn. Vách/sàn xét lớp bê tông chịu lực và biên lỗ mở; không chặn toàn bộ cấu kiện nhiều lớp. Các thanh sau khi Revit tạo đều được đọc lại để kiểm tra cover và giao thép.

**Thông số nhập không phải kết quả thiết kế chịu lực.** Các cấu kiện không xác minh được, thiếu cover hoặc bị API từ chối sẽ không được lưu. Thép cũ luôn được giữ nguyên. Xem [phạm vi và cách dùng chi tiết](docs/REBAR-2023.md), [nguyên nhân mã cũ](docs/PHAN-TICH-LOI-REBAR.md).

Log theo host ở `%LOCALAPPDATA%\\KetCauViet\\Logs\\`; lỗi chính có Element ID, nhóm nguyên nhân và nút mở log. Nên chạy chế độ kiểm tra thử rồi hoàn tác trước trên mô hình mẫu.

**Đánh số thép** có hai chế độ:
- Native Revit Number trong Partition: các thanh được Revit coi là khớp có cùng số. Không tác động tới thép ngoài lựa chọn; nếu Partition đang có thép chưa chọn thì yêu cầu chọn đủ hoặc dùng Partition mới.
- Mark: nhóm theo type, shape, móc và chiều dài từng đoạn làm tròn 0,1 mm; mã riêng dùng cho báo cáo. Mark không thay thế native Rebar Number. Với thép biến thiên, dùng chế độ native để theo cơ chế khớp của Revit.

### Khối lượng

- **Bê tông** dùng HOST_VOLUME_COMPUTED khi có, dự phòng thể tích solid. Bao gồm mọi lớp vật liệu của cấu kiện; người dùng chọn đúng cấu kiện bê tông. **Vách lõi** là toàn bộ các tường bạn chọn, không tự phát hiện lõi và không bóc riêng core layer của tường nhiều lớp.
- **Ván khuôn**: cộng diện tích mặt bên/đáy hiện có. Hình học Join/cắt ảnh hưởng kết quả. Không tự trừ tiếp xúc giữa các khối chưa Join; không phân loại vật liệu. Chế độ hướng mặt chỉ nhận mặt phẳng, từ chối mặt cong; có thể dùng Đo mặt hoặc Toàn bộ mặt cho mặt cong.
- **Đo mặt/chống thấm**: đọc diện tích Face.Area thực, gồm ảnh hưởng lỗ. Loại trùng tham chiếu trong một lần chọn. Chống thấm = diện tích × số lớp × (1 + bù%/100).
- **Thép**: tổng chiều dài cả bộ thép × tiết diện tròn theo đường kính danh nghĩa × khối lượng riêng (mặc định 7850 kg/m³); không nhân số lượng lần thứ hai, không tự cộng hao hụt. Chưa xử lý thép fabric, coupler hoặc đơn giá.
- **Cộng tham số**: tham số numeric ở instance, cùng loại đại lượng; không cộng Yes/No. Phân biệt tham số thiếu, tên trùng và khác đơn vị. Tổng xuất theo đơn vị dự án.

## Kiểm thử

```powershell
dotnet test tests/KetCauViet.Core.Tests/KetCauViet.Core.Tests.csproj -c Release
```

Test Core kiểm tra chuyển đơn vị, số/CSV, contour tiết diện, hệ trục xoay/nghiêng, fixture Join/Cut, cover/cung uốn, vùng đai, mối mở và đai phụ, giao thép và nối chồng phân đoạn. Các phép tính này không thay thế kiểm thử Revit.

Kiểm tra bộ cài sau khi build, bằng PowerShell (mô phỏng đường dẫn Windows trong thư mục thử nghiệm, không mở Revit):

```powershell
.\tests\Install.Tests.ps1 -RevitVersion 2023
```

Biên dịch thành công chỉ xác nhận API/kiểu dữ liệu. Máy cloud Linux không có Revit/giấy phép/GUI Windows để xác nhận hành vi mô hình. Checklist chạy thực tế: [docs/KIEM-THU-REVIT.md](docs/KIEM-THU-REVIT.md). Luôn thử bằng mô hình mẫu trước khi áp dụng dự án đang làm.
