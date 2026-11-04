using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using KetCauViet.Core;
using KetCauViet.Revit.Infrastructure;

namespace KetCauViet.Revit.Tools;

internal class RebarIssue : Exception
{
    public string Kind { get; }
    public RebarIssue(string kind, string message, Exception? inner = null) : base(message, inner) => Kind = kind;
}

internal sealed class RebarHostGeometry
{
    internal static Vector3 Vec(XYZ p) => new(p.X, p.Y, p.Z);
    internal static XYZ Xyz(Vector3 p) => new(p.X, p.Y, p.Z);
    internal static readonly double Tolerance = Engineering.Feet(.2);
    public Element Host { get; }
    public MemberFrame Frame { get; }
    public XYZ U => Xyz(Frame.U);
    public XYZ V => Xyz(Frame.V);
    public XYZ Axis => Xyz(Frame.Axis);
    public XYZ Min { get; private set; }
    public XYZ Max { get; private set; }
    public IList<Solid> Owned { get; }
    public IList<Solid> Envelope { get; }
    public string Kind { get; }
    private readonly Action<string> log;
    private readonly IList<(Vector3 Normal, double Offset)>? convexPlanes;
    private readonly List<IList<(Vector3 Normal, double Offset)>> certifiedConvex = new();

    public XYZ Point(double u, double v, double s) => Xyz(Frame.ToWorld(new Vector3(u, v, s)));
    public XYZ Local(XYZ point) => Xyz(Frame.ToLocal(Vec(point)));

    public static bool SupportedCategory(Element e) => ToolContext.Category(e, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors);

    public RebarHostGeometry(Element host, Action<string> log)
    {
        Host = host; this.log = log;
        if (!SupportedCategory(host)) throw new RebarIssue("HOST", "Danh mục không phải Structural Framing, Structural Columns, Walls hoặc Floors.");
        if ((ToolContext.Category(host, BuiltInCategory.OST_Walls) && !(host is Wall)) ||
            (ToolContext.Category(host, BuiltInCategory.OST_Floors) && !(host is Floor)))
            throw new RebarIssue("HOST", "Đối tượng in-place/DirectShape không có cấu tạo lớp Wall/Floor chuẩn để xác minh vùng bê tông. Cần host hệ thống hoặc bố trí riêng.");
        var data = RebarHostData.GetRebarHostData(host);
        if (data == null || !data.IsValidHost()) throw new RebarIssue("HOST", "Rebar Host không hợp lệ. Kiểm tra vật liệu kết cấu bê tông, Structural của wall/floor và template kết cấu.");
        log("HOST valid; category=" + host.Category.Name + "; element=" + host.Id.IntegerValue);
        Kind = host is Floor ? "floor" : host is Wall ? "wall" : ToolContext.Category(host, BuiltInCategory.OST_StructuralFraming) ? "beam" : "column";
        var actual = GeometryTools.Solids(host).ToList();
        log("GeometryElement thực: solids=" + actual.Count + "; volume(m³)=" + Engineering.CubicMetres(actual.Sum(s => s.Volume)));
        if (actual.Count == 0) throw new RebarIssue("PROFILE", "Không đọc được Solid có thể tích dương từ GeometryElement thực tế.");
        IList<Solid> nominal;
        if (host is FamilyInstance instance)
        {
            var original = instance.GetOriginalGeometry(new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false });
            nominal = ConcreteFamilySolids(host, GeometryTools.ReadSolids(original, instance.GetTransform()).ToList());
            log("GetOriginalGeometry trước Join/Cut: solids=" + nominal.Count + "; volume(m³)=" + Engineering.CubicMetres(nominal.Sum(s => s.Volume)) + "; transform=" + instance.GetTransform());
            Frame = FamilyFrame(instance, nominal, Kind == "beam");
            actual = ConcreteFamilySolids(host, actual).ToList();
        }
        else
        {
            nominal = actual;
            Frame = SystemFrame(host, actual);
        }
        var bounds = Bounds(nominal); Min = bounds.Min; Max = bounds.Max;
        if (host is FamilyInstance)
        {
            var body = UnionConnected(nominal);
            RectangleSection? reference = null;
            foreach (var fraction in new[] { .25, .5, .75 })
            {
                var at = Min.Z + (Max.Z - Min.Z) * fraction;
                var cut = BooleanOperationsUtils.CutWithHalfSpace(body, Plane.CreateByNormalAndOrigin(Axis, Point(0, 0, at)));
                var face = cut.Faces.Cast<Face>().OfType<PlanarFace>().FirstOrDefault(f => Math.Abs(Math.Abs(f.FaceNormal.DotProduct(Axis)) - 1) < 1e-7 && Math.Abs((f.Origin - Point(0, 0, at)).DotProduct(Axis)) < Tolerance);
                if (face == null) throw new RebarIssue("PROFILE", $"Không tìm được mặt tiết diện danh nghĩa tại {fraction:P0} chiều dài.");
                var loops = face.GetEdgesAsCurveLoops();
                log("Tiết diện danh nghĩa tại " + fraction + ": loops=" + loops.Count + "; curves=" + string.Join(",", loops.Select(l => l.Count())));
                if (loops.Count != 1 || loops.Any(loop => loop.Any(curve => !(curve is Line)))) throw new RebarIssue("PROFILE", "Tiết diện danh nghĩa có đường cong, lỗ hoặc nhiều miền; cần thuật toán bố trí riêng.");
                RectangleSection section;
                try { section = RectangleSection.Recognize(loops[0].Select(c => Vec(Local(c.GetEndPoint(0)))), loops.Count, Tolerance); reference?.RequireSame(section, Tolerance); }
                catch (ArgumentException ex) { throw new RebarIssue("PROFILE", ex.Message, ex); }
                reference = section;
                log("Tiết diện chữ nhật: U=" + (section.MaxU - section.MinU) * 304.8 + " mm; V=" + (section.MaxV - section.MinV) * 304.8 + " mm");
            }
            Min = new XYZ(reference!.MinU, reference.MinV, Min.Z); Max = new XYZ(reference.MaxU, reference.MaxV, Max.Z);
        }
        Owned = CoreSolids(host, actual, Frame).ToList();
        if (Owned.Count == 0) throw new RebarIssue("PROFILE", "Không tìm thấy miền bê tông kết cấu sau khi tách lớp vật liệu.");
        if (!(host is FamilyInstance)) { bounds = Bounds(Owned); Min = bounds.Min; Max = bounds.Max; }
        var envelope = new List<Solid>(Owned);
        var neighbors = JoinGeometryUtils.GetJoinedElements(host.Document, host);
        foreach (var id in neighbors)
        {
            var neighbor = host.Document.GetElement(id);
            if (!SupportedCategory(neighbor) || RebarHostData.GetRebarHostData(neighbor)?.IsValidHost() != true) continue;
            try
            {
                var solids = GeometryTools.Solids(neighbor).ToList();
                if (neighbor is FamilyInstance) solids = ConcreteFamilySolids(neighbor, solids).ToList();
                else solids = CoreSolids(neighbor, solids, SystemFrame(neighbor, solids)).ToList();
                envelope.AddRange(solids); log("Bê tông liên kết được xác minh: ID " + id.IntegerValue + ", solids=" + solids.Count);
            }
            catch (Exception ex) { log("Không dùng vùng neo của cấu kiện liên kết ID " + id.IntegerValue + ": " + ex.Message); }
        }
        // Unite touching joined bodies before eroding cover, so a concealed join plane
        // is not mistaken for an exposed concrete surface.
        Envelope = Coalesce(envelope);
        if (Envelope.Count == 1) convexPlanes = ConvexPlanes(Envelope[0]);
        foreach (var solid in envelope)
        {
            var planes = ConvexPlanes(solid); if (planes != null) certifiedConvex.Add(planes);
        }
        if (host is FamilyInstance)
        {
            var body = UnionConnected(nominal); var planes = ConvexPlanes(body);
            if (planes != null)
            {
                try { if (ContainsSolid(body)) { certifiedConvex.Add(planes); log("Đã chứng minh toàn bộ lăng trụ danh nghĩa nằm trong bê tông sau Join."); } }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex) { log("Không dùng chứng chỉ lăng trụ danh nghĩa: " + ex.Message); }
            }
        }
        log($"Hình học: loại={Kind}; nominal solids={nominal.Count}; owned solids={Owned.Count}; joined={neighbors.Count}; envelope solids={Envelope.Count}; convex={convexPlanes != null}");
        log($"Local bounds (mm): U={Min.X * 304.8:F3}..{Max.X * 304.8:F3}, V={Min.Y * 304.8:F3}..{Max.Y * 304.8:F3}, S={Min.Z * 304.8:F3}..{Max.Z * 304.8:F3}");
        log($"Frame origin={Xyz(Frame.Origin)}; U={U}; V={V}; axis={Axis}");
    }

    private (XYZ Min, XYZ Max) Bounds(IEnumerable<Solid> solids)
    {
        var vertices = GeometryTools.Vertices(solids).Select(Local).ToList();
        if (vertices.Count == 0) throw new RebarIssue("PROFILE", "Solid không có đỉnh hình học.");
        return (new XYZ(vertices.Min(p => p.X), vertices.Min(p => p.Y), vertices.Min(p => p.Z)), new XYZ(vertices.Max(p => p.X), vertices.Max(p => p.Y), vertices.Max(p => p.Z)));
    }

    private static MemberFrame FamilyFrame(FamilyInstance instance, IList<Solid> nominal, bool beam)
    {
        XYZ axis; XYZ origin;
        if (instance.Location is LocationCurve location)
        {
            if (!(location.Curve is Line line)) throw new RebarIssue("PROFILE", "Trục Family là đường cong; chỉ hỗ trợ trục thẳng, kể cả trục nghiêng.");
            axis = line.Direction; origin = line.GetEndPoint(0);
        }
        else { axis = instance.GetTransform().BasisZ.Normalize(); origin = instance.GetTransform().Origin; }
        var target = beam ? XYZ.BasisZ : instance.GetTransform().BasisY;
        var normals = nominal.SelectMany(s => s.Faces.Cast<Face>()).OfType<PlanarFace>()
            .Where(f => Math.Abs(f.FaceNormal.DotProduct(axis)) < 1e-6).Select(f => f.FaceNormal).ToList();
        if (normals.Count == 0) throw new RebarIssue("PROFILE", "Không nhận diện được mặt bên phẳng song song trục Family danh nghĩa.");
        var up = normals.OrderByDescending(n => Math.Abs(n.DotProduct(target))).First();
        if (up.DotProduct(target) < 0) up = -up;
        return new MemberFrame(Vec(origin), Vec(axis), Vec(up));
    }

    private static MemberFrame SystemFrame(Element host, IList<Solid> solids)
    {
        if (host is Wall wall)
        {
            if (!(wall.Location is LocationCurve location) || !(location.Curve is Line line) || Math.Abs(line.Direction.Z) > 1e-7)
                throw new RebarIssue("PROFILE", "Vách phải có trục thẳng nằm ngang; vách cong hoặc nghiêng cần bố trí riêng.");
            if (Math.Abs(wall.Orientation.Z) > 1e-6) throw new RebarIssue("PROFILE", "Chưa hỗ trợ vách nghiêng.");
            return new MemberFrame(Vec(line.GetEndPoint(0)), Vec(XYZ.BasisZ), Vec(wall.Orientation));
        }
        // Use major exterior faces: Fine geometry can expose the top of every
        // compound layer. Internal finish/core interfaces are not multiple slopes.
        var faces = HostObjectUtils.GetTopFaces((HostObject)host).Select(r => host.GetGeometryObjectFromReference(r)).OfType<PlanarFace>()
            .Where(f => f.FaceNormal.Z > .01).OrderByDescending(f => f.Area).ToList();
        var top = faces.FirstOrDefault() ?? throw new RebarIssue("PROFILE", "Sàn không có mặt phẳng trên để xác định phương rải.");
        var normal = top.FaceNormal.Normalize();
        var opposite = HostObjectUtils.GetBottomFaces((HostObject)host).Select(r => host.GetGeometryObjectFromReference(r)).OfType<PlanarFace>()
            .Where(f => f.FaceNormal.DotProduct(normal) < -.999999).OrderByDescending(f => f.Area).FirstOrDefault();
        if (opposite == null) throw new RebarIssue("PROFILE", "Sàn không có hai mặt phẳng song song; chưa hỗ trợ sàn thay đổi chiều dày.");
        if (faces.Any(f => f.Area > top.Area * .01 && f.FaceNormal.DotProduct(normal) > .5 && f.FaceNormal.DotProduct(normal) < .999999))
            throw new RebarIssue("PROFILE", "Sàn có nhiều mặt dốc khác nhau hoặc thay đổi chiều dày.");
        if (faces.Any(f => f.FaceNormal.DotProduct(normal) > .999999 && Math.Abs((f.Origin - top.Origin).DotProduct(normal)) > Tolerance))
            throw new RebarIssue("PROFILE", "Sàn có các vùng mặt trên khác cao độ; cần chia vùng rải theo từng mặt.");
        var edge = top.GetEdgesAsCurveLoops().SelectMany(l => l).OfType<Line>().OrderByDescending(l => l.Length).FirstOrDefault();
        var direction = edge?.Direction ?? (XYZ.BasisX - normal * normal.DotProduct(XYZ.BasisX)).Normalize();
        return new MemberFrame(Vec(top.Origin), Vec(normal), Vec(normal.CrossProduct(direction)));
    }

    private static IList<Solid> ConcreteFamilySolids(Element host, IList<Solid> solids)
    {
        var concrete = solids.Where(s => s.Faces.Cast<Face>().Any(f => IsConcrete(host.Document, f.MaterialElementId))).ToList();
        if (concrete.Count > 0) return concrete;
        if (host is FamilyInstance fi && fi.StructuralMaterialType == StructuralMaterialType.Concrete) return solids;
        throw new RebarIssue("HOST", "Không xác minh được vật liệu bê tông của Family. Cần Structural Material Type Concrete hoặc vật liệu có Structural Asset Concrete.");
    }

    private static bool IsConcrete(Document doc, ElementId id)
    {
        var material = doc.GetElement(id) as Material;
        return material != null && doc.GetElement(material.StructuralAssetId) is PropertySetElement asset && asset.GetStructuralAsset()?.StructuralAssetClass == StructuralAssetClass.Concrete;
    }

    private static IList<Solid> CoreSolids(Element host, IList<Solid> solids, MemberFrame frame)
    {
        if (host is FamilyInstance) return solids;
        var structure = host is Wall wall ? wall.WallType.GetCompoundStructure() : ((Floor)host).FloorType.GetCompoundStructure();
        if (structure == null || !structure.IsVerticallyHomogeneous()) throw new RebarIssue("PROFILE", "Cấu tạo lớp không đồng nhất; không xác định được lớp bê tông chịu lực.");
        // For a monolithic system host, Revit's native host validation is authoritative.
        // Requiring an additional StructuralAsset would reject otherwise valid library
        // materials even though there are no finish/core layers to distinguish.
        if (structure.LayerCount == 1 && RebarHostData.GetRebarHostData(host)?.IsValidHost() == true) return solids;
        var index = structure.StructuralMaterialIndex;
        if (index < 0 && structure.LayerCount == 1) index = 0;
        if (index < 0 || !IsConcrete(host.Document, structure.GetMaterialId(index)))
            throw new RebarIssue("HOST", "Lớp Structural Material không phải bê tông hoặc chưa có Structural Asset Concrete.");
        if (structure.LayerCount == 1) return solids;
        var local = GeometryTools.Vertices(solids).Select(p => frame.ToLocal(Vec(p))).ToList();
        var min = new XYZ(local.Min(p => p.X), local.Min(p => p.Y), local.Min(p => p.Z));
        var max = new XYZ(local.Max(p => p.X), local.Max(p => p.Y), local.Max(p => p.Z));
        var before = Enumerable.Range(0, index).Sum(structure.GetLayerWidth);
        if (host is Wall w)
        {
            var location = (WallLocationLine)w.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM).AsInteger();
            var offset = structure.GetOffsetForLocationLine(location);
            var outer = structure.GetWidth() / 2 - before - offset;
            min = new XYZ(min.X, outer - structure.GetLayerWidth(index), min.Z); max = new XYZ(max.X, outer, max.Z);
        }
        else
        {
            var ratio = (max.Z - min.Z) / structure.GetWidth();
            var upper = max.Z - before * ratio;
            min = new XYZ(min.X, min.Y, upper - structure.GetLayerWidth(index) * ratio); max = new XYZ(max.X, max.Y, upper);
        }
        var loop = new CurveLoop();
        var points = new[] { min, new XYZ(max.X, min.Y, min.Z), new XYZ(max.X, max.Y, min.Z), new XYZ(min.X, max.Y, min.Z) }.Select(p => Xyz(frame.ToWorld(Vec(p)))).ToArray();
        for (var i = 0; i < 4; i++) loop.Append(Line.CreateBound(points[i], points[(i + 1) % 4]));
        var band = GeometryCreationUtilities.CreateExtrusionGeometry(new[] { loop }, Xyz(frame.Axis), max.Z - min.Z);
        return solids.Select(s => BooleanOperationsUtils.ExecuteBooleanOperation(s, band, BooleanOperationsType.Intersect)).Where(s => s.Volume > 1e-9).ToList();
    }

    private static Solid UnionConnected(IList<Solid> solids)
    {
        if (solids.Count == 0) throw new RebarIssue("PROFILE", "Family không có solid bê tông danh nghĩa.");
        var result = solids[0];
        for (var i = 1; i < solids.Count; i++) result = BooleanOperationsUtils.ExecuteBooleanOperation(result, solids[i], BooleanOperationsType.Union);
        return result;
    }

    private static IList<Solid> Coalesce(IList<Solid> source)
    {
        var result = new List<Solid>();
        foreach (var sourceSolid in source)
        {
            var current = sourceSolid;
            for (var i = result.Count - 1; i >= 0; i--)
            {
                try
                {
                    var merged = BooleanOperationsUtils.ExecuteBooleanOperation(current, result[i], BooleanOperationsType.Union);
                    // Disconnected unions may contain multiple shells; keep them for exact
                    // subtraction as well. No volume-ratio requirement is used to accept a host.
                    current = merged; result.RemoveAt(i);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException) { /* retain separate validated bodies */ }
            }
            result.Add(current);
        }
        return result;
    }

    private static IList<(Vector3 Normal, double Offset)>? ConvexPlanes(Solid solid)
    {
        var faces = solid.Faces.Cast<Face>().ToList();
        if (faces.Any(f => !(f is PlanarFace))) return null;
        var vertices = GeometryTools.Vertices(new[] { solid }).ToList();
        var planes = new List<(Vector3, double)>();
        foreach (PlanarFace face in faces)
        {
            var n = face.FaceNormal.Normalize(); var offset = n.DotProduct(face.Origin);
            if (vertices.Any(p => n.DotProduct(p) - offset > Tolerance)) return null;
            planes.Add((Vec(n), offset));
        }
        return planes;
    }

    public IList<LinearInterval> SafeIntervals(XYZ start, XYZ end, double clearance)
    {
        var direction = (end - start).Normalize(); var length = start.DistanceTo(end);
        if (convexPlanes != null)
        {
            var result = RebarPlanning.ClipConvex(Vec(start), Vec(direction), new LinearInterval(0, length), convexPlanes, clearance);
            return result.HasValue ? new[] { result.Value } : Array.Empty<LinearInterval>();
        }
        var line = Line.CreateBound(start, end); var intervals = new List<LinearInterval>();
        foreach (var solid in Envelope)
        {
            using var options = new SolidCurveIntersectionOptions();
            using var cut = solid.IntersectWithCurve(line, options);
            for (var i = 0; i < cut.SegmentCount; i++)
            {
                var segment = cut.GetCurveSegment(i);
                var a = (segment.GetEndPoint(0) - start).DotProduct(direction); var b = (segment.GetEndPoint(1) - start).DotProduct(direction);
                intervals.Add(new LinearInterval(Math.Min(a, b), Math.Max(a, b)));
            }
        }
        var safe = new List<LinearInterval>();
        foreach (var interval in RebarPlanning.MergeIntervals(intervals, Tolerance))
        {
            var a = interval.Start + EndTrim(start + direction * interval.Start, direction, clearance);
            var b = interval.End - EndTrim(start + direction * interval.End, direction, clearance);
            if (b - a > Host.Document.Application.ShortCurveTolerance && ContainsClearanceLine(start + direction * a, start + direction * b, clearance)) safe.Add(new LinearInterval(a, b));
        }
        return safe;
    }

    private double EndTrim(XYZ point, XYZ direction, double clearance)
    {
        var trim = 0d;
        foreach (var solid in Envelope)
            foreach (Face face in solid.Faces)
            {
                var projection = face.Project(point);
                if (projection == null || projection.Distance > Tolerance || !face.IsInside(projection.UVPoint)) continue;
                var dot = Math.Abs(face.ComputeNormal(projection.UVPoint).DotProduct(direction));
                if (dot > 1e-5) trim = Math.Max(trim, clearance / dot);
            }
        return trim;
    }

    public bool ContainsClearanceLine(XYZ start, XYZ end, double clearance)
    {
        var direction = (end - start).Normalize();
        if (convexPlanes != null)
        {
            return convexPlanes.All(plane => Vec(start).Dot(plane.Normal) <= plane.Offset - clearance + 1e-8 && Vec(end).Dot(plane.Normal) <= plane.Offset - clearance + 1e-8);
        }
        var a = start; var b = end;
        var helper = Math.Abs(direction.Z) < .9 ? XYZ.BasisZ : XYZ.BasisX;
        var x = direction.CrossProduct(helper).Normalize(); var y = direction.CrossProduct(x).Normalize();
        var ring = new CurveLoop(); ring.Append(Arc.Create(a, clearance, 0, Math.PI, x, y)); ring.Append(Arc.Create(a, clearance, Math.PI, 2 * Math.PI, x, y));
        var cylinder = GeometryCreationUtilities.CreateExtrusionGeometry(new[] { ring }, direction, a.DistanceTo(b));
        return ContainsSolid(cylinder) && ContainsSolid(Sphere(a, clearance, direction, x, y)) && ContainsSolid(Sphere(b, clearance, direction, x, y));
    }

    private static Solid Sphere(XYZ center, double radius, XYZ axis, XYZ x, XYZ y)
    {
        var profile = new CurveLoop();
        profile.Append(Arc.Create(center, radius, -Math.PI / 2, Math.PI / 2, x, axis));
        profile.Append(Line.CreateBound(center + axis * radius, center - axis * radius));
        return GeometryCreationUtilities.CreateRevolvedGeometry(new Autodesk.Revit.DB.Frame(center, x, y, axis), new[] { profile }, 0, 2 * Math.PI);
    }

    private bool ContainsSolid(Solid candidate)
    {
        var remaining = candidate;
        foreach (var solid in Envelope)
        {
            remaining = BooleanOperationsUtils.ExecuteBooleanOperation(remaining, solid, BooleanOperationsType.Difference);
            if (remaining.Volume <= 1e-10) return true;
        }
        return remaining.Volume <= 1e-10;
    }

    public void RequireClearance(Curve curve, double clearance, string label)
    {
        if (curve is Line)
        {
            if (!ContainsClearanceLine(curve.GetEndPoint(0), curve.GetEndPoint(1), clearance)) throw new RebarIssue("COVER", label + ": thép hoặc lớp bảo vệ vượt ra ngoài miền bê tông thực tế/Join đã xác minh.");
            return;
        }
        if (!(curve is Arc arc)) throw new RebarIssue("PROFILE", label + ": đường tâm API không phải Line hoặc Arc.");
        bool In(IList<(Vector3 Normal, double Offset)> planes) => planes.All(plane => RebarClearance.ArcMaximumDot(Vec(arc.Center), Vec(arc.XDirection), Vec(arc.YDirection), arc.Radius, arc.GetEndParameter(0), arc.GetEndParameter(1), plane.Normal) <= plane.Offset - clearance + 1e-8);
        if ((convexPlanes != null && In(convexPlanes)) || certifiedConvex.Any(In)) return;
        try
        {
            var tangent = arc.ComputeDerivatives(0, true).BasisX.Normalize(); var start = arc.GetEndPoint(0);
            var x = arc.Normal.Normalize(); var y = tangent.CrossProduct(x).Normalize();
            var profile = new CurveLoop(); profile.Append(Arc.Create(start, clearance, 0, Math.PI, x, y)); profile.Append(Arc.Create(start, clearance, Math.PI, 2 * Math.PI, x, y));
            var path = new CurveLoop(); path.Append(arc);
            var tube = GeometryCreationUtilities.CreateSweptGeometry(path, 0, arc.GetEndParameter(0), new[] { profile });
            if (ContainsSolid(tube) && ContainsSolid(Sphere(start, clearance, tangent, x, y)) && ContainsSolid(Sphere(arc.GetEndPoint(1), clearance, tangent, x, y))) return;
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException ex) { throw new RebarIssue("COVER", label + ": không chứng minh được miền bảo vệ của cung uốn tại Join/Cut. " + ex.Message, ex); }
        throw new RebarIssue("COVER", label + ": phần uốn/móc sau khi Revit dựng không đủ lớp bảo vệ.");
    }

    public IList<Solid> PlanarMask(double clearance, XYZ normal)
    {
        var masks = new List<Solid>();
        foreach (var solid in Owned)
            foreach (var face in solid.Faces.Cast<Face>().OfType<PlanarFace>().Where(f => f.FaceNormal.DotProduct(normal) > .999999))
            {
                var loops = face.GetEdgesAsCurveLoops().OrderByDescending(loop => Math.Abs(LoopArea(loop, normal))).ToList();
                for (var i = 0; i < loops.Count; i++) if (loops[i].IsCounterclockwise(normal) != (i == 0)) loops[i].Flip();
                // Revit face loops keep concrete to their left with the outward normal:
                // offset inward shrinks the outline and expands openings.
                IList<CurveLoop> offset;
                try { offset = loops.Select(loop => CurveLoop.CreateViaOffset(loop, -clearance, normal)).ToList(); }
                catch (Exception ex) { throw new RebarIssue("PROFILE", "Không offset được biên hoặc lỗ mở để giữ cover. Có thể biên quá hẹp, tự giao hoặc cần chia vùng: " + ex.Message, ex); }
                var depth = GeometryTools.Vertices(new[] { solid }).Max(p => (face.Origin - p).DotProduct(normal));
                if (depth <= 2 * clearance) throw new RebarIssue("COVER", "Chiều dày bê tông không đủ cho hai lớp bảo vệ và đường kính thép.");
                // The mask clips the in-plane outline/openings only. Extend its depth
                // slightly past both faces so a grid at exact top/bottom cover is not
                // mistaken for a tangent/no-intersection curve by the Revit kernel.
                // Normal cover is independently checked against actual concrete solids.
                var shifted = offset.Select(loop => CurveLoop.CreateViaTransform(loop, Transform.CreateTranslation(normal * Tolerance))).ToList();
                masks.Add(GeometryCreationUtilities.CreateExtrusionGeometry(shifted, -normal, depth + 2 * Tolerance));
            }
        if (masks.Count == 0) throw new RebarIssue("PROFILE", "Không tạo được miền rải từ mặt phẳng và biên mở lỗ thực tế.");
        log("Miền rải có offset lớp bảo vệ: " + masks.Count + " solid(s)");
        return masks;
    }

    private static double LoopArea(CurveLoop loop, XYZ normal)
    {
        var points = loop.SelectMany(c => c.Tessellate().Take(c.Tessellate().Count - 1)).ToList();
        var origin = points[0];
        var area = 0d;
        for (var i = 0; i < points.Count; i++) area += (points[i] - origin).CrossProduct(points[(i + 1) % points.Count] - origin).DotProduct(normal);
        return area / 2;
    }
}
