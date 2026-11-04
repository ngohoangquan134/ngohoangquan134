# Bố trí cốt thép phiên bản 2 cho Revit 2023

Bản này thay thuật toán rải thép dầm, cột, vách và sàn; giữ nguyên 27 nút và tab KẾT CẤU VIỆT. Target là Revit 2023, Windows x64, .NET Framework 4.8. Thông số nhập là yêu cầu bố trí của người dùng, không phải kết quả tính chịu lực hoặc kiểm tra tiêu chuẩn thiết kế.

## Cài bản cập nhật

1. Tải `KetCauViet-Revit2023.zip` từ nhánh cập nhật trên GitHub, giải nén toàn bộ vào một thư mục.
2. Lưu dự án và đóng tất cả cửa sổ Revit.
3. Mở PowerShell trong thư mục giải nén, chạy `.\Install.ps1 -RevitVersion 2023`.
4. Mở Revit 2023, kiểm tra tab KẾT CẤU VIỆT. DLL bản cập nhật có file version 2.0.0.0.

Installer tạo manifest có đường dẫn tuyệt đối tại `%APPDATA%\Autodesk\Revit\Addins\2023\KetCauViet.addin`, DLL ở thư mục `KetCauViet` cùng cấp. ZIP cũng có manifest tương đối `KetCauViet.addin` để cài thủ công: chép file này và nguyên thư mục `KetCauViet` vào đúng thư mục Addins\2023. Không chép DLL Autodesk từ NuGet vào Revit; gói không chứa RevitAPI.dll/RevitAPIUI.dll.

## Chạy kiểm tra trước khi lưu thép

Chọn một nhóm cấu kiện rồi dùng Thép dầm, Thép cột/vách hoặc Thép sàn. Trong tab Chung, chọn **Kiểm tra thử và hoàn tác mọi thay đổi**. Công cụ vẫn tạo và kiểm tra bằng API trong nhóm giao dịch thử, rồi hoàn tác cả thép, hook/type/shape mới. Log được giữ lại. Kết quả "Kiểm tra đạt" không đồng nghĩa thép đã được lưu.

Sau khi kiểm tra hình học và thông số đạt yêu cầu, chạy lại ở chế độ **Tạo thép và lưu thay đổi**. Các host thành công được giữ; host lỗi được hoàn tác riêng. Có thể Undo cả lần chạy thành công bằng một thao tác.

Mặc định host đã có thép được giữ nguyên và bỏ qua. Chế độ thêm sẽ kiểm tra giao giữa thép mới và thép cũ có thể đọc qua API; không tự xóa, di chuyển hoặc thay type của thép cũ.

## Các tab thông số

### Chung

- Chọn Rebar Bar Type hoặc nhập đường kính danh nghĩa. Đường kính nhập phải có Type tương ứng trong dự án; nếu Type đang chọn khớp thì ưu tiên giữ Type đó. Kích thước bao thép dùng **đường kính mô hình**, có thể lớn hơn đường kính danh nghĩa.
- **Theo host**: đọc các Rebar Cover Type top/bottom/other/interior/exterior, lấy giá trị lớn nhất và áp dụng đồng đều. Đây là lựa chọn bảo thủ được ghi rõ trong UI và log, không tự suy đoán từng mặt.
- **Nhập đồng đều**: dùng cover nhập cho mọi mặt của lớp bê tông kết cấu. Cover đo từ bề mặt bê tông đến bề mặt thanh thép ngoài.
- Khe hở áp dụng cho các thanh song song và làn nối; các thanh hai phương tiếp xúc đúng bề mặt được phép.

### Dầm

Rải thép dưới và trên theo số thanh nhập. Thép tăng cường trên đặt tại mỗi đầu, ở lớp trong phía dưới thép trên; thép tăng cường dưới đặt giữa nhịp, ở lớp trong phía trên thép dưới. Cả hai dùng Type dọc đã chọn. Số tăng cường bằng 0 nghĩa là không thêm. Nếu hai vùng tăng cường trên chồng lên nhau thì yêu cầu sửa chiều dài, không tự gộp thành thiết kế khác.

Trục dầm phải thẳng nhưng có thể xoay trong mặt bằng, roll hoặc nghiêng. Hệ trục tiết diện suy ra từ các mặt bên của hình học Family; không giả định BasisY luôn là chiều rộng. Tiết diện thân danh nghĩa được cắt tại 25%, 50%, 75% và phải là chữ nhật nhất quán. Các đầu Join/Cut được xét riêng khi xác định chiều dài thanh và vùng lồng đai.

### Cột

Thép dọc theo chu vi tiết diện chữ nhật, số U/V là số trên mỗi cạnh; các góc không bị đếm hai lần. Cột có thể xoay hoặc có trục LocationCurve thẳng nghiêng nếu tiết diện danh nghĩa vẫn là lăng trụ chữ nhật.

Bật đai phụ khi cần: đai phụ U/V cho các thanh giữa cạnh, dùng móc 135°. Hai phương đai phụ lệch nhau theo trục để không cắt nhau trong cùng mặt phẳng. Vùng cuối được chừa đủ chỗ cho độ lệch. Công cụ không tự quyết định số nhánh đai theo điều kiện ổn định hoặc tiêu chuẩn thiết kế.

### Đai

Đai có ba vùng: đầu, giữa, cuối. Chiều dài hai vùng đầu/cuối tính từ đai đầu/cuối của vùng lồng đã kiểm tra hình học; tổng hai vùng không được vượt chiều dài đó. Chiều dài bằng 0 cho cả hai vùng nghĩa là rải đều bằng bước giữa. Bước nhập là bước tối đa; biên giữa các vùng không tạo thanh trùng.

Mặc định có móc 135°. Mối mở đai chọn ở giữa khoảng trống giữa các thanh dọc, thay vì đặt ngay lên thanh giữa cạnh. Kiểm tra tối đa bốn cặp chiều móc, chỉ nhận cặp thỏa cover và không giao thép đã dựng. Các cung uốn/móc do Revit tạo được kiểm tra lại. Có dự phòng một đường kính đai theo trục ở mỗi đầu cho phần móc tránh tự giao.

Nếu thiếu Hook Type 135° phù hợp, có thể tạo Hook Type mới. Nếu Bar Type gốc cấm hook đó, tạo **bản sao Type** cho phép hook; không sửa Type gốc. Nếu dự án chưa có RebarShape hoặc thiếu pool sáu tham số chiều dài, tạo một shape thẳng có tham số kích thước thật để bộ giải Revit có thể khớp/tạo shape. Tham số dùng GUID cố định, file shared parameter tạm được xóa và đường dẫn cấu hình trước đó được khôi phục. Nếu bộ giải từ chối, host được hoàn tác và log nêu lỗi Type/Shape, không giả lập thanh.

Chế độ khung kín không móc vẫn có trong UI; chỉ dùng nếu cấu tạo này đúng với yêu cầu của bạn. Đai phụ cần móc và sẽ từ chối khi tắt móc.

### Neo và nối

Neo nhập là **kéo dài thẳng mỗi đầu của thanh** sau khi xác định đoạn cơ sở hợp lệ. Không tự bẻ L, U hay tính chiều dài neo tiêu chuẩn. Phần kéo dài phải nằm trong bê tông thực tế của host hoặc cấu kiện bê tông Join trực tiếp đã xác minh; nếu không đủ thì báo lỗi, không rút ngắn yêu cầu để ép chạy.

Để phân đoạn, nhập chiều dài thanh phân đoạn và chiều dài nối chồng. Nối phải nhỏ hơn chiều dài phân đoạn. Các đoạn liên tiếp chồng đúng chiều dài nhập, luân phiên hai làn; làn thứ hai lệch vào trong bằng một đường kính mô hình cộng khe hở. Vị trí mối nối được chia đều theo chiều dài thanh từ đầu trục, **không tự bố trí vùng nối theo nội lực hoặc tỷ lệ nối**. Nếu không muốn chia thanh, để phân đoạn và nối bằng 0.

### Lưới vách và sàn

Vách thẳng, đứng: hai lưới trong/ngoài, mỗi lưới có thép đứng và ngang. Thép ngang lệch một đường kính so với thép đứng. Bước U là bước các thanh đứng; bước V là bước các thanh ngang.

Sàn: hai phương theo hệ trục của mặt phẳng sàn, chọn trên, dưới hoặc cả hai. Các mặt trên/dưới phải phẳng và song song, có chiều dày nhất quán. Có thể xử lý biên đa giác, sàn xoay, mặt nghiêng đều và lỗ mở khi API offset/clip được biên. Không thay lỗ mở bằng hộp bao. Nếu vùng sau offset tự giao hoặc quá hẹp, cần chia vùng hoặc sửa thông số.

Vách/sàn một lớp dùng kiểm tra host gốc của Revit, không yêu cầu thêm Asset ngoài điều kiện của API để tránh loại thư viện hợp lệ. Với nhiều lớp, lấy lớp được chỉ định bằng StructuralMaterialIndex và xác minh Structural Asset Concrete, trừ các lớp hoàn thiện. Nếu chưa chỉ định lớp chịu lực hoặc cấu tạo biến thiên không thể xác định chắc chắn thì từ chối cụ thể. Hình học thực sau Join/Cut vẫn được kiểm tra ở bước cuối.

## Kiểm tra hình học và lớp bảo vệ

1. Xác nhận danh mục, Rebar Host, vật liệu và Type.
2. Dầm/cột đọc GetOriginalGeometry theo tọa độ FamilySymbol và biến đổi bằng GetTransform; nhận diện từ contour tiết diện, không dùng tỷ lệ thể tích solid/hộp bao làm điều kiện chấp nhận.
3. Đọc GeometryElement/Solid/Face thực tế sau Join/Cut. Chỉ thêm vùng bê tông kết cấu đã xác minh của các host Join trực tiếp, không dùng hình học danh nghĩa để lấp một vết cắt thực.
4. Với miền lồi có mặt phẳng, kiểm tra nửa không gian đã offset cover. Với miền phức tạp dùng phép trừ solid của Revit cho ống cộng các cầu ở hai đầu, giữ đủ cover toàn thân và đầu thanh. Cung uốn được kiểm tra cực trị giải tích hoặc swept solid, không chỉ dùng vài điểm lấy mẫu.
5. Sau Regenerate, đọc GetTransformedCenterlineCurves ở **mọi vị trí** trong bộ thép, gồm móc/fillet. Kiểm tra lại cover, giao thép mới và giao với thép cũ khi thêm. Kiểm tra va chạm cung dùng dây cung có sai số võng tối đa 0,05 mm và dung sai giao 0,2 mm.
6. Lỗi, cảnh báo của Revit ở commit đều ghi log và hoàn tác host; không xóa cảnh báo để cho host qua.

Đường tâm hoặc cung không kiểm chứng được sẽ không được lưu. Không tự đổi thành dạng thép khác để vượt kiểm tra.

## Đọc lỗi và log

Hộp thoại luôn hiện Element ID và tối đa bốn nguyên nhân đầu. Có nút mở log hoặc chọn các cấu kiện lỗi. Log UTF-8 tại:

`%LOCALAPPDATA%\KetCauViet\Logs\rebar-*.log`

Log ghi thông số nhập, Type ID, vật liệu/host, số solid, thể tích trước/sau Join, tiết diện tại từng trạm, hệ tọa độ cục bộ, host liên kết được dùng, vùng thanh/đai, hook/type/shape phát sinh, ID các bộ thép mới, kết quả kiểm tra và exception đầy đủ.

| Mã | Ý nghĩa và việc cần kiểm tra |
|---|---|
| HOST | Danh mục, vật liệu bê tông, Structural hoặc Rebar Host chưa hợp lệ |
| PROFILE | Tiết diện, trục cong, biên offset/lỗ mở hoặc vùng đai liên tục chưa xử lý được |
| BAR_TYPE | Thiếu Type/Shape, đường kính hoặc đường kính uốn không hợp lệ |
| COVER | Thiếu chỗ cho cover/thép, neo vượt bê tông, móc/uốn sai vị trí hoặc thanh mới giao nhau |
| INPUT | Thông số vùng, số thanh, bước, chiều dài neo/nối không hợp lệ |
| EXISTING | Host đã có thép và được giữ nguyên theo lựa chọn; đếm là bỏ qua, không phải lỗi |
| EXISTING_CLASH | Thép mới giao với thép cũ; không sửa hoặc xóa thép cũ |
| REBAR_API | Revit từ chối hình học, giao dịch hoặc bộ giải; đọc exception/native failure trong log |

Nếu công cụ báo 0 host thành công, xem nguyên nhân hiển thị và log, không chỉ tăng dung sai. Giới hạn một host là 5000 vị trí thanh; chia vùng trước khi vượt giới hạn.

## Phạm vi còn cần xử lý riêng

Dầm/cột trục cong, tiết diện tròn/T/I/L/rỗng, tiết diện thay đổi, vách cong/nghiêng và sàn nhiều mặt dốc hoặc thay đổi cao độ/chiều dày chưa có thuật toán cage riêng. Các vết cắt/lỗ làm lồng đai gián đoạn cũng cần chia vùng. Không tự tạo thép cho các trường hợp đó.

Các test Core là fixture hình học giải tích trên cloud. Chúng không thay cho chạy bộ giải Revit, kiểm tra native constraints, hiệu năng hoặc nghiệm thu mô hình. Checklist thực tế tại [KIEM-THU-REVIT.md](KIEM-THU-REVIT.md); tình trạng kiểm chứng tại [KET-QUA-KIEM-TRA.md](KET-QUA-KIEM-TRA.md).
