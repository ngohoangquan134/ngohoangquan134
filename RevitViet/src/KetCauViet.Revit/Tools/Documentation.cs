using System.Diagnostics;
using System.IO;
using System.Reflection;
using Autodesk.Revit.UI;

namespace KetCauViet.Revit.Tools;

internal static class Documentation
{
    public static void Show()
    {
        var dialog = new TaskDialog("Kết Cấu Việt")
        {
            MainInstruction = "KẾT CẤU VIỆT 2.0 · 27 công cụ tiếng Việt",
            MainContent = "Kết cấu · Cốt thép · Bản vẽ · Khối lượng\n\nChọn đối tượng trước khi bấm lệnh, hoặc làm theo thanh trạng thái Revit. ESC để hủy; Undo để hoàn tác. Số liệu nhập dùng mm, báo cáo dùng m, m², m³ và kg. Thép đặt theo thông số bạn nhập, không tính thiết kế chịu lực.\n\nBản build: Revit " + Version,
            CommonButtons = TaskDialogCommonButtons.Close
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Mở hướng dẫn tiếng Việt");
        if (dialog.Show() == TaskDialogResult.CommandLink1)
            Process.Start(new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "HUONG-DAN.md")) { UseShellExecute = true });
    }
    public const string Version =
#if REVIT2022
        "2022";
#elif REVIT2023
        "2023";
#else
        "2024";
#endif
}
