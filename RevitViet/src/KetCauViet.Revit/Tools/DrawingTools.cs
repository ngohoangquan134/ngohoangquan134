using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI.Selection;
using KetCauViet.Core;
using KetCauViet.Revit.Infrastructure;
using KetCauViet.Revit.UI;

namespace KetCauViet.Revit.Tools;

internal static class DrawingTools
{
    public static void LoadFamily(ToolContext c)
    {
        using var file = new OpenFileDialog { Title = "Chọn family Revit", Filter = "Revit Family (*.rfa)|*.rfa", Multiselect = true };
        if (file.ShowDialog(new InputDialog.WindowOwner(c.App.MainWindowHandle)) != DialogResult.OK) return;
        var loaded = 0; var unchanged = 0; var errors = new List<string>();
        c.Write("Nạp family", () =>
        {
            foreach (var name in file.FileNames)
            {
                using var sub = new SubTransaction(c.Doc); sub.Start();
                try { if (c.Doc.LoadFamily(name, out _)) loaded++; else unchanged++; sub.Commit(); }
                catch (Exception ex) { sub.RollBack(); errors.Add(System.IO.Path.GetFileName(name) + ": " + ex.Message); }
            }
        });
        c.Summary("Nạp family", $"Đã nạp: {loaded}. Không thay đổi/đã có: {unchanged}. Lỗi: {errors.Count}.", errors);
    }

    public static void BeamDrawing(ToolContext c)
    {
        var beams = c.Select("Chọn các dầm thẳng để tạo mặt cắt.", e => ToolContext.Category(e, BuiltInCategory.OST_StructuralFraming));
        var sectionType = new FilteredElementCollector(c.Doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.Section) ?? throw new ArgumentException("Dự án không có Section View Family Type.");
        using var input = InputDialog.Ask(c.App, "Mặt cắt dầm", "Tạo view mặt cắt hình học; không tự bố trí sheet hoặc hoàn thiện bản vẽ cốt thép.", new Field("mode", "Hướng mặt cắt", options: new[] { "Dọc và ngang", "Chỉ dọc", "Chỉ ngang" }), new Field("margin", "Khoảng bao quanh (mm)", "300"), new Field("scale", "Tỷ lệ 1 :", "50"));
        var margin = Engineering.Feet(input.Number("margin", 10, 10000)); var scale = input.Integer("scale", 1, 1000);
        c.Batch("Tạo mặt cắt dầm", beams, e =>
        {
            if (!(e.Location is LocationCurve location) || !(location.Curve is Line line)) throw new ArgumentException("Chỉ nhận dầm có đường trục thẳng.");
            var axis = line.Direction.Normalize();
            var up = XYZ.BasisZ - axis * axis.DotProduct(XYZ.BasisZ);
            if (up.GetLength() < 1e-6) throw new ArgumentException("Trục dầm thẳng đứng không được hỗ trợ.");
            up = up.Normalize();
            var origin = line.Evaluate(0.5, true);
            var vertices = GeometryTools.Vertices(GeometryTools.Solids(e)).ToList();
            if (vertices.Count == 0) throw new ArgumentException("Không có hình học solid.");
            var modes = input.Index("mode") == 0 ? new[] { false, true } : new[] { input.Index("mode") == 2 };
            foreach (var cross in modes)
            {
                var x = cross ? up.CrossProduct(axis).Normalize() : axis;
                var y = up;
                var z = x.CrossProduct(y).Normalize();
                var frame = Transform.Identity; frame.Origin = origin; frame.BasisX = x; frame.BasisY = y; frame.BasisZ = z;
                var local = vertices.Select(p => frame.Inverse.OfPoint(p)).ToList();
                var min = new XYZ(local.Min(p => p.X) - margin, local.Min(p => p.Y) - margin, cross ? -margin : local.Min(p => p.Z) - margin);
                var max = new XYZ(local.Max(p => p.X) + margin, local.Max(p => p.Y) + margin, cross ? margin : local.Max(p => p.Z) + margin);
                var view = ViewSection.CreateSection(c.Doc, sectionType.Id, new BoundingBoxXYZ { Transform = frame, Min = min, Max = max });
                view.Name = GeometryTools.UniqueViewName(c.Doc, "KCV - Dầm " + (ToolContext.Mark(e).Length > 0 ? ToolContext.Mark(e) : ToolContext.Id(e)) + (cross ? " - Ngang" : " - Dọc"));
                view.Scale = scale;
            }
        });
    }

    public static void Rebar2D(ToolContext c)
    {
        if (c.Doc.ActiveView is View3D || c.Doc.ActiveView is ViewSchedule || c.Doc.ActiveView is ViewSheet) throw new ArgumentException("Hãy mở view mặt bằng/mặt cắt/detail hỗ trợ Detail Curve.");
        var selected = c.Select("Chọn một thanh/bộ thép phẳng để vẽ hình dạng 2D.", e => e is Rebar);
        if (selected.Count != 1) throw new ArgumentException("Chỉ chọn một thanh/bộ thép mỗi lần.");
        var rebar = (Rebar)selected[0];
        if (!rebar.IsRebarShapeDriven()) throw new ArgumentException("Chỉ hỗ trợ thép shape-driven, không hỗ trợ free-form.");
        var curves = rebar.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, 0);
        if (curves.Count == 0) throw new ArgumentException("Không đọc được đường tâm thép.");
        var normal = rebar.GetShapeDrivenAccessor().Normal.Normalize();
        var origin = curves[0].GetEndPoint(0);
        if (curves.SelectMany(curve => curve.Tessellate()).Any(p => Math.Abs((p - origin).DotProduct(normal)) > Engineering.Feet(0.5))) throw new ArgumentException("Hình dạng thép không nằm trên một mặt phẳng.");
        var x = curves[0].ComputeDerivatives(0.5, true).BasisX.Normalize();
        var source = Transform.Identity; source.Origin = origin; source.BasisX = x; source.BasisY = normal.CrossProduct(x).Normalize(); source.BasisZ = normal;
        var picked = c.Ui.Selection.PickPoint("Chọn vị trí đặt hình thép 2D trong view.");
        var view = c.Doc.ActiveView;
        picked -= view.ViewDirection * (picked - view.Origin).DotProduct(view.ViewDirection);
        var target = Transform.Identity; target.Origin = picked; target.BasisX = view.RightDirection; target.BasisY = view.UpDirection; target.BasisZ = view.RightDirection.CrossProduct(view.UpDirection);
        var mapping = target.Multiply(source.Inverse);
        c.Write("Vẽ hình thép 2D", () =>
        {
            foreach (var curve in curves) c.Doc.Create.NewDetailCurve(view, curve.CreateTransformed(mapping));
            var textType = new FilteredElementCollector(c.Doc).OfClass(typeof(TextNoteType)).FirstElementId();
            if (textType != ElementId.InvalidElementId)
                TextNote.Create(c.Doc, view.Id, picked + view.UpDirection * Engineering.Feet(150), "Thép " + ToolContext.Mark(rebar) + " · " + rebar.Name, textType);
        });
        c.Summary("Hình dạng thép 2D", "Đã tạo hình thép bằng Detail Curves đúng kích thước mô hình. Hình này độc lập, không tự cập nhật khi thép nguồn thay đổi.");
    }

    public static void AutoDimension(ToolContext c)
    {
        var view = c.Doc.ActiveView;
        if (view is View3D || view is ViewSchedule || view is ViewSheet) throw new ArgumentException("Hãy mở view 2D hỗ trợ kích thước.");
        var faces = GeometryTools.PickFaces(c);
        if (faces.Count < 2) throw new ArgumentException("Chọn ít nhất hai mặt phẳng song song.");
        if (faces.Any(x => !(x.Face is PlanarFace))) throw new ArgumentException("Kích thước chỉ hỗ trợ mặt phẳng.");
        var planes = faces.Select(pair => (pair.Reference, Plane: GeometryTools.WorldPlane(c.Doc, pair.Reference))).ToList();
        var normal = planes[0].Plane.Normal;
        if (Math.Abs(normal.DotProduct(view.ViewDirection)) > 1e-6) throw new ArgumentException("Pháp tuyến các mặt phải nằm trong mặt phẳng view.");
        if (planes.Any(x => Math.Abs(Math.Abs(x.Plane.Normal.DotProduct(normal)) - 1) > 1e-6)) throw new ArgumentException("Các mặt phải song song nhau.");
        var ordered = planes.OrderBy(x => x.Plane.Origin.DotProduct(normal)).ToList();
        var distances = ordered.Select(x => x.Plane.Origin.DotProduct(normal)).ToList();
        if (distances.Zip(distances.Skip(1), (a, b) => b - a).Any(d => d < c.Doc.Application.ShortCurveTolerance)) throw new ArgumentException("Có các mặt trùng mặt phẳng. Chọn các mặt ở vị trí khác nhau.");
        var point = c.Ui.Selection.PickPoint("Chọn vị trí đường kích thước.");
        point -= view.ViewDirection * (point - view.Origin).DotProduct(view.ViewDirection);
        var baseDistance = point.DotProduct(normal);
        var line = Line.CreateBound(point + normal * (distances.First() - baseDistance - 1), point + normal * (distances.Last() - baseDistance + 1));
        var references = new ReferenceArray(); foreach (var face in ordered) references.Append(face.Reference);
        c.Write("Ghi kích thước", () => c.Doc.Create.NewDimension(view, line, references));
    }
}
