namespace KetCauViet.Core;

public sealed class BarSegment
{
    public Vector3 A { get; }
    public Vector3 B { get; }
    public double Radius { get; }
    public string Bar { get; }
    public BarSegment(Vector3 a, Vector3 b, double radius, string bar) { A = a; B = b; Radius = radius; Bar = bar; }
}

public static class RebarClashes
{
    public static double SegmentDistance(Vector3 p, Vector3 q, Vector3 r, Vector3 s)
    {
        var d1 = q - p; var d2 = s - r; var v = p - r; var a = d1.Dot(d1); var e = d2.Dot(d2); var f = d2.Dot(v);
        double u, t;
        if (a <= 1e-20 && e <= 1e-20) return (p - r).Length;
        if (a <= 1e-20) { u = 0; t = Clamp(f / e); }
        else
        {
            var c = d1.Dot(v);
            if (e <= 1e-20) { t = 0; u = Clamp(-c / a); }
            else
            {
                var b = d1.Dot(d2); var denominator = a * e - b * b;
                u = denominator > 1e-20 ? Clamp((b * f - c * e) / denominator) : 0;
                t = (b * u + f) / e;
                if (t < 0) { t = 0; u = Clamp(-c / a); } else if (t > 1) { t = 1; u = Clamp((b - c) / a); }
            }
        }
        return (p + d1 * u - r - d2 * t).Length;
    }
    private static double Clamp(double v) => Math.Max(0, Math.Min(1, v));

    public static (string First, string Second)? FindCollision(IEnumerable<BarSegment> source, double tolerance, double cellSize, Func<string, string, bool>? compare = null)
    {
        var grid = new Dictionary<(int, int, int), List<BarSegment>>();
        foreach (var original in source)
        {
            var count = Math.Max(1, (int)Math.Ceiling((original.B - original.A).Length / (cellSize / 2)));
            for (var i = 0; i < count; i++)
            {
                var item = new BarSegment(original.A + (original.B - original.A) * ((double)i / count), original.A + (original.B - original.A) * ((double)(i + 1) / count), original.Radius, original.Bar);
                int Index(double value) => (int)Math.Floor(value / cellSize);
                var minX = Index(Math.Min(item.A.X, item.B.X) - item.Radius); var maxX = Index(Math.Max(item.A.X, item.B.X) + item.Radius);
                var minY = Index(Math.Min(item.A.Y, item.B.Y) - item.Radius); var maxY = Index(Math.Max(item.A.Y, item.B.Y) + item.Radius);
                var minZ = Index(Math.Min(item.A.Z, item.B.Z) - item.Radius); var maxZ = Index(Math.Max(item.A.Z, item.B.Z) + item.Radius);
                for (var x = minX; x <= maxX; x++) for (var y = minY; y <= maxY; y++) for (var z = minZ; z <= maxZ; z++)
                        {
                            var key = (x, y, z);
                            if (!grid.TryGetValue(key, out var existing)) grid[key] = existing = new List<BarSegment>();
                            foreach (var other in existing)
                                if (item.Bar != other.Bar && (compare == null || compare(item.Bar, other.Bar)) && SegmentDistance(item.A, item.B, other.A, other.B) < item.Radius + other.Radius - tolerance) return (other.Bar, item.Bar);
                            existing.Add(item);
                        }
            }
        }
        return null;
    }
}
