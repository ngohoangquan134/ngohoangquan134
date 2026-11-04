# Phân tích lỗi bố trí cốt thép trước khi sửa

## Bằng chứng từ mã cũ

Mã trước thay đổi được giữ tại commit `1e646d4` trên nhánh cập nhật để có thể so sánh. Khi kiểm tra ban đầu, GitHub chỉ chứa ZIP; nguồn đã được tạo ở workspace nhưng chưa được commit. Commit này ghi nhận nguồn trước khi sửa, không phải bản nâng cấp.

`HostFrame` cũ lấy solid đã qua Join/Cut, lập hộp bao cục bộ từ các đỉnh, rồi yêu cầu `abs(solidVolume / boundingVolume - 1) <= 0.001`. Một đầu dầm/cột cắt xiên hoặc mất phần thể tích tại Join có thể vi phạm điều kiện này dù thân Family vẫn có tiết diện chữ nhật. Do đó đây là một nguyên nhân xác định được từ mã khiến các cấu kiện đó bị từ chối.

Các vấn đề khác phát hiện khi rà soát:

- Không đọc GetOriginalGeometry để phân biệt danh nghĩa với sau Join/Cut.
- Cách dựng trục dầm phụ thuộc BasisY, dễ suy biến nếu family định nghĩa trục khác. Cột nghiêng bị từ chối trước khi phân tích tiết diện.
- Chỉ so tổng chiều dài giao giữa đường tâm và solid; chưa xác minh đường kính mô hình, cover quanh toàn thanh, phần uốn/móc và mọi vị trí trong bộ thép sau Regenerate.
- Chưa kiểm tra đủ Rebar Bar Type, uốn, Hook Permission và điều kiện bộ giải cần RebarShape có tham số.
- Sàn chỉ nhận một mặt top; vách/sàn nhiều lớp bị chặn đồng loạt. Chưa tách lõi bê tông hoặc clip lưới quanh lỗ.
- Batch cũ chỉ hiện tổng số ở nội dung chính; Element ID và exception nằm trong phần mở rộng. Không có log bền vững. Subtransaction không cô lập được các failure chỉ xuất hiện khi commit transaction cuối.

## Hồi quy đã bổ sung

Fixture dùng contour chữ nhật và mặt cắt xiên cho thấy có thể giữ nguyên tiết diện danh nghĩa, đồng thời rút thanh theo nửa không gian đã offset cover. Fixture interval xác nhận hai miền bê tông Join tiếp xúc không tạo khe giả, nhưng lỗ thật vẫn phải còn khe. Có test cho trục xoay/nghiêng, L/tròn/rỗng, tiết diện vát, vùng đai, phân đoạn nối, cung ở đúng cover, vị trí mối mở đai cột và giao đai phụ.

## Giới hạn của chẩn đoán

Chưa có file RVT hoặc log chi tiết của cấu kiện đã báo lỗi trên máy người dùng, nên chưa xác nhận nguyên nhân riêng của Element ID đó và chưa tái hiện lỗi trong Revit. Các nguyên nhân trên là kết luận từ mã và fixture, không phải một lần chạy thành công trong Revit. Bản mới cung cấp log theo từng host và chế độ kiểm tra thử để xác minh điều kiện thực tế trên máy Windows.
