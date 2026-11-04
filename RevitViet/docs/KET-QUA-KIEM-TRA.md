# Kết quả kiểm tra bản đầu

Ngày: 04/11/2026. Môi trường build: Linux x64, .NET SDK 8.0.427, PowerShell 7.4.22.

| Hạng mục | Kết quả |
|---|---|
| Release build, Revit API 2022.1.1 / net48 x64 | Thành công; 0 lỗi, 0 cảnh báo |
| Release build, Revit API 2023.0.1 / net48 x64 | Thành công; 0 lỗi, 0 cảnh báo |
| Release build, Revit API 2024.0.0 / net48 x64 | Thành công; 0 lỗi, 0 cảnh báo |
| Kiểm thử Core dùng chung | 26 đạt; 0 lỗi; 0 bỏ qua |
| PowerShell parser | Đạt |
| Tạo manifest UTF-8, DLL/entry point và tài liệu | Đạt trong thư mục mô phỏng |
| Cài lại cùng gói | Đạt trong thư mục mô phỏng |
| Từ chối gói khác đời Revit | Đạt |
| Từ chối cài khi Revit đang chạy | Đạt bằng process giả lập |
| Từ chối cài trên host không phải Windows | Đạt |
| Khởi động plugin / sửa mô hình trong Revit thật | **Chưa thực hiện** |

Các bài kiểm tra bộ cài đã chạy cho cả ba gói. Mô phỏng chỉ kiểm tra file, XML và các điều kiện của installer; không xác nhận hành vi của Windows, Revit hoặc thuật toán hình học trên mô hình thật. Cần thực hiện [checklist tích hợp](KIEM-THU-REVIT.md) bằng Revit đúng phiên bản trước khi dùng cho dự án.

Các log build và bộ cài nằm trong `artifacts/` của workspace. Không phân phối DLL Autodesk; Revit API do bản Revit đã cài cung cấp lúc chạy.
