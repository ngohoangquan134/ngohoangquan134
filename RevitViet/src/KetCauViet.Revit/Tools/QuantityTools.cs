using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using KetCauViet.Core;
using KetCauViet.Revit.Infrastructure;
using KetCauViet.Revit.UI;

namespace KetCauViet.Revit.Tools;

internal static class QuantityTools
{
    public static void ConcreteVolume(ToolContext c) => Concrete(c, false);
    public static void CoreWallVolume(ToolContext c) => Concrete(c, true);
    private static void Concrete(ToolContext c, bool core)
    {
        var elements = c.Select(core ? "Chọn các tường thuộc vách lõi." : "Chọn cột và tường bê tông.", e => ToolContext.Category(e, core ? new[] { BuiltInCategory.OST_Walls } : new[] { BuiltInCategory.OST_Walls, BuiltInCategory.OST_StructuralColumns }));
        var rows = new List<IEnumerable<string>> { new[] { "ID", "Mark", "Danh mục", "Kiểu", "Thể tích (m³)" } };
        var total = 0.0;
        foreach (var e in elements)
        {
            var volume = Engineering.CubicMetres(GeometryTools.Volume(e)); total += volume;
            rows.Add(new[] { ToolContext.Id(e), ToolContext.Mark(e), e.Category.Name, e.Name, ToolContext.N(volume) });
        }
        rows.Add(new[] { "TỔNG", "", "", "", ToolContext.N(total) });
        c.Report(core ? "Bê tông vách lõi" : "Bê tông cột/tường", $"{elements.Count} cấu kiện: {ToolContext.N(total)} m³.\nThể tích theo mô hình, gồm mọi lớp vật liệu của đối tượng đã chọn; hãy chọn đúng cấu kiện bê tông. Lệnh vách lõi không tự phân loại vách hoặc bóc riêng lớp lõi của tường nhiều lớp.", rows);
    }

    public static void FormworkArea(ToolContext c)
    {
        var elements = c.Select("Chọn cấu kiện cần đo diện tích ván khuôn.");
        using var input = InputDialog.Ask(c.App, "Diện tích ván khuôn", "Đọc mặt của solid theo hình học hiện tại. Chưa trừ tiếp xúc của các khối chưa Join; không tự phân loại vật liệu.", new Field("mode", "Các mặt được tính", options: new[] { "Mặt bên và đáy (bỏ mặt hướng lên)", "Chỉ mặt đứng", "Toàn bộ mặt" }));
        var rows = new List<IEnumerable<string>> { new[] { "ID", "Mark", "Kiểu", "Diện tích (m²)" } };
        var total = 0.0; var errors = new List<string>();
        foreach (var element in elements)
        {
            try
            {
                var area = 0.0;
                foreach (var solid in GeometryTools.Solids(element))
                    foreach (Face face in solid.Faces)
                    {
                        // Surface normal classification is exact for planar faces. Reject curved
                        // surfaces for directional modes rather than silently using a sampled normal.
                        if (input.Index("mode") == 2) { area += face.Area; continue; }
                        if (!(face is PlanarFace planar)) throw new ArgumentException("Có mặt cong; dùng Đo mặt hoặc chế độ Toàn bộ mặt.");
                        if (input.Index("mode") == 0 ? planar.FaceNormal.Z < 1e-6 : Math.Abs(planar.FaceNormal.Z) < 1e-6) area += face.Area;
                    }
                area = Engineering.SquareMetres(area); total += area;
                rows.Add(new[] { ToolContext.Id(element), ToolContext.Mark(element), element.Name, ToolContext.N(area) });
            }
            catch (Exception ex) { errors.Add(ToolContext.Id(element) + ": " + ex.Message); rows.Add(new[] { ToolContext.Id(element), ToolContext.Mark(element), "LỖI: " + ex.Message, "" }); }
        }
        rows.Add(new[] { "TỔNG", "", "", ToolContext.N(total) });
        c.Report("Diện tích ván khuôn", $"Diện tích các đối tượng xử lý được: {ToolContext.N(total)} m².\nĐối tượng lỗi/bỏ qua: {errors.Count}.\n{string.Join("\n", errors.Take(5))}", rows);
    }

    public static void PickFace(ToolContext c)
    {
        var faces = GeometryTools.PickFaces(c);
        var rows = new List<IEnumerable<string>> { new[] { "ID đối tượng", "Mặt", "Diện tích (m²)", "Chu vi gồm lỗ (m)" } };
        foreach (var pair in faces)
        {
            var perimeter = pair.Face.GetEdgesAsCurveLoops().Sum(loop => loop.Sum(curve => curve.Length));
            rows.Add(new[] { pair.Reference.ElementId.IntegerValue.ToString(), pair.Reference.ConvertToStableRepresentation(c.Doc), ToolContext.N(Engineering.SquareMetres(pair.Face.Area)), ToolContext.N(Engineering.Metres(perimeter)) });
        }
        c.Report("Đo mặt", $"{faces.Count} mặt. Tổng diện tích: {ToolContext.N(Engineering.SquareMetres(faces.Sum(x => x.Face.Area)))} m².\nDiện tích dùng Face.Area nên phản ánh lỗ trên mặt.", rows);
    }

    public static void Waterproofing(ToolContext c)
    {
        var faces = GeometryTools.PickFaces(c);
        using var input = InputDialog.Ask(c.App, "Chống thấm", "Diện tích mặt × số lớp × (1 + tỷ lệ bù/100). Chọn đúng mặt cần chống thấm.", new Field("layers", "Số lớp", "1"), new Field("extra", "Bù chồng mí/hao hụt (%)", "5"));
        var layers = input.Integer("layers", 1, 100); var extra = input.Number("extra", 0, 1000);
        var rows = new List<IEnumerable<string>> { new[] { "ID đối tượng", "Diện tích thực (m²)", "Số lớp", "Bù (%)", "Diện tích quy đổi (m²)" } };
        var net = 0.0; var gross = 0.0;
        foreach (var pair in faces)
        {
            var a = Engineering.SquareMetres(pair.Face.Area); var g = Engineering.WaterproofArea(a, layers, extra); net += a; gross += g;
            rows.Add(new[] { pair.Reference.ElementId.IntegerValue.ToString(), ToolContext.N(a), layers.ToString(), ToolContext.N(extra), ToolContext.N(g) });
        }
        rows.Add(new[] { "TỔNG", ToolContext.N(net), layers.ToString(), ToolContext.N(extra), ToolContext.N(gross) });
        c.Report("Chống thấm", $"Diện tích mặt: {ToolContext.N(net)} m².\nDiện tích theo {layers} lớp, bù {ToolContext.N(extra)}%: {ToolContext.N(gross)} m².", rows);
    }

    public static void RebarWeight(ToolContext c)
    {
        var rebars = c.Select("Chọn các thanh/bộ thép cần tính khối lượng.", e => e is Rebar);
        using var input = InputDialog.Ask(c.App, "Khối lượng thép", "Dùng tổng chiều dài cả bộ thép và đường kính danh nghĩa. Không cộng thêm hao hụt tự động.", new Field("density", "Khối lượng riêng (kg/m³)", "7850"));
        var density = input.Number("density", 1, 30000);
        var rows = new List<IEnumerable<string>> { new[] { "ID", "Mark", "Đường kính (mm)", "Tổng chiều dài (m)", "Số thanh", "Khối lượng (kg)" } };
        var total = 0.0;
        foreach (Rebar rebar in rebars)
        {
            var type = c.Doc.GetElement(rebar.GetTypeId());
            var diameter = type.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble() * 304.8;
            var length = Engineering.Metres(rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_TOTAL_LENGTH).AsDouble());
            var mass = Engineering.RebarMass(length, diameter, density); total += mass;
            rows.Add(new[] { ToolContext.Id(rebar), ToolContext.Mark(rebar), ToolContext.N(diameter), ToolContext.N(length), rebar.Quantity.ToString(), ToolContext.N(mass) });
        }
        rows.Add(new[] { "TỔNG", "", "", "", "", ToolContext.N(total) });
        c.Report("Khối lượng thép", $"{rebars.Count} đối tượng/bộ thép: {ToolContext.N(total)} kg.\nKhối lượng riêng: {ToolContext.N(density)} kg/m³.", rows);
    }

    public static void SumParameter(ToolContext c)
    {
        var elements = c.Select("Chọn các đối tượng cần cộng tham số.");
        var names = elements.SelectMany(e => e.Parameters.Cast<Parameter>()).Where(p => p.StorageType == StorageType.Double || p.StorageType == StorageType.Integer).Select(p => p.Definition.Name).Distinct().OrderBy(s => s).ToArray();
        using var input = InputDialog.Ask(c.App, "Cộng tham số", "Chỉ cộng tham số số ở cấp instance và cùng loại đại lượng. Nếu trùng tên nhiều tham số thì lệnh yêu cầu xử lý tên trước.", new Field("parameter", "Tên tham số", options: names));
        var name = names[input.Index("parameter")];
        var total = 0.0; ForgeTypeId? spec = null; var count = 0; var missing = 0;
        var rows = new List<IEnumerable<string>> { new[] { "ID", "Mark", "Tham số", "Giá trị theo đơn vị dự án" } };
        foreach (var e in elements)
        {
            var matches = e.GetParameters(name);
            if (matches.Count == 0) { missing++; rows.Add(new[] { ToolContext.Id(e), ToolContext.Mark(e), name, "KHÔNG CÓ THAM SỐ" }); continue; }
            if (matches.Count > 1) throw new ArgumentException("Có nhiều tham số tên " + name + " trên ID " + ToolContext.Id(e) + ". Không thể xác định duy nhất.");
            var p = matches[0];
            if (p.StorageType != StorageType.Double && p.StorageType != StorageType.Integer) throw new ArgumentException("Có tham số cùng tên nhưng không phải kiểu số.");
            var currentSpec = p.Definition.GetDataType();
            if (spec != null && spec != currentSpec) throw new ArgumentException("Các tham số không cùng loại đại lượng; không thể cộng.");
            if (currentSpec == SpecTypeId.Boolean.YesNo) throw new ArgumentException("Tham số Có/Không không phải đại lượng để cộng.");
            spec = currentSpec; total += p.StorageType == StorageType.Double ? p.AsDouble() : p.AsInteger(); count++;
            rows.Add(new[] { ToolContext.Id(e), ToolContext.Mark(e), name, p.AsValueString() ?? ToolContext.N(p.StorageType == StorageType.Double ? p.AsDouble() : p.AsInteger()) });
        }
        var formatted = ToolContext.N(total);
        if (spec != null && UnitUtils.IsMeasurableSpec(spec)) formatted = UnitFormatUtils.Format(c.Doc.GetUnits(), spec, total, false);
        rows.Add(new[] { "TỔNG", "", name, formatted });
        c.Report("Cộng tham số", $"{name}: {formatted}.\nCó giá trị: {count}; không có tham số: {missing}.", rows);
    }
}
