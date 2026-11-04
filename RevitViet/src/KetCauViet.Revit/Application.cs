using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Autodesk.Revit.UI;

namespace KetCauViet.Revit;

public sealed class Application : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        const string tab = "KẾT CẤU VIỆT";
        try { application.CreateRibbonTab(tab); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { }
        var definitions = new[]
        {
            new[] {"Kết cấu", "AutoJoin", "Nối hình học", "JN", "Tự nối các cấu kiện được chọn có hộp bao giao nhau."},
            new[] {"Kết cấu", "CadModel", "Dựng từ CAD", "CAD", "Dựng dầm hoặc tường từ đường thẳng/cung tròn trên layer CAD được chọn."},
            new[] {"Kết cấu", "ChangeFramingType", "Đổi kiểu dầm", "T", "Đổi family type cho các dầm được chọn."},
            new[] {"Kết cấu", "PileCoordinates", "Tọa độ cọc", "XY", "Xuất tọa độ các móng/cọc dạng FamilyInstance theo hệ nội bộ hoặc hệ chia sẻ."},
            new[] {"Kết cấu", "AutoNumber", "Đánh số cấu kiện", "01", "Đánh Mark theo thứ tự cao độ, Y, X; tự tránh mã đã dùng."},
            new[] {"Kết cấu", "LiningConcrete", "Bê tông lót", "BT", "Tạo sàn lót bên dưới mặt đáy nằm ngang của sàn được chọn."},
            new[] {"Kết cấu", "ToggleJoins", "Cho/không nối", "±", "Cho phép hoặc cấm nối đầu dầm/tường."},
            new[] {"Kết cấu", "CutByFace", "Cắt dầm theo mặt", "CUT", "Chọn mặt của khối cắt; áp dụng SolidSolidCut cho các dầm Revit cho phép cắt."},
            new[] {"Kết cấu", "StructuralUtilities", "Tiện ích kết cấu", "…", "Cô lập cấu kiện, bỏ cô lập hoặc xuất kiểm tra mã và thể tích."},
            new[] {"Cốt thép", "NumberRebar", "Đánh mã thép", "R01", "Gán Mark chung cho thép cùng type, shape và chiều dài thanh."},
            new[] {"Cốt thép", "ScheduleRebar", "Bảng thống kê thép", "ΣR", "Tạo Revit schedule cốt thép cho toàn dự án."},
            new[] {"Cốt thép", "BeamRebar", "Thép dầm", "RD", "Đặt thép trên/dưới và đai cho dầm thẳng, tiết diện chữ nhật."},
            new[] {"Cốt thép", "ColumnWallRebar", "Thép cột/vách", "RC", "Đặt thép dọc/đai cột chữ nhật hoặc hai lớp thép cho vách thẳng chữ nhật."},
            new[] {"Cốt thép", "FloorRebar", "Thép sàn", "RS", "Đặt hai phương thép trên/dưới cho sàn phẳng chữ nhật."},
            new[] {"Cốt thép", "RebarUtilities", "Tiện ích cốt thép", "R+", "Chỉnh hiển thị thép hoặc chọn thép theo host."},
            new[] {"Bản vẽ", "LoadFamily", "Nạp family", "RFA", "Nạp một hoặc nhiều family .rfa."},
            new[] {"Bản vẽ", "BeamDrawing", "Mặt cắt dầm", "SEC", "Tạo mặt cắt dọc/ngang của các dầm được chọn."},
            new[] {"Bản vẽ", "Rebar2D", "Hình dạng thép 2D", "2D", "Chuyển hình dạng một thanh thép phẳng thành detail curves đúng chiều dài."},
            new[] {"Bản vẽ", "AutoDimension", "Ghi kích thước", "DIM", "Tạo chuỗi kích thước từ các mặt phẳng song song được chọn."},
            new[] {"Khối lượng", "FormworkArea", "Diện tích ván khuôn", "A", "Đo mặt bên/đáy theo hình học mô hình hiện tại."},
            new[] {"Khối lượng", "ConcreteVolume", "Bê tông cột/tường", "V", "Thống kê thể tích cột và tường được chọn."},
            new[] {"Khối lượng", "CoreWallVolume", "Bê tông vách lõi", "VC", "Thống kê thể tích toàn bộ các tường vách lõi do bạn chọn."},
            new[] {"Khối lượng", "PickFace", "Đo mặt", "FACE", "Đo diện tích, chu vi các mặt được chọn."},
            new[] {"Khối lượng", "SumParameter", "Cộng tham số", "Σ", "Cộng tham số số cùng đơn vị, xuất CSV."},
            new[] {"Khối lượng", "RebarWeight", "Khối lượng thép", "KG", "Tính khối lượng theo tổng chiều dài, đường kính danh nghĩa và khối lượng riêng."},
            new[] {"Khối lượng", "Waterproofing", "Chống thấm", "W", "Diện tích các mặt được chọn × số lớp × hệ số bù."},
            new[] {"Trợ giúp", "Help", "Hướng dẫn", "?", "Hướng dẫn tiếng Việt, phạm vi và cách cài đặt."},
        };
        var assembly = Assembly.GetExecutingAssembly().Location;
        var colors = new[] { "#0F766E", "#B45309", "#1D4ED8", "#7C3AED", "#475569" };
        var index = 0;
        foreach (var group in definitions.GroupBy(x => x[0]))
        {
            var panel = application.CreateRibbonPanel(tab, group.Key);
            var buttons = group.Select(t => new PushButtonData("KCV." + t[1], t[2], assembly, "KetCauViet.Revit.Commands." + t[1] + "Command")
            { ToolTip = t[4], LongDescription = "Chọn đối tượng trước hoặc chọn khi lệnh yêu cầu. Có thể hoàn tác thay đổi mô hình bằng Undo.", Image = Icon(t[3], colors[index]), LargeImage = Icon(t[3], colors[index]) }).ToList();
            while (buttons.Count >= 3) { panel.AddStackedItems(buttons[0], buttons[1], buttons[2]); buttons.RemoveRange(0, 3); }
            if (buttons.Count == 2) panel.AddStackedItems(buttons[0], buttons[1]);
            else if (buttons.Count == 1) panel.AddItem(buttons[0]);
            index++;
        }
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

    private static ImageSource Icon(string text, string color)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRoundedRectangle((Brush)new BrushConverter().ConvertFromString(color), null, new Rect(0, 0, 32, 32), 6, 6);
            var label = new FormattedText(text, CultureInfo.GetCultureInfo("vi-VN"), FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), text.Length > 2 ? 10 : 13, Brushes.White, 1);
            dc.DrawText(label, new Point((32 - label.Width) / 2, (32 - label.Height) / 2));
        }
        var image = new DrawingImage(group); image.Freeze(); return image;
    }
}
