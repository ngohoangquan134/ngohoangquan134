using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using KetCauViet.Core;
using KetCauViet.Revit.Infrastructure;
using KetCauViet.Revit.UI;

namespace KetCauViet.Revit.Tools;

internal static class RebarTools
{
    public static void NumberRebar(ToolContext c)
    {
        var elements = c.Select("Chọn thép cần đánh số.", e => e is Rebar);
        using var input = InputDialog.Ask(c.App, "Đánh số thép", "Số Revit: gom vào Partition, khớp hình dạng theo Revit. Mark: nhóm theo type, shape, móc và chiều dài từng đoạn. Không ghi đè số của thép ngoài phạm vi chọn.",
            new Field("mode", "Cách đánh số", options: new[] { "Số thép Revit (Partition)", "Gán Mark theo hình dạng" }), new Field("partition", "Tên Partition (chế độ Số)", "KCV"),
            new Field("start", "Số bắt đầu", "1"), new Field("prefix", "Tiền tố (chế độ Mark)", "T-"), new Field("digits", "Số chữ số (chế độ Mark)", "3"));
        var start = input.Integer("start", 1, 99999999);
        if (input.Index("mode") == 0)
        {
            var partition = input.TextValue("partition");
            if (partition.Length == 0) throw new ArgumentException("Cần nhập tên Partition.");
            var ids = new HashSet<ElementId>(elements.Select(e => e.Id));
            var outside = new FilteredElementCollector(c.Doc).OfClass(typeof(Rebar)).Cast<Rebar>().Any(r => !ids.Contains(r.Id) && string.Equals(r.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.AsString(), partition, StringComparison.OrdinalIgnoreCase));
            if (outside) throw new ArgumentException("Partition này có thép ngoài phạm vi chọn. Hãy chọn toàn bộ Partition hoặc dùng tên Partition mới.");
            c.Write("Đánh số thép Revit", () =>
            {
                var schema = NumberingSchema.GetNumberingSchema(c.Doc, NumberingSchemaTypes.StructuralNumberingSchemas.Rebar);
                schema.AssignElementsToSequence(ids, partition); schema.RemoveGaps(partition); schema.ShiftNumbers(partition, start);
            });
            c.Summary("Đánh số thép", $"Đã đánh số {elements.Count} đối tượng trong Partition {partition}, bắt đầu từ {start}. Các thanh khớp hình dạng có cùng số theo cơ chế Revit.");
            return;
        }
        var chosen = new HashSet<ElementId>(elements.Select(e => e.Id));
        var used = new FilteredElementCollector(c.Doc).WhereElementIsNotElementType().Where(e => !chosen.Contains(e.Id)).Select(ToolContext.Mark);
        var sequence = new NumberSequence(input.TextValue("prefix"), start, input.Integer("digits", 1, 9), used);
        var groups = elements.Cast<Rebar>().GroupBy(r =>
        {
            var lengths = r.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, 0).Select(curve => Math.Round(curve.Length * 304.8, 1).ToString("0.0", CultureInfo.InvariantCulture));
            return r.GetTypeId().IntegerValue + ":" + r.GetShapeId().IntegerValue + ":" + r.GetHookTypeId(0).IntegerValue + ":" + r.GetHookTypeId(1).IntegerValue + ":" + string.Join(",", lengths);
        }).OrderBy(g => g.Key).ToList();
        var assigned = new Dictionary<ElementId, string>();
        foreach (var group in groups) { var mark = sequence.Next(); foreach (var bar in group) assigned[bar.Id] = mark; }
        c.Batch("Gán Mark cốt thép", elements, e =>
        {
            var p = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (p == null || p.IsReadOnly) throw new ArgumentException("Tham số Mark không ghi được.");
            p.Set(assigned[e.Id]);
        });
    }

    public static void ScheduleRebar(ToolContext c)
    {
        using var input = InputDialog.Ask(c.App, "Bảng thống kê thép", "Tạo schedule cho toàn bộ cốt thép trong dự án, mỗi đối tượng/bộ thép một dòng.", new Field("name", "Tên bảng", "KCV - Thống kê cốt thép"));
        if (input.TextValue("name").Length == 0) throw new ArgumentException("Cần nhập tên bảng.");
        ViewSchedule? schedule = null;
        c.Write("Tạo bảng thống kê thép", () =>
        {
            schedule = ViewSchedule.CreateSchedule(c.Doc, new ElementId(BuiltInCategory.OST_Rebar));
            schedule.Name = GeometryTools.UniqueViewName(c.Doc, input.TextValue("name"));
            var wanted = new[] { BuiltInParameter.NUMBER_PARTITION_PARAM, BuiltInParameter.REBAR_NUMBER, BuiltInParameter.ALL_MODEL_MARK, BuiltInParameter.ELEM_TYPE_PARAM, BuiltInParameter.REBAR_BAR_DIAMETER, BuiltInParameter.REBAR_ELEM_LENGTH, BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS, BuiltInParameter.REBAR_ELEM_TOTAL_LENGTH };
            var headings = new[] { "Phân vùng", "Số thép Revit", "Ký hiệu", "Loại thép", "Đường kính", "Chiều dài thanh", "Số lượng", "Tổng chiều dài" };
            var fields = schedule.Definition.GetSchedulableFields();
            for (var i = 0; i < wanted.Length; i++)
            {
                var available = fields.FirstOrDefault(f => f.ParameterId == new ElementId(wanted[i]));
                if (available == null) continue;
                schedule.Definition.AddField(available).ColumnHeading = headings[i];
            }
            if (schedule.Definition.GetFieldCount() == 0) throw new InvalidOperationException("Không có trường thống kê thép phù hợp.");
            schedule.Definition.IsItemized = true;
        });
        c.Ui.ActiveView = schedule!;
    }

    public static void BeamRebar(ToolContext c) => Layout(c, "beam");
    public static void ColumnWallRebar(ToolContext c) => Layout(c, "columnwall");
    public static void FloorRebar(ToolContext c) => Layout(c, "floor");

    private static void Layout(ToolContext c, string mode)
    {
        var elements = c.Select("Chọn cấu kiện để bố trí thép.", e => mode == "beam" ? ToolContext.Category(e, BuiltInCategory.OST_StructuralFraming) : mode == "floor" ? e is Floor : ToolContext.Category(e, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Walls));
        var types = new FilteredElementCollector(c.Doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().OrderBy(t => t.Name).ToList();
        if (types.Count == 0) throw new ArgumentException("Dự án chưa có Rebar Bar Type. Hãy nạp/chọn template kết cấu có loại thép.");
        var fields = new List<Field>
        {
            new("main", "Loại thép dọc/lưới", options: types.Select(t => t.Name).ToArray()),
            new("tie", "Loại thép đai (dầm/cột)", options: types.Select(t => t.Name).ToArray()),
            new("cover", "Bảo vệ tới mặt thép ngoài (mm)", "25"),
            new("spacing", mode == "beam" ? "Bước đai (mm)" : mode == "columnwall" ? "Bước đai/lưới vách (mm)" : "Bước lưới (mm)", "150"),
            new("existing", "Nếu host đã có thép", options: new[] { "Bỏ qua host đó", "Giữ thép cũ và thêm thép mới" })
        };
        if (mode == "beam") { fields.Add(new Field("bottom", "Số thép dưới", "3")); fields.Add(new Field("top", "Số thép trên", "2")); }
        else if (mode == "columnwall") { fields.Add(new Field("nx", "Số thanh/cạnh X của cột", "3")); fields.Add(new Field("ny", "Số thanh/cạnh Y của cột", "3")); }
        else fields.Add(new Field("layers", "Lớp thép sàn", options: new[] { "Cả trên và dưới", "Chỉ trên", "Chỉ dưới" }));
        using var input = InputDialog.Ask(c.App, "Bố trí cốt thép", "Theo thông số nhập, chỉ khối hộp chữ nhật. Không tự tính chịu lực, neo/nối hoặc móc đai. Đai là khung kín không móc; cần hoàn thiện cấu tạo trước khi phát hành.", fields.ToArray());
        var cover = Engineering.Feet(input.Number("cover", 1, 1000)); var spacing = Engineering.Feet(input.Number("spacing", 1, 10000));
        var main = types[input.Index("main")]; var tie = types[input.Index("tie")];
        if (spacing <= Math.Max(main.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble(), tie.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble())) throw new ArgumentException("Bước thép phải lớn hơn đường kính thanh.");
        var bottom = mode == "beam" ? input.Integer("bottom", 2, 100) : 2; var top = mode == "beam" ? input.Integer("top", 2, 100) : 2;
        var nx = mode == "columnwall" ? input.Integer("nx", 2, 100) : 2; var ny = mode == "columnwall" ? input.Integer("ny", 2, 100) : 2;
        var layers = mode == "floor" ? input.Index("layers") : 0;
        var layout = new RebarLayout(c, main, tie, cover, spacing);
        c.Batch("Bố trí cốt thép", elements, e =>
        {
            if (input.Index("existing") == 0 && (RebarHostData.GetRebarHostData(e)?.GetRebarsInHost().Count ?? 0) > 0) throw new ArgumentException("Host đã có thép; bỏ qua theo lựa chọn.");
            if (mode == "beam") layout.Beam(e, bottom, top);
            else if (mode == "floor") layout.Floor(e, layers);
            else if (e is Wall) layout.Wall(e);
            else layout.Column(e, nx, ny);
        });
    }

    public static void RebarUtilities(ToolContext c)
    {
        using var input = InputDialog.Ask(c.App, "Tiện ích cốt thép", "Hiện rõ thép; với 3D đặt Detail Level của view thành Fine. Hoặc chọn thép theo host.", new Field("mode", "Tác vụ", options: new[] { "Hiện thép rõ, 3D Fine", "Tắt hiện rõ", "Chọn thép của host được chọn" }));
        if (input.Index("mode") == 2)
        {
            var hosts = c.Select("Chọn host có cốt thép.");
            var ids = hosts.SelectMany(e => RebarHostData.GetRebarHostData(e)?.GetRebarsInHost() ?? new List<Rebar>()).Select(r => r.Id).ToList();
            c.Ui.Selection.SetElementIds(ids); c.Summary("Chọn thép theo host", $"Đã chọn {ids.Count} đối tượng/bộ thép."); return;
        }
        var rebars = c.Select("Chọn thép cần đổi hiển thị.", e => e is Rebar);
        var on = input.Index("mode") == 0;
        c.Batch("Hiển thị cốt thép", rebars, e =>
        {
            var rebar = (Rebar)e; rebar.SetUnobscuredInView(c.Doc.ActiveView, on);
            if (c.Doc.ActiveView is View3D view)
            {
#if REVIT2022
                rebar.SetSolidInView(view, on);
#endif
                if (on) view.DetailLevel = ViewDetailLevel.Fine;
            }
        });
    }
}
