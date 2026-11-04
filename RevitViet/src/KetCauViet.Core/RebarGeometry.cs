namespace KetCauViet.Core;

public readonly struct Vector3
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public Vector3(double x, double y, double z) { X = x; Y = y; Z = z; }
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3 operator *(Vector3 a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    public double Dot(Vector3 other) => X * other.X + Y * other.Y + Z * other.Z;
    public Vector3 Cross(Vector3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(Dot(this));
    public Vector3 Unit() => Length > 1e-12 && Engineering.Finite(Length) ? this * (1 / Length) : throw new ArgumentException("Trục cục bộ bị suy biến.");
}

public sealed class MemberFrame
{
    public Vector3 Origin { get; }
    public Vector3 U { get; }
    public Vector3 V { get; }
    public Vector3 Axis { get; }
    public MemberFrame(Vector3 origin, Vector3 axis, Vector3 sectionUp)
    {
        Origin = origin; Axis = axis.Unit();
        V = (sectionUp - Axis * sectionUp.Dot(Axis)).Unit();
        U = V.Cross(Axis).Unit();
        V = Axis.Cross(U).Unit();
    }
    public Vector3 ToLocal(Vector3 world)
    { var p = world - Origin; return new(p.Dot(U), p.Dot(V), p.Dot(Axis)); }
    public Vector3 ToWorld(Vector3 local) => Origin + U * local.X + V * local.Y + Axis * local.Z;
}

public sealed class RectangleSection
{
    public double MinU { get; }
    public double MaxU { get; }
    public double MinV { get; }
    public double MaxV { get; }
    private RectangleSection(double minU, double maxU, double minV, double maxV)
    { MinU = minU; MaxU = maxU; MinV = minV; MaxV = maxV; }

    // Input is an ordered contour from a perpendicular section cut, NOT a bounding box.
    public static RectangleSection Recognize(IEnumerable<Vector3> contour, int loopCount, double tolerance)
    {
        if (loopCount != 1) throw new ArgumentException("Tiết diện có lỗ hoặc nhiều miền rời.");
        var points = contour.ToList();
        if (points.Count > 1 && (points[0] - points[points.Count - 1]).Length <= tolerance) points.RemoveAt(points.Count - 1);
        for (var i = points.Count - 1; i >= 0 && points.Count > 2; i--)
            if ((points[i] - points[(i + points.Count - 1) % points.Count]).Length <= tolerance) points.RemoveAt(i);
        var changed = true;
        while (changed && points.Count > 4)
        {
            changed = false;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i] - points[(i + points.Count - 1) % points.Count];
                var b = points[(i + 1) % points.Count] - points[i];
                if (a.Cross(b).Length <= tolerance * Math.Max(a.Length, b.Length) && a.Dot(b) >= 0)
                { points.RemoveAt(i); changed = true; break; }
            }
        }
        if (points.Count != 4) throw new ArgumentException("Tiết diện không phải hình chữ nhật bốn cạnh.");
        var minU = points.Min(p => p.X); var maxU = points.Max(p => p.X);
        var minV = points.Min(p => p.Y); var maxV = points.Max(p => p.Y);
        if (maxU - minU <= tolerance || maxV - minV <= tolerance) throw new ArgumentException("Tiết diện suy biến.");
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i]; var d = points[(i + 1) % points.Count] - p;
            if (Math.Abs(d.X) > tolerance && Math.Abs(d.Y) > tolerance)
                throw new ArgumentException("Cạnh tiết diện không song song các trục cục bộ đã xác định.");
            if (Math.Min(Math.Abs(p.X - minU), Math.Abs(p.X - maxU)) > tolerance || Math.Min(Math.Abs(p.Y - minV), Math.Abs(p.Y - maxV)) > tolerance)
                throw new ArgumentException("Biên tiết diện có khuyết hoặc không vuông góc.");
        }
        return new RectangleSection(minU, maxU, minV, maxV);
    }

    public void RequireSame(RectangleSection other, double tolerance)
    {
        if (Math.Abs(MinU - other.MinU) > tolerance || Math.Abs(MaxU - other.MaxU) > tolerance || Math.Abs(MinV - other.MinV) > tolerance || Math.Abs(MaxV - other.MaxV) > tolerance)
            throw new ArgumentException("Tiết diện danh nghĩa thay đổi dọc trục; chưa hỗ trợ cấu kiện vát hoặc thay đổi tiết diện.");
    }
}

public readonly struct LinearInterval
{
    public double Start { get; }
    public double End { get; }
    public double Length => End - Start;
    public LinearInterval(double start, double end) { Start = start; End = end; }
}

public static class RebarPlanning
{
    public static IReadOnlyList<LinearInterval> MergeIntervals(IEnumerable<LinearInterval> input, double tolerance)
    {
        var result = new List<LinearInterval>();
        foreach (var item in input.Where(x => x.Length > tolerance).OrderBy(x => x.Start))
        {
            if (result.Count > 0 && item.Start <= result[result.Count - 1].End + tolerance)
            { var last = result[result.Count - 1]; result[result.Count - 1] = new(last.Start, Math.Max(last.End, item.End)); }
            else result.Add(item);
        }
        return result;
    }

    public static IReadOnlyList<double> Spaced(double start, double end, double maximumSpacing)
    {
        if (end < start || maximumSpacing <= 0) throw new ArgumentException("Khoảng đặt và bước thép không hợp lệ.");
        if (end - start < 1e-10) return new[] { start };
        return Engineering.EvenPositions(start, end, Engineering.SpacingCount(end - start, maximumSpacing));
    }

    public static IReadOnlyList<double> Zones(double start, double end, double leftLength, double rightLength, double leftStep, double middleStep, double rightStep)
    {
        if (leftLength < 0 || rightLength < 0 || leftLength + rightLength > end - start + 1e-10)
            throw new ArgumentException("Tổng chiều dài hai vùng đai đầu vượt chiều dài vùng bố trí hợp lệ.");
        if (leftStep <= 0 || middleStep <= 0 || rightStep <= 0) throw new ArgumentException("Bước đai phải dương.");
        var values = new List<double>();
        void Add(double a, double b, double step) { if (b > a) values.AddRange(Spaced(a, b, step)); }
        Add(start, start + leftLength, leftStep);
        Add(start + leftLength, end - rightLength, middleStep);
        Add(end - rightLength, end, rightStep);
        return values.OrderBy(x => x).Aggregate(new List<double>(), (list, x) => { if (list.Count == 0 || x - list[list.Count - 1] > 1e-9) list.Add(x); return list; });
    }

    public static IReadOnlyList<LinearInterval> Splice(double start, double end, double stockLength, double lapLength)
    {
        if (end <= start || stockLength < 0 || lapLength < 0 || (lapLength > 0 && (stockLength <= lapLength || stockLength == 0)))
            throw new ArgumentException("Nối chồng cần chiều dài phân đoạn lớn hơn chiều dài nối.");
        if (stockLength == 0 || end - start <= stockLength) return new[] { new LinearInterval(start, end) };
        if (lapLength == 0) throw new ArgumentException("Phân đoạn thanh dài cần chiều dài nối chồng dương.");
        var result = new List<LinearInterval>();
        for (var a = start; a < end - 1e-9;)
        {
            var b = Math.Min(end, a + stockLength); result.Add(new(a, b));
            if (b == end) break;
            a = b - lapLength;
            if (result.Count > 10000) throw new ArgumentException("Số phân đoạn quá lớn; kiểm tra chiều dài nối và phân đoạn.");
        }
        return result;
    }

    // Exact clipping for a convex planar concrete region: n.dot(p) <= offset.
    // Used as a fast path by the Revit adapter; joined/non-convex bodies use solid booleans.
    public static LinearInterval? ClipConvex(Vector3 origin, Vector3 direction, LinearInterval domain,
        IEnumerable<(Vector3 Normal, double Offset)> planes, double clearance)
    {
        var a = domain.Start; var b = domain.End;
        foreach (var plane in planes)
        {
            var n = plane.Normal.Unit(); var denominator = n.Dot(direction);
            var remaining = plane.Offset / plane.Normal.Length - clearance - n.Dot(origin);
            if (Math.Abs(denominator) < 1e-12) { if (remaining < -1e-10) return null; continue; }
            var value = remaining / denominator;
            if (denominator > 0) b = Math.Min(b, value); else a = Math.Max(a, value);
            if (b - a <= 1e-10) return null;
        }
        return new LinearInterval(a, b);
    }
}
