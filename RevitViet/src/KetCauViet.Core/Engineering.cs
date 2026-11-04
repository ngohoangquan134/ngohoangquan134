using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KetCauViet.Core;

public static class Engineering
{
    public const double MillimetresPerFoot = 304.8;
    public static double Feet(double mm) => mm / MillimetresPerFoot;
    public static double Metres(double feet) => feet * 0.3048;
    public static double SquareMetres(double squareFeet) => squareFeet * 0.09290304;
    public static double CubicMetres(double cubicFeet) => cubicFeet * 0.028316846592;

    public static double RebarMass(double lengthMetres, double diameterMm, double density)
    {
        if (!Finite(lengthMetres) || lengthMetres < 0 || !Finite(diameterMm) || diameterMm <= 0 || !Finite(density) || density <= 0)
            throw new ArgumentOutOfRangeException("Thông số khối lượng thép không hợp lệ.");
        return lengthMetres * Math.PI * diameterMm * diameterMm / 4e6 * density;
    }

    public static double WaterproofArea(double netArea, int layers, double extraPercent)
    {
        if (!Finite(netArea) || netArea < 0 || layers < 1 || !Finite(extraPercent) || extraPercent < 0)
            throw new ArgumentOutOfRangeException("Thông số chống thấm không hợp lệ.");
        return netArea * layers * (1 + extraPercent / 100);
    }

    public static IReadOnlyList<double> EvenPositions(double minimum, double maximum, int count)
    {
        if (!Finite(minimum) || !Finite(maximum) || maximum <= minimum || count < 2)
            throw new ArgumentException("Khoảng đặt thép phải dương và có ít nhất hai thanh.");
        return Enumerable.Range(0, count).Select(i => minimum + (maximum - minimum) * i / (count - 1)).ToArray();
    }

    public static int SpacingCount(double length, double maxSpacing)
    {
        if (!Finite(length) || !Finite(maxSpacing) || length <= 0 || maxSpacing <= 0)
            throw new ArgumentException("Chiều dài và khoảng cách phải lớn hơn 0.");
        return checked((int)Math.Ceiling(length / maxSpacing) + 1);
    }

    public static bool Finite(double number) => !double.IsNaN(number) && !double.IsInfinity(number);

    public static double ParseNumber(string text)
    {
        // Accept a decimal comma or point; thousands separators are intentionally excluded.
        var normalized = text.Trim().Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !Finite(number))
            throw new ArgumentException("Nhập số dùng dấu phẩy hoặc dấu chấm thập phân, không dùng dấu tách hàng nghìn.");
        return number;
    }

    public static string Csv(IEnumerable<IEnumerable<string>> rows)
    {
        var output = new StringBuilder();
        foreach (var row in rows) output.AppendLine(string.Join(";", row.Select(CsvCell)));
        return output.ToString();
    }

    private static string CsvCell(string value)
    {
        value = value ?? "";
        // Prevent element names/marks from becoming spreadsheet formulas. Signed numbers remain numeric.
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && ("=+@-".IndexOf(trimmed[0]) >= 0 || value[0] == '\t' || value[0] == '\r') &&
            !Regex.IsMatch(trimmed, @"^[+-]?\d+(?:[.,]\d+)?(?:[eE][+-]?\d+)?$")) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

public sealed class NumberSequence
{
    private readonly HashSet<string> used;
    private readonly string prefix;
    private readonly int digits;
    private int next;

    public NumberSequence(string prefix, int start, int digits, IEnumerable<string> used)
    {
        if (start < 0 || digits < 1 || digits > 9) throw new ArgumentException("Số bắt đầu >= 0, số chữ số từ 1 đến 9.");
        this.prefix = prefix;
        this.digits = digits;
        next = start;
        this.used = new HashSet<string>(used, StringComparer.OrdinalIgnoreCase);
    }

    public string Next()
    {
        while (true)
        {
            var candidate = prefix + next.ToString("D" + digits, CultureInfo.InvariantCulture);
            next = checked(next + 1);
            if (used.Add(candidate)) return candidate;
        }
    }
}
