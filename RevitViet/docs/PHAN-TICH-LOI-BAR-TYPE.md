# Sửa BAR_TYPE trong KẾT CẤU VIỆT 2.0.1 — Revit 2023

Ngày 04/11/2026. Phân tích dựa trên mã nguồn bản 2.0, metadata và XML của Revit API **2023.0.1**. Người dùng cung cấp thông báo lỗi và đường dẫn log Windows; nội dung file log/RVT chưa có trong cloud. Chưa chạy Revit thật, chưa xác nhận kết quả trên 466 host của mô hình nhà 10 tầng.

## Dòng phát sinh đúng thông báo đã báo cáo

Bản trước tại commit `009528310cb614c5e2f8f856b2c31d42280c1596`:

[`RebarTemplates.cs`, hàm `Validate`, dòng 23](https://github.com/ngohoangquan134/ngohoangquan134/blob/009528310cb614c5e2f8f856b2c31d42280c1596/RevitViet/src/KetCauViet.Revit/Tools/RebarTemplates.cs#L23):

```csharp
if (type.Document != doc || !type.IsValidObject)
    throw new RebarIssue("BAR_TYPE", "Rebar Bar Type không thuộc tài liệu hiện tại hoặc đã mất hiệu lực.");
```

Có hai sai sót ở đây:

1. Trong Revit 2023, `Document` override `Equals(object)` nhưng không định nghĩa toán tử `==`/`!=`; `APIObject` cũng không định nghĩa hai toán tử đó. Vì thế `!=` ở dòng này so sánh tham chiếu managed. Hai wrapper có thể đại diện cùng một tài liệu Revit mà khác tham chiếu .NET. XML chính thức mô tả `Document.Equals`: “Two document instances are considered equal if they represent the same document currently opened in the Revit session.” Kiểm tra bằng `!=` có thể từ chối type hợp lệ của chính dự án.
2. `type.Document` được đọc **trước** `type.IsValidObject`. Wrapper đã mất hiệu lực có thể ném exception ngay khi đọc thuộc tính, trước khi chạm kiểm tra hiệu lực. Hai nguyên nhân bị gộp vào cùng một thông báo, không đủ dữ liệu để phân biệt.

Metadata test đọc trực tiếp reference assembly Autodesk phiên bản major 23 để xác minh override và toán tử; fixture riêng tái hiện hai wrapper cùng native identity. Đây là bằng chứng về lỗi trong mã, không phải bản ghi giá trị `ReferenceEquals` tại máy người dùng. Log bản mới ghi cả `owner.Equals(current)` và `ReferenceEquals` để kiểm chứng trong Revit.

## Những vị trí liên quan đã kiểm tra

| Vị trí ở commit trước | Kết luận từ mã |
|---|---|
| `RebarTools.cs`, `Layout`, dòng 83 | Collector lấy từ `c.Doc`, không có bằng chứng collector cố tình lấy type từ Document khác |
| `RebarLayout.cs`, `RebarLayoutSettings`, dòng 10–11 | Giữ đối tượng `RebarBarType` sống xuyên hộp thoại và batch; không có snapshot định danh/kiểm tra thay đổi |
| `RebarLayout.cs`, constructor, dòng 70 | Gọi `Validate` ở mỗi host; lỗi chung bị lặp cho từng cấu kiện |
| `RebarExecution.cs`, `Run` | Bắt lỗi theo host; chưa có preflight loại thép chung trước TransactionGroup |
| `ToolContext.cs`, thuộc tính `Doc`/`Ui` | Truy cập `ActiveUIDocument` mỗi lần; không cố định Document theo lần chạy |
| `RebarTemplates.cs`, `Hook` | Dùng lại `Validate` sai khi tạo bản sao type cho móc |
| Toàn bộ đường gọi bố trí thép | Không tìm thấy cache tĩnh RebarBarType/ElementId dùng chung giữa các dự án |

Do đó không kết luận thiếu type, type bị xóa hay cache từ dự án khác là nguyên nhân đã xảy ra trên máy người dùng. Phép so sánh sai và vị trí kiểm tra trong vòng lặp là các lỗi xác định được từ nguồn, phù hợp với việc cùng thông báo lặp 466 lần. Các trường hợp mất/xóa/đổi type vẫn được bảo vệ riêng trong bản sửa.

## Cách sửa

- `ToolContext` giữ `UIDocument` và `Document` tại lúc bắt đầu lệnh.
- `RebarTypeCatalog` được tạo/dispose theo từng lần gọi lệnh, không có cache tĩnh. Collector chỉ đọc Document đã cố định; đối chiếu Document đang active bằng `Document.Equals`.
- Hộp thoại chỉ giữ `TypeChoice` bất biến: mã phạm vi ngẫu nhiên của lần chạy, ID số nguyên, `UniqueId`, snapshot tham số, tên và kích thước. Không giữ RebarBarType, Parameter hoặc ElementId wrapper trong lựa chọn/bản kế hoạch thanh.
- Trước sử dụng, lấy lại `doc.GetElement(new ElementId(id))`; kiểm tra tồn tại, `IsValidObject`, đúng lớp `RebarBarType`, native owner `Equals`, `UniqueId`, tên, đường kính và uốn. Không tra type thay thế theo tên khi ID mất hiệu lực.
- Snapshot có `VersionGuid` và SHA-256 giá trị tham số type đọc trực tiếp. Revit 2023 ghi rõ `VersionGuid` **không phát hiện mọi thay đổi trong phiên giữa các lần save/sync**; vì vậy bản sửa không dựa riêng vào thuộc tính đó. Các đường kính/bend cũng được đối chiếu trực tiếp.
- Mỗi lần tạo thanh, sau rollback/giao dịch con, đều resolve lại type/host/hook bằng định danh; không tiếp tục dùng wrapper type đã lấy trước đó. Hook/type phụ của từng host được lấy snapshot sau khi cấu hình xong.
- Đọc `BarNominalDiameter` và `BarModelDiameter` riêng bằng API 2023: danh sách/UI dùng nominal, kiểm tra hình học dùng model.
- Cho chọn riêng type dọc/chính, đai và phân bố. Nhập đường kính khác lựa chọn phải xác nhận đúng type khớp; thiếu type thì hướng dẫn nạp/tạo, không thay bằng đường kính khác.
- Preflight tất cả role trước `TransactionGroup`, kiểm tra lại trước mỗi host và `CreateFromCurves`. Lỗi type chung dừng một lần. Nếu phát hiện giữa batch thì rollback nhóm, không giữ batch dở. Lỗi host/hình học/API riêng vẫn rollback host rồi xử lý tiếp.
- Mặc định bỏ qua host đã có thép. Khi người dùng chọn thêm, kiểm tra giao thép; không xóa/di chuyển/đổi type thép cũ. Giữ nguyên kiểm tra cover/hình học, 27 công cụ và giao diện tiếng Việt.

## Mã chẩn đoán mới

Các lỗi lựa chọn chung có dạng `[BAR_TYPE] [mã chi tiết] ...`:

| Mã chi tiết | Cách xử lý |
|---|---|
| `NO_VALID_TYPES` | Nạp/tạo Rebar Bar Type trong dự án hiện tại; kiểm tra diameter và bend |
| `DOCUMENT_CHANGED`, `FOREIGN_SCOPE`, `SCOPE_CLOSED` | Chạy lại lệnh trong đúng dự án và chọn lại type |
| `TYPE_DELETED`, `INVALID_OBJECT`, `WRONG_CLASS`, `REPLACED_ID` | Type/định danh đã mất hiệu lực; mở lại lệnh để chọn lại |
| `FOREIGN_DOCUMENT` | Loại bỏ lựa chọn ngoài Document hiện tại |
| `TYPE_CHANGED` | Type đổi sau khi chọn; xem snapshot/current trong log, mở lại hộp thoại |
| `INVALID_DIAMETER`, `INVALID_BEND` | Sửa/nạp type có kích thước hợp lệ cho role |
| `DIAMETER_MISSING` | Không có đúng đường kính nhập; tạo/nạp đúng type |
| `TYPE_READ` | Revit API không đọc được type; log giữ exception gốc |
| `PREFLIGHT` | Tổng hợp các role lỗi; batch chưa bắt đầu |

Log ở `%LOCALAPPDATA%\KetCauViet\Logs\rebar-*.log`, đầu log ghi **KCV rebar 2.0.1**. Cần giữ log này khi thử lại để tránh nhầm với DLL cũ. Xem [kết quả cloud](KET-QUA-KIEM-TRA.md) và [8 ca tích hợp BAR_TYPE](KIEM-THU-REVIT.md#hồi-quy-bar_type-bản-201-trong-revit-2023).
