using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI.Selection;
using KetCauViet.Core;
using KetCauViet.Revit.Infrastructure;
using KetCauViet.Revit.UI;

namespace KetCauViet.Revit.Tools;

internal static class StructuralTools
{
    public static void AutoJoin(ToolContext c)
    {
        var selected = c.Select("Chọn các cấu kiện cần nối hình học.");
        if (selected.Count < 2) throw new ArgumentException("Cần ít nhất hai cấu kiện.");
        var ids = selected.Select(e => e.Id).ToList();
        var joined = 0; var existing = 0; var errors = new List<string>();
        c.Write("Nối hình học", () =>
        {
            foreach (var element in selected)
            {
                var box = element.get_BoundingBox(null);
                if (box == null) continue;
                var corners = new[] { box.Transform.OfPoint(box.Min), box.Transform.OfPoint(box.Max) };
                var outline = new Outline(new XYZ(Math.Min(corners[0].X, corners[1].X), Math.Min(corners[0].Y, corners[1].Y), Math.Min(corners[0].Z, corners[1].Z)),
                    new XYZ(Math.Max(corners[0].X, corners[1].X), Math.Max(corners[0].Y, corners[1].Y), Math.Max(corners[0].Z, corners[1].Z)));
                var candidates = new FilteredElementCollector(c.Doc, ids).WherePasses(new BoundingBoxIntersectsFilter(outline)).ToElements();
                foreach (var other in candidates.Where(e => e.Id.IntegerValue > element.Id.IntegerValue))
                {
                    if (JoinGeometryUtils.AreElementsJoined(c.Doc, element, other)) { existing++; continue; }
                    using var sub = new SubTransaction(c.Doc); sub.Start();
                    try { JoinGeometryUtils.JoinGeometry(c.Doc, element, other); sub.Commit(); joined++; }
                    catch (Exception ex) { sub.RollBack(); errors.Add($"{ToolContext.Id(element)} / {ToolContext.Id(other)}: {ex.Message}"); }
                }
            }
        });
        c.Summary("Nối hình học", $"Nối mới: {joined}. Đã nối: {existing}. Cặp không nối được: {errors.Count}.", errors);
    }

    private sealed class CadCurve
    {
        public Curve Curve { get; }
        public string Layer { get; }
        public CadCurve(Curve curve, string layer) { Curve = curve; Layer = layer; }
    }
    private static IEnumerable<CadCurve> CadGeometry(Document doc, GeometryElement geometry, Transform transform, string inherited = "Không tên")
    {
        foreach (GeometryObject item in geometry)
        {
            var layer = (doc.GetElement(item.GraphicsStyleId) as GraphicsStyle)?.GraphicsStyleCategory?.Name ?? inherited;
            if (item is GeometryInstance instance)
                foreach (var child in CadGeometry(doc, instance.GetSymbolGeometry(), transform.Multiply(instance.Transform), layer)) yield return child;
            else if (item is Line || item is Arc) yield return new CadCurve(((Curve)item).CreateTransformed(transform), layer);
            else if (item is PolyLine polyline)
            {
                var points = polyline.GetCoordinates();
                for (var i = 1; i < points.Count; i++)
                    if (points[i].DistanceTo(points[i - 1]) > doc.Application.ShortCurveTolerance)
                        yield return new CadCurve(Line.CreateBound(transform.OfPoint(points[i - 1]), transform.OfPoint(points[i])), layer);
            }
        }
    }

    public static void CadModel(ToolContext c)
    {
        var picked = c.Select("Chọn một CAD Import/Link.", e => e is ImportInstance);
        if (picked.Count != 1) throw new ArgumentException("Mỗi lần chỉ chọn một CAD Import/Link.");
        var geometry = picked[0].get_Geometry(new Options());
        var curves = CadGeometry(c.Doc, geometry, Transform.Identity).ToList();
        var layers = curves.Select(x => x.Layer).Distinct().OrderBy(x => x).ToArray();
        var levels = new FilteredElementCollector(c.Doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
        using var input = InputDialog.Ask(c.App, "Dựng từ CAD", "Dùng đường tim trên một layer CAD. Chỉ nhận đường/cung nằm ngang; không suy đoán đường biên thành tim.",
            new Field("mode", "Loại cấu kiện", options: new[] { "Dầm", "Tường" }), new Field("layer", "Layer đường tim", options: layers),
            new Field("level", "Cao độ", options: levels.Select(l => l.Name).ToArray()), new Field("offset", "Độ lệch cao độ (mm)", "0"), new Field("height", "Chiều cao tường (mm)", "3000"));
        var level = levels[input.Index("level")]; var offset = Engineering.Feet(input.Number("offset", -100000, 100000));
        var height = Engineering.Feet(input.Number("height", 1));
        var beam = input.Index("mode") == 0;
        Element type = beam ? (Element)c.Choose("Kiểu dầm", new FilteredElementCollector(c.Doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_StructuralFraming).Cast<FamilySymbol>(), ToolContext.TypeName)
            : c.Choose("Kiểu tường", new FilteredElementCollector(c.Doc).OfClass(typeof(WallType)).Cast<WallType>().Where(t => t.Kind == WallKind.Basic), ToolContext.TypeName);
        var selected = curves.Where(x => x.Layer == layers[input.Index("layer")]).ToList();
        var made = 0; var errors = new List<string>(); var keys = new HashSet<string>();
        c.Write("Dựng từ CAD", () =>
        {
            if (type is FamilySymbol symbol && !symbol.IsActive) { symbol.Activate(); c.Doc.Regenerate(); }
            foreach (var item in selected)
            {
                using var sub = new SubTransaction(c.Doc); sub.Start();
                try
                {
                    var start = item.Curve.GetEndPoint(0); var end = item.Curve.GetEndPoint(1);
                    if (Math.Abs(start.Z - end.Z) > Engineering.Feet(0.5) || (item.Curve is Arc arc && Math.Abs(arc.Normal.Z) < 0.9999))
                        throw new ArgumentException("Đường CAD không nằm ngang.");
                    var moved = item.Curve.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, level.Elevation + (beam ? offset : 0) - start.Z)));
                    var key = string.Join("|", new[] { moved.GetEndPoint(0), moved.Evaluate(0.5, true), moved.GetEndPoint(1) }.Select(p => $"{Math.Round(p.X, 5)},{Math.Round(p.Y, 5)},{Math.Round(p.Z, 5)}").OrderBy(x => x));
                    if (!keys.Add(key)) { sub.RollBack(); continue; }
                    if (beam) c.Doc.Create.NewFamilyInstance(moved, (FamilySymbol)type, level, StructuralType.Beam);
                    else Wall.Create(c.Doc, moved, type.Id, level.Id, height, offset, false, true);
                    sub.Commit(); made++;
                }
                catch (Exception ex) { sub.RollBack(); errors.Add(ex.Message); }
            }
        });
        c.Summary("Dựng từ CAD", $"Đã tạo: {made}. Đường lỗi: {errors.Count}. Đường trùng trong CAD được bỏ qua.", errors);
    }

    public static void ChangeFramingType(ToolContext c)
    {
        var beams = c.Select("Chọn dầm cần đổi kiểu.", e => ToolContext.Category(e, BuiltInCategory.OST_StructuralFraming));
        var type = c.Choose("Đổi kiểu dầm", new FilteredElementCollector(c.Doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_StructuralFraming).Cast<FamilySymbol>(), ToolContext.TypeName);
        c.Batch("Đổi kiểu dầm", beams, e => { if (!type.IsActive) type.Activate(); e.ChangeTypeId(type.Id); });
    }

    public static void PileCoordinates(ToolContext c)
    {
        var piles = c.Select("Chọn các cọc/móng có điểm chèn.", e => ToolContext.Category(e, BuiltInCategory.OST_StructuralFoundation) && e.Location is LocationPoint);
        using var input = InputDialog.Ask(c.App, "Tọa độ cọc", "Tọa độ điểm chèn, đơn vị mm; hệ chia sẻ dùng Active Project Location.", new Field("system", "Hệ tọa độ", options: new[] { "Tọa độ chia sẻ", "Tọa độ nội bộ Revit" }));
        var rows = new List<IEnumerable<string>> { new[] { "ID", "Ký hiệu", "Kiểu", "X / Đông (mm)", "Y / Bắc (mm)", "Z / Cao độ (mm)", "Hệ tọa độ" } };
        foreach (var pile in piles)
        {
            var point = ((LocationPoint)pile.Location).Point;
            if (input.Index("system") == 0) { var p = c.Doc.ActiveProjectLocation.GetProjectPosition(point); point = new XYZ(p.EastWest, p.NorthSouth, p.Elevation); }
            rows.Add(new[] { ToolContext.Id(pile), ToolContext.Mark(pile), pile.Name, ToolContext.N(point.X * 304.8), ToolContext.N(point.Y * 304.8), ToolContext.N(point.Z * 304.8), input.TextValue("system") });
        }
        c.Report("Tọa độ cọc", $"Đã đọc {piles.Count} điểm chèn. Đơn vị mm.", rows);
    }

    public static void AutoNumber(ToolContext c)
    {
        var elements = c.Select("Chọn cấu kiện cần đánh số.").OrderBy(e => GeometryTools.Position(e).Z).ThenBy(e => GeometryTools.Position(e).Y).ThenBy(e => GeometryTools.Position(e).X).ThenBy(e => e.Id.IntegerValue).ToList();
        using var input = InputDialog.Ask(c.App, "Đánh số cấu kiện", "Gán tham số Mark, thứ tự Z → Y → X. Tự tránh mã đã dùng trong dự án.",
            new Field("prefix", "Tiền tố", "KC-"), new Field("start", "Số bắt đầu", "1"), new Field("digits", "Số chữ số", "3"), new Field("overwrite", "Mã hiện có", options: new[] { "Giữ nguyên", "Ghi đè các đối tượng đã chọn" }));
        var overwrite = input.Index("overwrite") == 1;
        var ids = new HashSet<ElementId>(elements.Select(e => e.Id));
        var used = new FilteredElementCollector(c.Doc).WhereElementIsNotElementType().Where(e => !overwrite || !ids.Contains(e.Id)).Select(ToolContext.Mark).Where(s => s.Length > 0);
        var sequence = new NumberSequence(input.TextValue("prefix"), input.Integer("start", 0, 99999999), input.Integer("digits", 1, 9), used);
        var targets = elements.Where(e => overwrite || string.IsNullOrWhiteSpace(ToolContext.Mark(e))).ToList();
        c.Batch("Đánh số cấu kiện", targets, e =>
        {
            var p = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) throw new ArgumentException("Không có Mark có thể ghi.");
            p.Set(sequence.Next());
        });
    }

    public static void LiningConcrete(ToolContext c)
    {
        var floors = c.Select("Chọn các sàn cần tạo bê tông lót ở mặt đáy.", e => e is Floor);
        var type = c.Choose("Loại sàn bê tông lót", new FilteredElementCollector(c.Doc).OfClass(typeof(FloorType)).Cast<FloorType>(), ToolContext.TypeName);
        using var input = InputDialog.Ask(c.App, "Bê tông lót", "Mặt trên sàn lót đặt dưới đáy sàn gốc. Chiều dày theo Floor Type đã chọn; chỉ mặt đáy nằm ngang.", new Field("gap", "Khe hở dưới đáy (mm)", "0"));
        var gap = Engineering.Feet(input.Number("gap"));
        c.Batch("Tạo bê tông lót", floors, e =>
        {
            var level = c.Doc.GetElement(e.LevelId) as Level ?? throw new ArgumentException("Không có Level hợp lệ.");
            var bottom = HostObjectUtils.GetBottomFaces((HostObject)e).Select(r => e.GetGeometryObjectFromReference(r)).OfType<PlanarFace>().Where(f => f.FaceNormal.Z < -0.9999).ToList();
            if (bottom.Count == 0) throw new ArgumentException("Không có mặt đáy phẳng nằm ngang.");
            foreach (var face in bottom)
            {
                var transform = Transform.CreateTranslation(new XYZ(0, 0, level.Elevation - face.Origin.Z));
                var loops = face.GetEdgesAsCurveLoops().Select(loop => CurveLoop.CreateViaTransform(loop, transform)).ToList();
                var lining = Floor.Create(c.Doc, loops, type.Id, level.Id);
                lining.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(face.Origin.Z - level.Elevation - gap);
            }
        });
    }

    public static void ToggleJoins(ToolContext c)
    {
        var elements = c.Select("Chọn dầm hoặc tường.", e => e is Wall || (e is FamilyInstance && ToolContext.Category(e, BuiltInCategory.OST_StructuralFraming)));
        using var input = InputDialog.Ask(c.App, "Cho/không nối đầu", "Chỉnh trạng thái nối đầu cấu kiện, không thay đổi Join Geometry giữa các khối.", new Field("mode", "Trạng thái", options: new[] { "Không cho nối", "Cho phép nối" }), new Field("end", "Đầu cấu kiện", options: new[] { "Cả hai đầu", "Đầu 0", "Đầu 1" }));
        var allow = input.Index("mode") == 1;
        var ends = input.Index("end") == 0 ? new[] { 0, 1 } : new[] { input.Index("end") - 1 };
        c.Batch("Trạng thái nối đầu", elements, e =>
        {
            foreach (var end in ends)
                if (e is Wall wall) { if (allow) WallUtils.AllowWallJoinAtEnd(wall, end); else WallUtils.DisallowWallJoinAtEnd(wall, end); }
                else { if (allow) StructuralFramingUtils.AllowJoinAtEnd((FamilyInstance)e, end); else StructuralFramingUtils.DisallowJoinAtEnd((FamilyInstance)e, end); }
        });
    }

    public static void CutByFace(ToolContext c)
    {
        var beams = c.Select("Chọn dầm cần cắt.", e => ToolContext.Category(e, BuiltInCategory.OST_StructuralFraming));
        var face = c.Ui.Selection.PickObject(ObjectType.Face, "Chọn mặt của KHỐI CẮT (Revit phải cho phép Solid-Solid Cut).");
        var cutter = c.Doc.GetElement(face.ElementId);
        c.Batch("Cắt dầm theo khối có mặt được chọn", beams, beam =>
        {
            if (beam.Id == cutter.Id) throw new ArgumentException("Dầm và khối cắt không được trùng nhau.");
            SolidSolidCutUtils.AddCutBetweenSolids(c.Doc, beam, cutter);
        });
    }

    public static void StructuralUtilities(ToolContext c)
    {
        using var input = InputDialog.Ask(c.App, "Tiện ích kết cấu", "Cô lập tạm trong view hoặc kiểm tra các đối tượng được chọn.", new Field("mode", "Tác vụ", options: new[] { "Xuất kiểm tra cấu kiện", "Cô lập đối tượng được chọn", "Bỏ cô lập tạm" }));
        if (input.Index("mode") == 2) { c.Write("Bỏ cô lập", () => c.Doc.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate)); return; }
        var elements = c.Select("Chọn đối tượng cần kiểm tra/cô lập.");
        if (input.Index("mode") == 1) { c.Write("Cô lập tạm", () => c.Doc.ActiveView.IsolateElementsTemporary(elements.Select(e => e.Id).ToList())); return; }
        var rows = new List<IEnumerable<string>> { new[] { "ID", "Danh mục", "Kiểu", "Mark", "Thể tích (m³)", "Thiếu Mark" } };
        rows.AddRange(elements.Select(e => new[] { ToolContext.Id(e), e.Category?.Name ?? "", e.Name, ToolContext.Mark(e), ToolContext.N(Engineering.CubicMetres(GeometryTools.Volume(e))), string.IsNullOrWhiteSpace(ToolContext.Mark(e)) ? "Có" : "" }));
        c.Report("Kiểm tra cấu kiện", $"{elements.Count} cấu kiện; thiếu Mark: {elements.Count(e => string.IsNullOrWhiteSpace(ToolContext.Mark(e)))}.", rows);
    }
}
