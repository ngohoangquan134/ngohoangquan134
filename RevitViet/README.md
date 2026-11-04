# Kết Cấu Việt

Phiên bản 2.0.1 cho Revit 2023 x64 và .NET Framework 4.8, giữ 27 công cụ kết cấu, cốt thép, bản vẽ và khối lượng bằng tiếng Việt.

Xem [hướng dẫn đầy đủ và cách cài](HUONG-DAN.md). Mã nguồn trong `src/`, test tính toán trong `tests/`, build và cài bằng PowerShell trong `scripts/`.

Bản 2.0.1 sửa kiểm tra BAR_TYPE: dùng native `Document.Equals`, lấy lại type từ Document hiện tại bằng ID/UniqueId và snapshot tham số trước mỗi lần tạo, kiểm tra cấu hình chung trước batch. Không cache wrapper type giữa các lệnh/dự án; có lựa chọn riêng thép dọc, đai và phân bố. Xem [dòng gây lỗi và cách sửa](docs/PHAN-TICH-LOI-BAR-TYPE.md).

Phần bố trí thép tách hình học Family trước Join/Cut khỏi miền bê tông thực tế, kiểm tra cover/đường kính/móc sau khi tạo, có đai 3 vùng, tăng cường, đai phụ, lưới có lỗ mở, neo thẳng và nối chồng phân đoạn. Xem [hướng dẫn bố trí thép](docs/REBAR-2023.md), [chẩn đoán mã cũ](docs/PHAN-TICH-LOI-REBAR.md) và [kết quả kiểm chứng](docs/KET-QUA-KIEM-TRA.md).

Có chế độ kiểm tra thử rồi hoàn tác; không tự xóa thép cũ. Các thông số không thay thế thiết kế chịu lực. Kiểm thử trực tiếp trong Revit trên Windows vẫn là bước cần thực hiện trước khi dùng cho dự án.
