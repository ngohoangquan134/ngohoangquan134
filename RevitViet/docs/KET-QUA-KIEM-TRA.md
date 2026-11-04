# Kết quả kiểm tra bản cập nhật bố trí thép 2.0

Ngày 04/11/2026. Môi trường cloud Linux x64; .NET SDK 8.0.427; PowerShell 7.4.22. Plugin nhắm .NET Framework 4.8, Windows x64, Revit 2023.

| Hạng mục | Kết quả |
|---|---|
| Release build với Revit API 2023.0.1 | Thành công; không có lỗi/cảnh báo biên dịch |
| Core regression suite | 52 đạt; 0 lỗi; 0 bỏ qua |
| Contour dầm/cột chữ nhật và kiểm tra trục xoay/nghiêng | Đạt trên fixture giải tích |
| Join tiếp xúc, khe thật và đầu cắt xiên | Đạt trên fixture interval/nửa không gian |
| Biên dạng tròn/L/rỗng và thay đổi tiết diện | Từ chối đúng trong fixture |
| Cover hết tiết diện, cung ở đúng cover, uốn quanh thép góc | Đạt trên fixture giải tích |
| Đai ba vùng, mối mở tránh thanh giữa và đai phụ lệch mặt phẳng | Đạt trên fixture giải tích |
| Phân đoạn nối chồng, làn nối và phát hiện giao thép | Đạt trên fixture giải tích |
| PowerShell và manifest cài đặt | Xem log kiểm thử gói trong artifacts |
| Revit thật trên Windows | **Chưa thực hiện** |
| File RVT đã báo lỗi trên máy người dùng | **Chưa có file/log để tái hiện** |

52 test trên cloud không nạp RevitAPI và không khởi chạy Revit. Fixture được dùng bởi phần toán học thực tế trong adapter API, nhưng không xác nhận bộ giải shape/hook, native constraints, Boolean kernel, kết quả vật liệu nhiều lớp, hiệu năng hoặc tương tác UI trong Revit.

Bộ cài được kiểm tra bằng PowerShell với đường dẫn profile và trạng thái process Windows mô phỏng trong thư mục tạm: XML/entry point, sao chép DLL/tài liệu, cài lại, sai phiên bản, Revit đang mở và host không phải Windows. Đây không phải một lần cài/chạy trong Windows thật.

Log build và TRX nằm trong artifacts của workspace. Cần chạy [checklist tích hợp](KIEM-THU-REVIT.md) và dùng chế độ kiểm tra thử trên mô hình Revit trước khi áp dụng cho dự án. [Phân tích nguyên nhân](PHAN-TICH-LOI-REBAR.md) phân biệt bằng chứng từ mã với lỗi chưa tái hiện trong Revit.
