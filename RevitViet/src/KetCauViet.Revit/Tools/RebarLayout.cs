using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using KetCauViet.Core;
using KetCauViet.Revit.Infrastructure;

namespace KetCauViet.Revit.Tools;

internal sealed class HostFrame
{
    public Element Host { get; }
    public XYZ X { get; }
    public XYZ Y { get; }
    public XYZ Z { get; }
    public XYZ Min { get; }
    public XYZ Max { get; }
    private readonly IList<Solid> solids;
    public HostFrame(Element host, XYZ x, XYZ y, XYZ z)
    {
        Host = host; X = x.Normalize(); Y = y.Normalize(); Z = z.Normalize();
        if (RebarHostData.GetRebarHostData(host)?.IsValidHost() != true) throw new ArgumentException("Cấu kiện không phải host cốt thép hợp lệ. Kiểm tra Structural Material/Concrete.");
        if ((host is Wall wall && wall.WallType.GetCompoundStructure()?.LayerCount != 1) ||
            (host is Floor floor && floor.FloorType.GetCompoundStructure()?.LayerCount != 1))
            throw new ArgumentException("Chỉ bố trí thép cho sàn/vách một lớp bê tông. Không suy đoán lớp bảo vệ từ lớp hoàn thiện của cấu kiện nhiều lớp.");
        solids = GeometryTools.Solids(host).ToList();
        var local = GeometryTools.Vertices(solids).Select(p => new XYZ(p.DotProduct(X), p.DotProduct(Y), p.DotProduct(Z))).ToList();
        if (local.Count == 0) throw new ArgumentException("Không có hình học solid.");
        Min = new XYZ(local.Min(p => p.X), local.Min(p => p.Y), local.Min(p => p.Z));
        Max = new XYZ(local.Max(p => p.X), local.Max(p => p.Y), local.Max(p => p.Z));
        var boundingVolume = (Max.X - Min.X) * (Max.Y - Min.Y) * (Max.Z - Min.Z);
        if (boundingVolume <= 1e-8 || Math.Abs(solids.Sum(s => s.Volume) / boundingVolume - 1) > 0.001)
            throw new ArgumentException("Chỉ nhận khối hộp chữ nhật không lỗ/cắt/vát. Cấu kiện có hình học khác cần bố trí riêng.");
    }

    public XYZ Point(double x, double y, double z) => X * x + Y * y + Z * z;

    public void Check(IEnumerable<Curve> curves)
    {
        foreach (var curve in curves)
        {
            var inside = 0.0;
            foreach (var solid in solids)
            {
                using var options = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside };
                using var intersection = solid.IntersectWithCurve(curve, options);
                for (var i = 0; i < intersection.SegmentCount; i++) inside += intersection.GetCurveSegment(i).Length;
            }
            if (inside < curve.Length - Engineering.Feet(0.5)) throw new ArgumentException("Đường tâm thép nằm ngoài host. Đã hủy toàn bộ thép của cấu kiện này.");
        }
    }

    public static HostFrame Beam(Element host)
    {
        if (!(host.Location is LocationCurve location) || !(location.Curve is Line line) || !(host is FamilyInstance instance)) throw new ArgumentException("Chỉ nhận dầm có trục thẳng.");
        var x = line.Direction.Normalize();
        var y = instance.GetTransform().BasisY; y = (y - x * y.DotProduct(x)).Normalize();
        var z = x.CrossProduct(y).Normalize();
        if (z.Z < 0) { y = -y; z = -z; }
        // The upper/lower rows follow the family's section orientation, including beam roll.
        return new HostFrame(host, x, y, z);
    }

    public static HostFrame Column(Element host)
    {
        if (!(host is FamilyInstance instance)) throw new ArgumentException("Chỉ nhận cột family.");
        var t = instance.GetTransform();
        if (Math.Abs(t.BasisZ.DotProduct(XYZ.BasisZ)) < 0.99999) throw new ArgumentException("Chỉ hỗ trợ cột thẳng đứng.");
        return new HostFrame(host, t.BasisX, t.BasisY, t.BasisZ);
    }

    public static HostFrame Wall(Element host)
    {
        if (!(host is Wall wall) || !(wall.Location is LocationCurve location) || !(location.Curve is Line line)) throw new ArgumentException("Chỉ hỗ trợ vách thẳng, đứng, tiết diện chữ nhật.");
        var x = line.Direction;
        if (Math.Abs(x.Z) > 1e-6) throw new ArgumentException("Trục vách phải nằm ngang.");
        return new HostFrame(host, x, XYZ.BasisZ.CrossProduct(x), XYZ.BasisZ);
    }

    public static HostFrame Floor(Element host)
    {
        var faces = HostObjectUtils.GetTopFaces((Floor)host).Select(r => host.GetGeometryObjectFromReference(r)).OfType<PlanarFace>().ToList();
        if (faces.Count != 1 || faces[0].FaceNormal.Z < 0.99999) throw new ArgumentException("Chỉ hỗ trợ sàn phẳng chữ nhật, không dốc.");
        var edge = faces[0].GetEdgesAsCurveLoops().SelectMany(loop => loop).OfType<Line>().OrderByDescending(l => l.Length).FirstOrDefault() ?? throw new ArgumentException("Không xác định được cạnh sàn.");
        var x = edge.Direction;
        return new HostFrame(host, x, XYZ.BasisZ.CrossProduct(x), XYZ.BasisZ);
    }
}

internal sealed class RebarLayout
{
    private readonly ToolContext context;
    private readonly RebarBarType main;
    private readonly RebarBarType tie;
    private readonly double cover;
    private readonly double spacing;
    private double MainDiameter => main.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble();
    private double TieDiameter => tie.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble();
    public RebarLayout(ToolContext context, RebarBarType main, RebarBarType tie, double cover, double spacing)
    { this.context = context; this.main = main; this.tie = tie; this.cover = cover; this.spacing = spacing; }

    private Rebar Bar(HostFrame frame, RebarBarType type, RebarStyle style, XYZ normal, IList<Curve> curves)
    {
        frame.Check(curves);
        return Rebar.CreateFromCurves(context.Doc, style, type, null, null, frame.Host, normal, curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true)
            ?? throw new InvalidOperationException("Revit không tạo được shape cốt thép.");
    }

    private void Straight(HostFrame frame, XYZ start, XYZ end, XYZ normal, double distribution = 0)
    {
        if (start.DistanceTo(end) < context.Doc.Application.ShortCurveTolerance) throw new ArgumentException("Khoảng đặt thép quá ngắn.");
        var curves = new List<Curve> { Line.CreateBound(start, end) };
        if (distribution > 0) frame.Check(curves.Select(c => c.CreateTransformed(Transform.CreateTranslation(normal * distribution))));
        var bar = Bar(frame, main, RebarStyle.Standard, normal, curves);
        if (distribution > 0)
        {
            var count = Engineering.SpacingCount(distribution, spacing);
            if (count > 1002) throw new ArgumentException("Số thanh trong một bộ vượt giới hạn Revit. Tăng khoảng cách hoặc chia vùng đặt thép.");
            bar.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(spacing, distribution, true, true, true);
        }
    }

    private void Ties(HostFrame frame, XYZ[] points, XYZ normal, double distribution)
    {
        if (distribution <= context.Doc.Application.ShortCurveTolerance) throw new ArgumentException("Chiều dài vùng đai không đủ.");
        if (Engineering.SpacingCount(distribution, spacing) > 1002) throw new ArgumentException("Vượt giới hạn số đai trong một bộ Revit.");
        var curves = Enumerable.Range(0, 4).Select(i => (Curve)Line.CreateBound(points[i], points[(i + 1) % 4])).ToList();
        frame.Check(curves.Select(c => c.CreateTransformed(Transform.CreateTranslation(normal * distribution))));
        var rebar = Bar(frame, tie, RebarStyle.StirrupTie, normal, curves);
        rebar.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(spacing, distribution, true, true, true);
    }

    private static void Range(double lower, double upper)
    { if (upper <= lower) throw new ArgumentException("Tiết diện không đủ cho lớp bảo vệ và đường kính thép đã nhập."); }

    public void Beam(Element host, int bottom, int top)
    {
        var f = HostFrame.Beam(host); var d = cover + TieDiameter + MainDiameter / 2;
        var x0 = f.Min.X + cover + MainDiameter / 2; var x1 = f.Max.X - cover - MainDiameter / 2;
        var y0 = f.Min.Y + d; var y1 = f.Max.Y - d; var z0 = f.Min.Z + d; var z1 = f.Max.Z - d;
        Range(x0, x1); Range(y0, y1); Range(z0, z1);
        if (z1 - z0 < MainDiameter) throw new ArgumentException("Thép trên và dưới bị chồng lấn; tiết diện quá thấp.");
        if ((y1 - y0) / (Math.Max(bottom, top) - 1) < MainDiameter) throw new ArgumentException("Thép dọc bị chồng lấn; giảm số thanh hoặc đường kính.");
        foreach (var y in Engineering.EvenPositions(y0, y1, bottom)) Straight(f, f.Point(x0, y, z0), f.Point(x1, y, z0), f.Z);
        foreach (var y in Engineering.EvenPositions(y0, y1, top)) Straight(f, f.Point(x0, y, z1), f.Point(x1, y, z1), f.Z);
        var t = cover + TieDiameter / 2;
        var tx = f.Min.X + t; var ty0 = f.Min.Y + t; var ty1 = f.Max.Y - t; var tz0 = f.Min.Z + t; var tz1 = f.Max.Z - t;
        Ties(f, new[] { f.Point(tx, ty0, tz0), f.Point(tx, ty1, tz0), f.Point(tx, ty1, tz1), f.Point(tx, ty0, tz1) }, f.X, f.Max.X - f.Min.X - 2 * t);
    }

    public void Column(Element host, int nx, int ny)
    {
        var f = HostFrame.Column(host); var d = cover + TieDiameter + MainDiameter / 2;
        var x0 = f.Min.X + d; var x1 = f.Max.X - d; var y0 = f.Min.Y + d; var y1 = f.Max.Y - d;
        var z0 = f.Min.Z + cover + MainDiameter / 2; var z1 = f.Max.Z - cover - MainDiameter / 2;
        Range(x0, x1); Range(y0, y1); Range(z0, z1);
        if ((x1 - x0) / (nx - 1) < MainDiameter || (y1 - y0) / (ny - 1) < MainDiameter) throw new ArgumentException("Thép dọc bị chồng lấn.");
        foreach (var x in Engineering.EvenPositions(x0, x1, nx))
            foreach (var y in new[] { y0, y1 }) Straight(f, f.Point(x, y, z0), f.Point(x, y, z1), f.X);
        foreach (var y in Engineering.EvenPositions(y0, y1, ny).Skip(1).Take(ny - 2))
            foreach (var x in new[] { x0, x1 }) Straight(f, f.Point(x, y, z0), f.Point(x, y, z1), f.X);
        var t = cover + TieDiameter / 2; var z = f.Min.Z + t;
        Ties(f, new[] { f.Point(f.Min.X + t, f.Min.Y + t, z), f.Point(f.Max.X - t, f.Min.Y + t, z), f.Point(f.Max.X - t, f.Max.Y - t, z), f.Point(f.Min.X + t, f.Max.Y - t, z) }, f.Z, f.Max.Z - f.Min.Z - 2 * t);
    }

    public void Wall(Element host)
    {
        var f = HostFrame.Wall(host); var d = MainDiameter; var edge = cover + d / 2;
        var x0 = f.Min.X + edge; var x1 = f.Max.X - edge; var z0 = f.Min.Z + edge; var z1 = f.Max.Z - edge;
        Range(x0, x1); Range(z0, z1);
        if (f.Max.Y - f.Min.Y <= 2 * cover + 4 * d) throw new ArgumentException("Vách quá mỏng cho hai lớp thép hai phương.");
        foreach (var side in new[] { false, true })
        {
            var yVertical = side ? f.Max.Y - edge : f.Min.Y + edge;
            var yHorizontal = side ? yVertical - d : yVertical + d;
            Straight(f, f.Point(x0, yVertical, z0), f.Point(x0, yVertical, z1), f.X, x1 - x0);
            Straight(f, f.Point(x0, yHorizontal, z0), f.Point(x1, yHorizontal, z0), f.Z, z1 - z0);
        }
    }

    public void Floor(Element host, int layers)
    {
        var f = HostFrame.Floor(host); var d = MainDiameter; var edge = cover + d / 2;
        var x0 = f.Min.X + edge; var x1 = f.Max.X - edge; var y0 = f.Min.Y + edge; var y1 = f.Max.Y - edge;
        Range(x0, x1); Range(y0, y1);
        var needed = layers == 0 ? 2 * cover + 4 * d : 2 * cover + 2 * d;
        if (f.Max.Z - f.Min.Z <= needed) throw new ArgumentException("Sàn quá mỏng cho số lớp và đường kính đã chọn.");
        foreach (var top in layers == 0 ? new[] { false, true } : new[] { layers == 1 })
        {
            var zX = top ? f.Max.Z - edge : f.Min.Z + edge;
            var zY = top ? zX - d : zX + d;
            Straight(f, f.Point(x0, y0, zX), f.Point(x1, y0, zX), f.Y, y1 - y0);
            Straight(f, f.Point(x0, y0, zY), f.Point(x0, y1, zY), f.X, x1 - x0);
        }
    }
}
