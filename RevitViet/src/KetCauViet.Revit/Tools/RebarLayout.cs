using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using KetCauViet.Core;
using KetCauViet.Revit.Infrastructure;

namespace KetCauViet.Revit.Tools;

internal sealed class RebarLayoutSettings
{
    public RebarBarType Main { get; set; } = null!;
    public RebarBarType Tie { get; set; } = null!;
    public bool CoverFromHost { get; set; } = true;
    public double Cover { get; set; } = Engineering.Feet(25);
    public double ClearGap { get; set; } = Engineering.Feet(25);
    public bool AddToExisting { get; set; }
    public bool VerifyOnly { get; set; }
    public bool Hooks { get; set; } = true;
    public bool AuxiliaryTies { get; set; }
    public int Bottom { get; set; } = 3;
    public int Top { get; set; } = 2;
    public int Nx { get; set; } = 3;
    public int Ny { get; set; } = 3;
    public int ExtraTop { get; set; }
    public int ExtraBottom { get; set; }
    public double ExtraLength { get; set; } = Engineering.Feet(1500);
    public double LeftLength { get; set; }
    public double RightLength { get; set; }
    public double LeftSpacing { get; set; } = Engineering.Feet(100);
    public double MiddleSpacing { get; set; } = Engineering.Feet(150);
    public double RightSpacing { get; set; } = Engineering.Feet(100);
    public double SpacingU { get; set; } = Engineering.Feet(150);
    public double SpacingV { get; set; } = Engineering.Feet(150);
    public int Layers { get; set; }
    public double Anchor { get; set; }
    public double Stock { get; set; }
    public double Lap { get; set; }
    public string Description { get; set; } = "";
}

internal sealed class RebarLayout
{
    private readonly ToolContext c;
    private readonly RebarLayoutSettings options;
    private readonly RebarHostGeometry f;
    private readonly Action<string> log;
    private readonly double cover, d, td;
    private readonly List<BarPlan> plans = new();
    private readonly List<BarSegment> acceptedSegments = new();
    private RebarHookType? hook;
    private RebarBarType tie;
    private int positionCount;
    private const int MaximumPositions = 5000;

    private sealed class BarPlan
    {
        public IList<XYZ> Points { get; }
        public XYZ Normal { get; }
        public RebarBarType Type { get; }
        public RebarStyle Style { get; }
        public string Tag { get; }
        public bool Hook { get; }
        public BarPlan(IList<XYZ> points, XYZ normal, RebarBarType type, string tag, bool tie = false, bool hook = false)
        { Points = points; Normal = normal.Normalize(); Type = type; Tag = tag; Style = tie ? RebarStyle.StirrupTie : RebarStyle.Standard; Hook = hook; }
        public IList<Curve> Curves() => Enumerable.Range(1, Points.Count - 1).Select(i => (Curve)Line.CreateBound(Points[i - 1], Points[i])).ToList();
    }

    public RebarLayout(ToolContext c, Element host, RebarLayoutSettings options, Action<string> log)
    {
        this.c = c; this.options = options; this.log = log; f = new RebarHostGeometry(host, log);
        RebarTemplates.Validate(options.Main, c.Doc, false);
        tie = options.Tie; d = RebarTemplates.Diameter(options.Main); td = RebarTemplates.Diameter(tie);
        cover = RebarTemplates.Cover(host, options.CoverFromHost, options.Cover, log);
        if (cover <= 0 || !Engineering.Finite(cover)) throw new RebarIssue("COVER", "Lớp bảo vệ phải lớn hơn 0.");
        if (options.Lap > 0 && options.Stock <= options.Lap) throw new RebarIssue("INPUT", "Nối chồng cần chiều dài phân đoạn lớn hơn chiều dài nối.");
    }

    public void Execute()
    {
        if ((f.Kind == "beam" || f.Kind == "column") && options.Hooks) hook = RebarTemplates.Hook(c.Doc, ref tie, true, log);
        try { if (f.Kind == "beam") Beam(); else if (f.Kind == "column") Column(); else if (f.Kind == "wall") Wall(); else Floor(); }
        catch (System.ArgumentException ex) { throw new RebarIssue("INPUT", ex.Message, ex); }
        if (plans.Count == 0) throw new RebarIssue("COVER", "Không còn vị trí đặt thép sau khi trừ lớp bảo vệ và lỗ mở.");
        log("Kiểm tra trước khi tạo: " + plans.Count + " vị trí; cover=" + cover * 304.8 + " mm; model diameters=" + d * 304.8 + "/" + td * 304.8 + " mm");
        foreach (var p in plans) foreach (var curve in p.Curves()) f.RequireClearance(curve, cover + RebarTemplates.Diameter(p.Type) / 2, p.Tag);
        RebarTemplates.EnsureSeed(c.Doc, log);
        var created = new List<(Rebar Bar, string Tag)>();
        foreach (var group in Compress(plans)) created.Add((Create(group), group[0].Tag));
        c.Doc.Regenerate();
        var segments = new List<BarSegment>();
        foreach (var item in created) ReadSegments(item.Bar, item.Tag, segments, true, true);
        var clash = RebarClashes.FindCollision(segments, Engineering.Feet(.2), Engineering.Feet(200));
        if (clash.HasValue) throw new RebarIssue("COVER", "Hai thanh mới giao nhau: " + clash.Value.First + " / " + clash.Value.Second + ". Kiểm tra khoảng cách, lớp tăng cường hoặc làn nối.");
        if (options.AddToExisting)
        {
            var newIds = new HashSet<ElementId>(created.Select(x => x.Bar.Id)); var old = new List<BarSegment>();
            foreach (var bar in RebarHostData.GetRebarHostData(f.Host).GetRebarsInHost().Where(r => !newIds.Contains(r.Id))) ReadSegments(bar, "CŨ ID " + bar.Id.IntegerValue, old, false);
            var mixed = RebarClashes.FindCollision(segments.Concat(old), Engineering.Feet(.2), Engineering.Feet(200), (a, b) => !a.StartsWith("CŨ", StringComparison.Ordinal) || !b.StartsWith("CŨ", StringComparison.Ordinal));
            if (mixed.HasValue) throw new RebarIssue("EXISTING_CLASH", "Thép mới giao với thép đang có: " + mixed.Value.First + " / " + mixed.Value.Second + ". Không sửa hoặc xóa thép cũ.");
        }
        log("Đã kiểm tra mọi vị trí sau Regenerate, gồm móc/uốn, cover và giao thép; objects=" + created.Count + "; positions=" + positionCount);
    }

    private void Add(BarPlan plan)
    {
        if (plans.Count >= MaximumPositions) throw new RebarIssue("INPUT", "Host vượt 5000 vị trí thanh. Chia vùng hoặc tăng bước trước khi chạy.");
        if (plan.Points.Zip(plan.Points.Skip(1), (a, b) => a.DistanceTo(b)).Any(length => length < c.Doc.Application.ShortCurveTolerance)) throw new RebarIssue("PROFILE", plan.Tag + ": đoạn ngắn hơn ShortCurveTolerance.");
        plans.Add(plan);
    }
    private IReadOnlyList<double> Across(double a, double b, int count)
    {
        if (b <= a) throw new RebarIssue("COVER", "Không đủ tiết diện sau khi trừ cover, đai và đường kính.");
        if (count == 1) return new[] { (a + b) / 2 };
        if ((b - a) / (count - 1) < d + options.ClearGap) throw new RebarIssue("COVER", "Khoảng hở giữa các thanh dọc nhỏ hơn giá trị nhập. Giảm số lượng/đường kính hoặc sửa tiết diện.");
        return Engineering.EvenPositions(a, b, count);
    }
    private void Longitudinal(double u, double v, double start, double end, XYZ normal, XYZ inward, string tag)
    {
        var point = f.Point(u, v, start); var intervals = f.SafeIntervals(point, f.Point(u, v, end), cover + d / 2);
        if (intervals.Count != 1) throw new RebarIssue("COVER", tag + ": đường thép bị mất đoạn hoặc không đủ cover sau Join/Cut; cần chia vùng hoặc sửa thông số.");
        var interval = intervals[0]; log(tag + ": tâm thanh S=" + (start + interval.Start) * 304.8 + ".." + (start + interval.End) * 304.8 + " mm");
        AddStraight(point + f.Axis * interval.Start, point + f.Axis * interval.End, normal, inward, tag);
    }
    private void AddStraight(XYZ a, XYZ b, XYZ normal, XYZ inward, string tag)
    {
        var direction = (b - a).Normalize(); a -= direction * options.Anchor; b += direction * options.Anchor;
        if (options.Anchor > 0) log(tag + ": kéo dài neo thẳng mỗi đầu " + options.Anchor * 304.8 + " mm; không rút ngắn khi thiếu bê tông.");
        var pieces = RebarPlanning.Splice(0, a.DistanceTo(b), options.Stock, options.Lap);
        for (var i = 0; i < pieces.Count; i++)
        {
            var offset = i % 2 == 0 ? XYZ.Zero : inward.Normalize() * (d + options.ClearGap);
            Add(new BarPlan(new[] { a + direction * pieces[i].Start + offset, a + direction * pieces[i].End + offset }, normal, options.Main, tag + " / đoạn " + (i + 1)));
        }
    }
    private (double U0, double U1, double V0, double V1) MainRectangle()
    {
        RebarTemplates.Validate(tie, c.Doc, true);
        var t = cover + td / 2; var bend = (tie.StirrupTieBendDiameter + td) / 2;
        var inset = RebarClearance.LongitudinalInset(td / 2, d / 2, bend);
        var a = f.Min.X + t + inset; var b = f.Max.X - t - inset; var x = f.Min.Y + t + inset; var y = f.Max.Y - t - inset;
        if (b - a < d || y - x < d) throw new RebarIssue("COVER", "Tiết diện không đủ cho đường kính uốn đai, thép dọc và cover.");
        return (a, b, x, y);
    }
    private void Beam()
    {
        var r = MainRectangle();
        foreach (var u in Across(r.U0, r.U1, options.Bottom)) Longitudinal(u, r.V0, f.Min.Z, f.Max.Z, f.U, f.V, "Dầm dưới");
        foreach (var u in Across(r.U0, r.U1, options.Top)) Longitudinal(u, r.V1, f.Min.Z, f.Max.Z, f.U, -f.V, "Dầm trên");
        if ((options.ExtraTop > 0 || options.ExtraBottom > 0) && options.ExtraLength <= 0) throw new RebarIssue("INPUT", "Chiều dài thép tăng cường phải dương.");
        if (options.ExtraTop > 0)
        {
            if (2 * (options.ExtraLength + options.Anchor) > f.Max.Z - f.Min.Z) throw new RebarIssue("INPUT", "Hai vùng tăng cường trên chồng nhau; giảm chiều dài hoặc bố trí thanh liên tục.");
            foreach (var u in Across(r.U0, r.U1, options.ExtraTop))
            {
                var v = r.V1 - d - options.ClearGap;
                Longitudinal(u, v, f.Min.Z, f.Min.Z + options.ExtraLength, f.U, -f.V, "Tăng cường trên đầu");
                Longitudinal(u, v, f.Max.Z - options.ExtraLength, f.Max.Z, f.U, -f.V, "Tăng cường trên cuối");
            }
        }
        if (options.ExtraBottom > 0)
        {
            if (options.ExtraLength > f.Max.Z - f.Min.Z) throw new RebarIssue("INPUT", "Thép tăng cường giữa nhịp dài hơn dầm.");
            var middle = (f.Min.Z + f.Max.Z) / 2;
            foreach (var u in Across(r.U0, r.U1, options.ExtraBottom)) Longitudinal(u, r.V0 + d + options.ClearGap, middle - options.ExtraLength / 2, middle + options.ExtraLength / 2, f.U, f.V, "Tăng cường dưới giữa nhịp");
        }
        Cage(false, r);
    }
    private void Column()
    {
        var r = MainRectangle(); var center = f.Point((r.U0 + r.U1) / 2, (r.V0 + r.V1) / 2, f.Min.Z);
        foreach (var u in Across(r.U0, r.U1, options.Nx)) foreach (var v in new[] { r.V0, r.V1 }) Longitudinal(u, v, f.Min.Z, f.Max.Z, f.U, center - f.Point(u, v, f.Min.Z), "Cột dọc cạnh U " + v);
        foreach (var v in Across(r.V0, r.V1, options.Ny).Skip(1).Take(options.Ny - 2)) foreach (var u in new[] { r.U0, r.U1 }) Longitudinal(u, v, f.Min.Z, f.Max.Z, f.V, center - f.Point(u, v, f.Min.Z), "Cột dọc cạnh V " + u);
        Cage(true, r);
    }
    private void Cage(bool column, (double U0, double U1, double V0, double V1) main)
    {
        var t = cover + td / 2; var u0 = f.Min.X + t; var u1 = f.Max.X - t; var v0 = f.Min.Y + t; var v1 = f.Max.Y - t;
        var bend = (tie.StirrupTieBendDiameter + td) / 2;
        if (u1 - u0 <= 2 * bend || v1 - v0 <= 2 * bend) throw new RebarIssue("BAR_TYPE", "Bán kính uốn đai không vừa kích thước tâm đai.");
        var start = f.Min.Z; var end = f.Max.Z;
        foreach (var uv in new[] { new UV(u0, v0), new UV(u1, v0), new UV(u1, v1), new UV(u0, v1) })
        {
            var intervals = f.SafeIntervals(f.Point(uv.U, uv.V, f.Min.Z), f.Point(uv.U, uv.V, f.Max.Z), t);
            if (intervals.Count != 1) throw new RebarIssue("PROFILE", "Lồng đai bị gián đoạn bởi lỗ/cắt; không thể rải một lồng liên tục an toàn.");
            start = Math.Max(start, f.Min.Z + intervals[0].Start); end = Math.Min(end, f.Min.Z + intervals[0].End);
        }
        var auxiliary = column && options.AuxiliaryTies && (options.Nx > 2 || options.Ny > 2); var shift = td + Engineering.Feet(2);
        if (options.Hooks) { start += td; end -= td; log("Dự phòng 1 đường kính theo trục mỗi đầu cho phần móc/đai tránh tự giao."); }
        if (auxiliary && !options.Hooks) throw new RebarIssue("INPUT", "Đai phụ cần móc 135°. Hãy bật chế độ móc.");
        var auxiliaryOffset = auxiliary ? shift * ((options.Nx > 2 && options.Ny > 2) ? 2 : 1) : 0;
        if (auxiliary) end -= auxiliaryOffset;
        if (end - start <= c.Doc.Application.ShortCurveTolerance) throw new RebarIssue("COVER", "Không có vùng đủ dài cho lồng đai sau khi xét đầu cắt và cover.");
        if (new[] { options.LeftSpacing, options.MiddleSpacing, options.RightSpacing }.Min() < td + Engineering.Feet(2) + auxiliaryOffset) throw new RebarIssue("INPUT", "Bước đai quá nhỏ gây giao đai hoặc đai phụ.");
        var stations = RebarPlanning.Zones(start, end, options.LeftLength, options.RightLength, options.LeftSpacing, options.MiddleSpacing, options.RightSpacing);
        log("Đai 3 vùng: S=" + start * 304.8 + ".." + end * 304.8 + " mm; số vị trí=" + stations.Count);
        var seam = RebarClearance.HookSeam(Across(main.U0, main.U1, column ? options.Nx : options.Top));
        foreach (var s in stations)
        {
            XYZ[] points;
            if (options.Hooks)
            {
                var middle = seam; var half = td;
                if (u1 - u0 <= 2 * bend + 2 * half) throw new RebarIssue("BAR_TYPE", "Đai quá hẹp để bố trí mối mở và hai móc 135°.");
                points = new[] { f.Point(middle + half, v1, s), f.Point(u1, v1, s), f.Point(u1, v0, s), f.Point(u0, v0, s), f.Point(u0, v1, s), f.Point(middle - half, v1, s) };
            }
            else points = new[] { f.Point(u0, v0, s), f.Point(u1, v0, s), f.Point(u1, v1, s), f.Point(u0, v1, s), f.Point(u0, v0, s) };
            Add(new BarPlan(points, f.Axis, tie, "Đai chính", true, options.Hooks));
            if (!auxiliary) continue;
            foreach (var u in Across(main.U0, main.U1, options.Nx).Skip(1).Take(options.Nx - 2)) Add(new BarPlan(new[] { f.Point(u + (d + td) / 2, v0, s + shift), f.Point(u + (d + td) / 2, v1, s + shift) }, f.Axis, tie, "Đai phụ U", true, true));
            var vShift = options.Nx > 2 ? 2 * shift : shift;
            foreach (var v in Across(main.V0, main.V1, options.Ny).Skip(1).Take(options.Ny - 2)) Add(new BarPlan(new[] { f.Point(u0, v + (d + td) / 2, s + vShift), f.Point(u1, v + (d + td) / 2, s + vShift) }, f.Axis, tie, "Đai phụ V", true, true));
        }
    }
    private void Wall()
    {
        if (options.SpacingU < d + options.ClearGap || options.SpacingV < d + options.ClearGap) throw new RebarIssue("INPUT", "Bước lưới vách nhỏ hơn đường kính cộng khoảng hở.");
        var r = cover + d / 2; var lo = f.Min.Y + r; var hi = f.Max.Y - r;
        if (hi - lo < 3 * d + options.ClearGap) throw new RebarIssue("COVER", "Vách không đủ chiều dày cho hai lớp thép đứng/ngang và khe hở giữa hai lưới.");
        var masks = f.PlanarMask(r, f.V);
        foreach (var back in new[] { false, true })
        {
            var y = back ? hi : lo; var inner = back ? -f.V : f.V; var horizontal = back ? y - d : y + d;
            foreach (var u in RebarPlanning.Spaced(f.Min.X + r + RebarHostGeometry.Tolerance, f.Max.X - r - RebarHostGeometry.Tolerance, options.SpacingU)) Grid(f.Point(u, y, f.Min.Z - 1), f.Point(u, y, f.Max.Z + 1), f.U, inner, "Vách đứng " + back, masks);
            foreach (var z in RebarPlanning.Spaced(f.Min.Z + r + RebarHostGeometry.Tolerance, f.Max.Z - r - RebarHostGeometry.Tolerance, options.SpacingV)) Grid(f.Point(f.Min.X - 1, horizontal, z), f.Point(f.Max.X + 1, horizontal, z), f.Axis, inner, "Vách ngang " + back, masks);
        }
    }
    private void Floor()
    {
        if (options.SpacingU < d + options.ClearGap || options.SpacingV < d + options.ClearGap) throw new RebarIssue("INPUT", "Bước lưới sàn nhỏ hơn đường kính cộng khoảng hở.");
        var r = cover + d / 2; var needed = options.Layers == 0 ? 2 * cover + 4 * d + options.ClearGap : 2 * cover + 2 * d;
        if (f.Max.Z - f.Min.Z < needed) throw new RebarIssue("COVER", "Sàn không đủ chiều dày cho số lớp và khoảng hở đã nhập.");
        var masks = f.PlanarMask(r, f.Axis);
        foreach (var top in options.Layers == 0 ? new[] { false, true } : new[] { options.Layers == 1 })
        {
            var z = top ? f.Max.Z - r : f.Min.Z + r; var second = top ? z - d : z + d; var inward = top ? -f.Axis : f.Axis;
            foreach (var v in RebarPlanning.Spaced(f.Min.Y + r + RebarHostGeometry.Tolerance, f.Max.Y - r - RebarHostGeometry.Tolerance, options.SpacingV)) Grid(f.Point(f.Min.X - 1, v, z), f.Point(f.Max.X + 1, v, z), f.V, inward, "Sàn U " + top, masks);
            foreach (var u in RebarPlanning.Spaced(f.Min.X + r + RebarHostGeometry.Tolerance, f.Max.X - r - RebarHostGeometry.Tolerance, options.SpacingU)) Grid(f.Point(u, f.Min.Y - 1, second), f.Point(u, f.Max.Y + 1, second), f.U, inward, "Sàn V " + top, masks);
        }
    }
    private void Grid(XYZ start, XYZ end, XYZ normal, XYZ inward, string tag, IList<Solid> masks)
    {
        var line = Line.CreateBound(start, end); var direction = (end - start).Normalize(); var intervals = new List<LinearInterval>();
        foreach (var mask in masks)
        {
            using var option = new SolidCurveIntersectionOptions(); using var result = mask.IntersectWithCurve(line, option);
            for (var i = 0; i < result.SegmentCount; i++) { var segment = result.GetCurveSegment(i); var a = (segment.GetEndPoint(0) - start).DotProduct(direction); var b = (segment.GetEndPoint(1) - start).DotProduct(direction); intervals.Add(new LinearInterval(Math.Min(a, b), Math.Max(a, b))); }
        }
        foreach (var segment in RebarPlanning.MergeIntervals(intervals, RebarHostGeometry.Tolerance)) if (segment.Length > c.Doc.Application.ShortCurveTolerance) AddStraight(start + direction * segment.Start, start + direction * segment.End, normal, inward, tag);
    }
    private static IEnumerable<IList<BarPlan>> Compress(IEnumerable<BarPlan> source)
    {
        foreach (var tagged in source.GroupBy(p => p.Tag + ";" + p.Type.Id.IntegerValue + ";" + p.Points.Count))
        {
            var ordered = tagged.OrderBy(p => p.Points[0].DotProduct(p.Normal)).ToList(); var batch = new List<BarPlan>(); double step = 0;
            foreach (var plan in ordered)
            {
                var same = true;
                if (batch.Count > 0)
                {
                    var previous = batch[batch.Count - 1]; var delta = plan.Points[0] - previous.Points[0]; var distance = delta.DotProduct(previous.Normal);
                    same = distance > 1e-7 && (delta - previous.Normal * distance).GetLength() < 1e-7 && plan.Normal.DotProduct(previous.Normal) > .999999;
                    if (same) same = plan.Points.Zip(previous.Points, (a, b) => (a - b - delta).GetLength()).All(error => error < 1e-7);
                    if (same && batch.Count > 1) same = Math.Abs(distance - step) < 1e-7; if (same) step = distance;
                }
                if (!same || batch.Count == 1002) { yield return batch; batch = new List<BarPlan>(); step = 0; }
                batch.Add(plan);
            }
            if (batch.Count > 0) yield return batch;
        }
    }
    private Rebar Create(IList<BarPlan> group)
    {
        var p = group[0]; var errors = new List<string>();
        var orientations = p.Hook ? new[] { (RebarHookOrientation.Left, RebarHookOrientation.Left), (RebarHookOrientation.Left, RebarHookOrientation.Right), (RebarHookOrientation.Right, RebarHookOrientation.Left), (RebarHookOrientation.Right, RebarHookOrientation.Right) } : new[] { (RebarHookOrientation.Left, RebarHookOrientation.Right) };
        foreach (var orientation in orientations)
        {
            using var sub = new SubTransaction(c.Doc); sub.Start();
            try
            {
                var bar = Rebar.CreateFromCurves(c.Doc, p.Style, p.Type, p.Hook ? hook : null, p.Hook ? hook : null, f.Host, p.Normal, p.Curves(), orientation.Item1, orientation.Item2, true, true);
                if (bar == null) throw new RebarIssue("BAR_TYPE", "CreateFromCurves không tìm/tạo được Shape phù hợp; kiểm tra RebarShape và tham số uốn.");
                if (group.Count > 1) bar.GetShapeDrivenAccessor().SetLayoutAsFixedNumber(group.Count, group[0].Points[0].DistanceTo(group[group.Count - 1].Points[0]), true, true, true);
                c.Doc.Regenerate(); var check = new List<BarSegment>(); ReadSegments(bar, p.Tag, check, true);
                var current = new HashSet<string>(check.Select(segment => segment.Bar));
                var clash = RebarClashes.FindCollision(acceptedSegments.Concat(check), Engineering.Feet(.2), Engineering.Feet(200), (a, b) => current.Contains(a) || current.Contains(b));
                if (clash.HasValue) throw new RebarIssue("COVER", "Hướng móc hoặc thanh chồng với thanh đã rải: " + clash.Value.First + " / " + clash.Value.Second);
                if (sub.Commit() != TransactionStatus.Committed) throw new RebarIssue("REBAR_API", "Revit hoàn tác giao dịch con tạo thanh.");
                acceptedSegments.AddRange(check);
                log("Tạo " + p.Tag + "; ID=" + bar.Id.IntegerValue + "; positions=" + bar.NumberOfBarPositions + "; hooks=" + orientation); return bar;
            }
            catch (Exception ex)
            {
                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack(); errors.Add(ex.Message);
                if (!p.Hook) throw new RebarIssue((ex as RebarIssue)?.Kind ?? "REBAR_API", p.Tag + ": " + ex.Message, ex);
                log("Móc " + orientation + " không hợp lệ: " + ex);
            }
        }
        throw new RebarIssue("COVER", p.Tag + ": không có hướng móc nào thỏa hình học/cover. " + string.Join(" | ", errors.Distinct()));
    }
    private void ReadSegments(Rebar bar, string tag, IList<BarSegment> segments, bool checkCover, bool count = false)
    {
        var radius = RebarTemplates.Diameter((RebarBarType)c.Doc.GetElement(bar.GetTypeId())) / 2;
        for (var index = 0; index < bar.NumberOfBarPositions; index++)
        {
            if (!bar.DoesBarExistAtPosition(index)) continue; if (count) positionCount++;
            var curves = bar.GetTransformedCenterlineCurves(true, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, index);
            if (curves.Count == 0) throw new RebarIssue("REBAR_API", "Không đọc được đường tâm sau khi tạo: ID " + bar.Id.IntegerValue + ", vị trí " + index);
            foreach (var curve in curves)
            {
                if (checkCover) f.RequireClearance(curve, cover + radius, tag + " ID " + bar.Id.IntegerValue + " #" + index);
                IList<XYZ> points;
                if (curve is Arc arc) { var step = 2 * Math.Acos(Math.Max(-1, 1 - Engineering.Feet(.05) / arc.Radius)); var n = Math.Max(2, (int)Math.Ceiling(arc.Length / arc.Radius / step)); points = Enumerable.Range(0, n + 1).Select(i => arc.Evaluate((double)i / n, true)).ToList(); }
                else points = curve.Tessellate();
                for (var i = 1; i < points.Count; i++) segments.Add(new BarSegment(RebarHostGeometry.Vec(points[i - 1]), RebarHostGeometry.Vec(points[i]), radius, tag + " ID " + bar.Id.IntegerValue + " #" + index));
            }
        }
    }
}
