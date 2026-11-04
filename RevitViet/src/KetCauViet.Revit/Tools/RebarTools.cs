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
        var types = new FilteredElementCollector(c.Doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().OrderBy(t => t.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble()).ThenBy(t => t.Name).ToList();
        if (types.Count == 0) throw new RebarIssue("BAR_TYPE", "Dự án chưa có Rebar Bar Type. Nạp loại thép vào dự án trước khi bố trí.");
        string Label(RebarBarType t) => t.Name + " · Ø" + ToolContext.N(t.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble() * 304.8) + " · ID " + t.Id.IntegerValue;
        var labels = types.Select(Label).ToArray();
        var defaultMain = types.OrderBy(t => Math.Abs(t.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble() * 304.8 - 16)).First();
        var defaultTie = types.OrderBy(t => Math.Abs(t.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble() * 304.8 - 8)).First();
        var hasColumn = elements.Any(e => ToolContext.Category(e, BuiltInCategory.OST_StructuralColumns));
        var hasGrid = elements.Any(e => e is Wall || e is Floor);
        var hasCage = mode == "beam" || hasColumn;
        var fields = new List<Field>
        {
            new("main","Rebar Bar Type dọc hoặc lưới",Label(defaultMain),labels,"Chung"),
            new("diameter","Đường kính danh nghĩa (0 theo Type) mm","0",group:"Chung"),
            new("coverMode","Chọn lớp bảo vệ",options:new[]{"Theo host và dùng giá trị lớn nhất","Nhập đồng đều cho mọi mặt"},group:"Chung"),
            new("cover","Lớp bảo vệ nhập (mm)","25",group:"Chung"),
            new("gap","Khe hở giữa thanh song song hoặc làn nối (mm)","25",group:"Chung"),
            new("existing","Nếu host đã có thép",options:new[]{"Giữ nguyên và bỏ qua host","Thêm thép và kiểm tra giao với thép cũ"},group:"Chung"),
            new("verify","Chế độ thực hiện",options:new[]{"Tạo thép và lưu thay đổi","Kiểm tra thử và hoàn tác mọi thay đổi"},group:"Chung"),
            new("anchor","Kéo dài neo thẳng mỗi đầu (mm)","0",group:"Neo và nối"),
            new("stock","Chiều dài thanh phân đoạn (0 không chia) mm","0",group:"Neo và nối"),
            new("lap","Chiều dài nối chồng giữa các phân đoạn (mm)","0",group:"Neo và nối")
        };
        if (mode == "beam")
        {
            fields.AddRange(new[]{
                new Field("bottom","Số thanh dưới","3",group:"Dầm"),
                new Field("top","Số thanh trên","2",group:"Dầm"),
                new Field("extraTop","Số thanh tăng cường trên tại mỗi đầu","0",group:"Dầm"),
                new Field("extraBottom","Số thanh tăng cường dưới giữa nhịp","0",group:"Dầm"),
                new Field("extraLength","Chiều dài mỗi vùng tăng cường (mm)","1500",group:"Dầm")});
        }
        if (hasColumn) fields.AddRange(new[]{
            new Field("nx","Số thanh trên mỗi cạnh U","3",group:"Cột"),
            new Field("ny","Số thanh trên mỗi cạnh V","3",group:"Cột"),
            new Field("auxiliary","Đai phụ cho các thanh giữa cạnh",options:new[]{"Không thêm","Có thêm cùng nhịp đai chính"},group:"Cột")});
        if (hasCage) fields.AddRange(new[]{
            new Field("tie","Rebar Bar Type đai",Label(defaultTie),labels,"Đai"),
            new Field("tieDiameter","Đường kính danh nghĩa đai (0 theo Type) mm","0",group:"Đai"),
            new Field("hooks","Móc đai",options:new[]{"Móc 135 độ và kiểm tra hình học","Khung kín không móc"},group:"Đai"),
            new Field("leftLength","Chiều dài vùng đầu từ đai hợp lệ đầu (mm)","0",group:"Đai"),
            new Field("rightLength","Chiều dài vùng cuối từ đai hợp lệ cuối (mm)","0",group:"Đai"),
            new Field("leftStep","Bước tối đa vùng đầu (mm)","100",group:"Đai"),
            new Field("middleStep","Bước tối đa vùng giữa (mm)","150",group:"Đai"),
            new Field("rightStep","Bước tối đa vùng cuối (mm)","100",group:"Đai")});
        if (hasGrid) fields.AddRange(new[]{
            new Field("spacingU","Bước tối đa theo U hoặc thép đứng vách (mm)","150",group:"Lưới"),
            new Field("spacingV","Bước tối đa theo V hoặc thép ngang vách (mm)","150",group:"Lưới")});
        if (mode == "floor") fields.Add(new Field("layers", "Lớp thép sàn", options: new[] { "Cả trên và dưới", "Chỉ trên", "Chỉ dưới" }, group: "Lưới"));
        using var input = InputDialog.Ask(c.App, "Bố trí cốt thép", "Thông số bố trí do bạn nhập, không phải kết quả tính chịu lực. Neo chỉ kéo dài khi đủ bê tông; thanh nối chia thành hai làn lệch vào trong. Thép cũ luôn được giữ nguyên.", fields.ToArray());
        RebarBarType Resolve(RebarBarType selected, double diameter)
        {
            if (diameter == 0) return selected;
            if (Math.Abs(selected.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble() * 304.8 - diameter) < .05) return selected;
            var matched = types.FirstOrDefault(t => Math.Abs(t.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble() * 304.8 - diameter) < .05);
            return matched ?? throw new RebarIssue("BAR_TYPE", "Chưa có Rebar Bar Type đường kính " + diameter + " mm. Nạp hoặc tạo Type đúng vật liệu và thông số uốn trước khi chạy.");
        }
        var options = new RebarLayoutSettings
        {
            Main = Resolve(types[input.Index("main")], input.Number("diameter", 0, 100)),
            Tie = hasCage ? Resolve(types[input.Index("tie")], input.Number("tieDiameter", 0, 100)) : types[input.Index("main")],
            CoverFromHost = input.Index("coverMode") == 0,
            Cover = Engineering.Feet(input.Number("cover", 1, 1000)),
            ClearGap = Engineering.Feet(input.Number("gap", 0, 1000)),
            AddToExisting = input.Index("existing") == 1,
            VerifyOnly = input.Index("verify") == 1,
            Anchor = Engineering.Feet(input.Number("anchor", 0, 100000)),
            Stock = Engineering.Feet(input.Number("stock", 0, 100000)),
            Lap = Engineering.Feet(input.Number("lap", 0, 100000))
        };
        if (mode == "beam")
        {
            options.Bottom = input.Integer("bottom", 2, 100); options.Top = input.Integer("top", 2, 100);
            options.ExtraTop = input.Integer("extraTop", 0, 100); options.ExtraBottom = input.Integer("extraBottom", 0, 100);
            options.ExtraLength = Engineering.Feet(input.Number("extraLength", 0, 100000));
        }
        if (hasColumn) { options.Nx = input.Integer("nx", 2, 100); options.Ny = input.Integer("ny", 2, 100); options.AuxiliaryTies = input.Index("auxiliary") == 1; }
        if (hasCage)
        {
            options.Hooks = input.Index("hooks") == 0; options.LeftLength = Engineering.Feet(input.Number("leftLength", 0, 100000)); options.RightLength = Engineering.Feet(input.Number("rightLength", 0, 100000));
            options.LeftSpacing = Engineering.Feet(input.Number("leftStep", 1, 10000)); options.MiddleSpacing = Engineering.Feet(input.Number("middleStep", 1, 10000)); options.RightSpacing = Engineering.Feet(input.Number("rightStep", 1, 10000));
        }
        if (hasGrid) { options.SpacingU = Engineering.Feet(input.Number("spacingU", 1, 10000)); options.SpacingV = Engineering.Feet(input.Number("spacingV", 1, 10000)); }
        if (mode == "floor") options.Layers = input.Index("layers");
        options.Description = string.Join("; ", fields.Select(f => f.Label + "=" + input.TextValue(f.Key)));
        RebarExecution.Run(c, "Bố trí cốt thép", elements, options.Description, (element, log) =>
        {
            if (!options.AddToExisting && (RebarHostData.GetRebarHostData(element)?.GetRebarsInHost().Count ?? 0) > 0) throw new RebarIssue("EXISTING", "Host đã có thép; giữ nguyên theo lựa chọn.");
            new RebarLayout(c, element, options, log).Execute();
        }, options.VerifyOnly);
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
