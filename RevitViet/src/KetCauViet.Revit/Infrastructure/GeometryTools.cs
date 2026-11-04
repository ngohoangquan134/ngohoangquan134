using Autodesk.Revit.DB;

namespace KetCauViet.Revit.Infrastructure;

internal static class GeometryTools
{
    public static IEnumerable<Solid> Solids(Element element)
    {
        var geometry = element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
        return geometry == null ? Enumerable.Empty<Solid>() : ReadSolids(geometry, Transform.Identity).ToList();
    }
    internal static IEnumerable<Solid> ReadSolids(GeometryElement geometry, Transform transform)
    {
        foreach (GeometryObject item in geometry)
        {
            if (item is Solid solid && solid.Volume > 1e-8)
                yield return SolidUtils.CreateTransformed(solid, transform);
            else if (item is GeometryInstance instance)
                foreach (var child in ReadSolids(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform))) yield return child;
        }
    }

    public static IEnumerable<XYZ> Vertices(IEnumerable<Solid> solids)
    {
        foreach (var solid in solids)
            foreach (Face face in solid.Faces)
            {
                var mesh = face.Triangulate();
                foreach (XYZ point in mesh.Vertices) yield return point;
            }
    }

    public static double Volume(Element element)
    {
        var p = element.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
        return p?.StorageType == StorageType.Double ? p.AsDouble() : Solids(element).Sum(s => s.Volume);
    }

    public static XYZ Position(Element e)
    {
        if (e.Location is LocationPoint point) return point.Point;
        if (e.Location is LocationCurve line) return line.Curve.Evaluate(0.5, true);
        var box = e.get_BoundingBox(null);
        if (box == null) throw new ArgumentException("Không xác định được vị trí của cấu kiện.");
        return box.Transform.OfPoint((box.Min + box.Max) * 0.5);
    }

    public static string UniqueViewName(Document doc, string proposed)
    {
        var used = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
        var name = proposed;
        for (var n = 2; used.Contains(name); n++) name = proposed + " (" + n + ")";
        return name;
    }

    public static IList<(Reference Reference, Face Face)> PickFaces(ToolContext context)
    {
        var refs = context.Ui.Selection.PickObjects(Autodesk.Revit.UI.Selection.ObjectType.Face, "Chọn các mặt; nhấn Finish để kết thúc.");
        var unique = refs.GroupBy(r => r.ConvertToStableRepresentation(context.Doc)).Select(g => g.First());
        var result = unique.Select(r => (r, context.Doc.GetElement(r.ElementId).GetGeometryObjectFromReference(r) as Face))
            .Where(x => x.Item2 != null).Select(x => (x.r, x.Item2!)).ToList();
        if (result.Count == 0) throw new ArgumentException("Cần chọn ít nhất một mặt hợp lệ.");
        return result;
    }

    public static (XYZ Origin, XYZ Normal) WorldPlane(Document doc, Reference reference)
    {
        var element = doc.GetElement(reference.ElementId);
        if (!(element.GetGeometryObjectFromReference(reference) is PlanarFace direct)) throw new ArgumentException("Tham chiếu không phải mặt phẳng.");
        var stable = reference.ConvertToStableRepresentation(doc);
        var geometry = element.get_Geometry(new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine });
        if (geometry != null)
            foreach (var pair in Faces(geometry, Transform.Identity))
            {
                if (pair.Face.Reference == null || pair.Face.Reference.ConvertToStableRepresentation(doc) != stable) continue;
                return (pair.Transform.OfPoint(pair.Face.Origin), pair.Transform.OfVector(pair.Face.FaceNormal).Normalize());
            }
        // System elements expose their faces directly in model coordinates. Do not
        // guess a symbol-to-model transform when a family reference cannot be resolved.
        if (element is FamilyInstance) throw new ArgumentException("Không xác định được hệ tọa độ của mặt family. Hãy chọn mặt hệ thống hoặc mặt khác.");
        return (direct.Origin, direct.FaceNormal.Normalize());
    }

    private static IEnumerable<(PlanarFace Face, Transform Transform)> Faces(GeometryElement geometry, Transform transform)
    {
        foreach (GeometryObject item in geometry)
            if (item is Solid solid)
            {
                foreach (Face face in solid.Faces) if (face is PlanarFace planar) yield return (planar, transform);
            }
            else if (item is GeometryInstance instance)
                foreach (var face in Faces(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform))) yield return face;
    }
}
