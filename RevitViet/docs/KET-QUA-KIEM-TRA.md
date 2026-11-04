# Kết quả kiểm tra bản cập nhật bố trí thép 2.0.1

Ngày 04/11/2026. Môi trường cloud Linux x64; .NET SDK 8.0.427; PowerShell 7.4.22. Plugin nhắm .NET Framework 4.8, Windows x64, Revit 2023.

| Hạng mục | Kết quả |
|---|---|
| Release build với Revit API 2023.0.1 | Thành công; không có lỗi/cảnh báo biên dịch |
| Core/contract regression suite | 83 đạt; 0 lỗi; 0 bỏ qua (52 ca cũ, 27 ca chính sách type, 4 ca contract API 2023) |
| Document.Equals so với managed reference | Metadata DLL major 23 + XML API xác nhận override Equals, không có operator ==/!= trên Document/APIObject |
| Type đổi/xóa, Document đổi, type trùng tên/ID, chạy lại lệnh | Đạt trên chính sách chọn/resolve dùng chung với production adapter |
| Batch 466 lượt resolve | Đạt trên fixture policy; mỗi lượt lấy wrapper mới, preflight lỗi không gọi xử lý host |
| Nominal/model diameter và bend theo role | Đạt trên fixture; contract API xác nhận hai property riêng và giới hạn VersionGuid trong phiên |
| Contour dầm/cột chữ nhật và kiểm tra trục xoay/nghiêng | Đạt trên fixture giải tích |
| Join tiếp xúc, khe thật và đầu cắt xiên | Đạt trên fixture interval/nửa không gian |
| Biên dạng tròn/L/rỗng và thay đổi tiết diện | Từ chối đúng trong fixture |
| Cover hết tiết diện, cung ở đúng cover, uốn quanh thép góc | Đạt trên fixture giải tích |
| Đai ba vùng, mối mở tránh thanh giữa và đai phụ lệch mặt phẳng | Đạt trên fixture giải tích |
| Phân đoạn nối chồng, làn nối và phát hiện giao thép | Đạt trên fixture giải tích |
| PowerShell và manifest cài đặt | Xem log kiểm thử gói trong artifacts |
| Revit thật trên Windows | **Chưa thực hiện** |
| File RVT đã báo lỗi trên máy người dùng | **Chưa có file/log để tái hiện** |

83 test trên cloud không nạp native RevitAPI và không khởi chạy Revit. Contract test đọc metadata PE/XML của reference API 2023 bằng thư viện .NET; fixture vòng đời dùng chính `TypeSelectionScope` mà production adapter gọi. Chúng không thực thi `Document.GetElement`, collector, transaction, bộ giải shape/hook hoặc UI thật. Các ca hình học cũng không xác nhận native constraints, Boolean kernel, vật liệu nhiều lớp hay hiệu năng. Chưa xác nhận lưu thép thành công trên 466 host của file RVT người dùng.

Bộ cài được kiểm tra bằng PowerShell với đường dẫn profile và trạng thái process Windows mô phỏng trong thư mục tạm: XML/entry point, sao chép DLL/tài liệu, cài lại, sai phiên bản, Revit đang mở và host không phải Windows. Đây không phải một lần cài/chạy trong Windows thật.

Log build và TRX nằm trong artifacts khi chạy kiểm thử. Cần chạy [checklist tích hợp và 8 ca BAR_TYPE](KIEM-THU-REVIT.md) và dùng chế độ kiểm tra thử trên mô hình Revit trước khi áp dụng cho dự án. [Phân tích BAR_TYPE](PHAN-TICH-LOI-BAR-TYPE.md) chỉ rõ file/hàm/dòng cũ, bằng chứng API và giới hạn chưa tái hiện trong Revit; [phân tích hình học](PHAN-TICH-LOI-REBAR.md) mô tả bản nâng cấp trước.
