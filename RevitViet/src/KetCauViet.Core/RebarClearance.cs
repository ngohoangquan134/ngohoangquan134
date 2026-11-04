namespace KetCauViet.Core;

public static class RebarClearance
{
    public static double ArcMaximumDot(Vector3 center, Vector3 x, Vector3 y, double radius, double start, double end, Vector3 normal)
    {
        if (radius <= 0 || end < start) throw new ArgumentException("Cung không hợp lệ.");
        var a = normal.Dot(x); var b = normal.Dot(y);
        double At(double angle) => normal.Dot(center) + radius * (a * Math.Cos(angle) + b * Math.Sin(angle));
        var result = Math.Max(At(start), At(end));
        var peak = Math.Atan2(b, a); peak += 2 * Math.PI * Math.Ceiling((start - peak) / (2 * Math.PI));
        if (peak <= end + 1e-12) result = Math.Max(result, normal.Dot(center) + radius * Math.Sqrt(a * a + b * b));
        return result;
    }

    public static double LongitudinalInset(double tieRadius, double mainRadius, double bendCenterRadius)
    {
        var sum = tieRadius + mainRadius;
        return Math.Max(sum, bendCenterRadius - (bendCenterRadius - sum) / Math.Sqrt(2));
    }

    public static double HookSeam(IEnumerable<double> longitudinalPositions)
    {
        var p = longitudinalPositions.OrderBy(x => x).ToArray();
        if (p.Length < 2) throw new ArgumentException("Cần ít nhất hai vị trí thép để chọn mối mở đai.");
        var best = 0;
        for (var i = 1; i < p.Length - 1; i++) if (p[i + 1] - p[i] > p[best + 1] - p[best]) best = i;
        return (p[best] + p[best + 1]) / 2;
    }
}
