using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using KetCauViet.Core;

namespace KetCauViet.Revit.Tools;

internal static class RebarTemplates
{
    public static double Diameter(RebarBarType type)
    {
#if REVIT2022
        var value=type.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble();
#else
        var value = type.BarModelDiameter;
#endif
        if (!Engineering.Finite(value) || value <= 0) throw new RebarIssue("BAR_TYPE", "Rebar Bar Type " + type.Name + " có đường kính mô hình không hợp lệ.");
        return value;
    }

    public static void Validate(RebarBarType type, Document doc, bool stirrup)
    {
        if (type.Document != doc || !type.IsValidObject) throw new RebarIssue("BAR_TYPE", "Rebar Bar Type không thuộc tài liệu hiện tại hoặc đã mất hiệu lực.");
        var diameter = Diameter(type);
        var bend = stirrup ? type.StirrupTieBendDiameter : type.StandardBendDiameter;
        if (!Engineering.Finite(bend) || bend < diameter) throw new RebarIssue("BAR_TYPE", "Đường kính uốn của " + type.Name + " nhỏ hơn đường kính thanh hoặc không hợp lệ.");
    }

    public static double Cover(Element host, bool fromHost, double entered, Action<string> log)
    {
        if (!fromHost) { log("Lớp bảo vệ đồng đều do người dùng nhập: " + entered * 304.8 + " mm"); return entered; }
        var values = new List<double>();
        foreach (var parameter in new[] { BuiltInParameter.CLEAR_COVER, BuiltInParameter.CLEAR_COVER_OTHER, BuiltInParameter.CLEAR_COVER_TOP, BuiltInParameter.CLEAR_COVER_BOTTOM, BuiltInParameter.CLEAR_COVER_INTERIOR, BuiltInParameter.CLEAR_COVER_EXTERIOR })
        {
            var p = host.get_Parameter(parameter);
            if (p?.StorageType != StorageType.ElementId || !(host.Document.GetElement(p.AsElementId()) is RebarCoverType cover)) continue;
            if (cover.CoverDistance > 0) { values.Add(cover.CoverDistance); log("Host cover " + parameter + "=" + cover.CoverDistance * 304.8 + " mm"); }
        }
        if (values.Count == 0) throw new RebarIssue("COVER", "Host chưa có Rebar Cover Type hợp lệ. Gán cover trong Properties hoặc chọn chế độ nhập lớp bảo vệ.");
        var maximum = values.Max(); log("Dùng cover đồng đều bằng giá trị host lớn nhất: " + maximum * 304.8 + " mm"); return maximum;
    }

    public static RebarHookType? Hook(Document doc, ref RebarBarType type, bool enabled, Action<string> log)
    {
        if (!enabled) return null;
        var selected = type;
        var hooks = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .Where(h => h.Style == RebarStyle.StirrupTie && Math.Abs(h.HookAngle - 3 * Math.PI / 4) < 1e-6).ToList();
        var hook = hooks.FirstOrDefault(h => selected.GetHookPermission(h.Id)) ?? hooks.FirstOrDefault();
        if (hook == null)
        {
            hook = RebarHookType.Create(doc, 3 * Math.PI / 4, 10); hook.Style = RebarStyle.StirrupTie;
            hook.Name = "KCV - Móc đai 135 - " + hook.Id.IntegerValue;
        }
        if (!type.GetHookPermission(hook.Id))
        {
            type = (RebarBarType)type.Duplicate(type.Name + " - KCV 135 - " + Guid.NewGuid().ToString("N").Substring(0, 6));
            type.SetHookPermission(hook.Id, true);
            Validate(type, doc, true);
            log("Tạo bản sao Type cho phép móc 135; original=" + selected.Id.IntegerValue + "; effective=" + type.Id.IntegerValue + "; không sửa Type gốc.");
        }
        log("Hook 135: ID=" + hook.Id.IntegerValue + "; multiplier=" + hook.StraightLineMultiplier); return hook;
    }

    public static void EnsureSeed(Document doc, Action<string> log)
    {
        var shapeCount = new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).GetElementCount();
        var lengthParameters = RebarShapeParameters.GetAllRebarShapeParameters(doc)
            .Select(doc.GetElement).OfType<ParameterElement>().Count(p => p.GetDefinition().GetDataType() == SpecTypeId.ReinforcementLength);
        if (shapeCount > 0 && lengthParameters >= 6) return;
        log("Shape hiện có=" + shapeCount + "; tham số chiều dài=" + lengthParameters + "; bổ sung pool tham số cho CreateFromCurves.");
        // Create a genuine parametric straight shape, with spare legal reinforcement
        // parameters for Revit's CreateFromCurves shape matcher. No dummy model bar.
        var app = doc.Application; var previous = app.SharedParametersFilename;
        var path = Path.Combine(Path.GetTempPath(), "kcv-shape-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "# This is a Revit shared parameter file.\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n");
            app.SharedParametersFilename = path;
            var definitions = app.OpenSharedParameterFile() ?? throw new RebarIssue("BAR_TYPE", "Không tạo được tập định nghĩa tham số RebarShape chuẩn.");
            var group = definitions.Groups.Create("KetCauViet_Rebar");
            var shape = new RebarShapeDefinitionBySegments(doc, 1);
            ElementId? a = null;
            for (var i = 0; i < 6; i++)
            {
                var option = new ExternalDefinitionCreationOptions("KCV_RB_" + (char)('A' + i), SpecTypeId.ReinforcementLength)
                { GUID = new Guid("04f70cab-8736-42a8-bd73-00e01310000" + i) };
                var definition = (ExternalDefinition)group.Definitions.Create(option);
                var id = RebarShapeParameters.GetOrCreateElementIdForExternalDefinition(doc, definition);
                shape.AddParameter(id, 1); if (i == 0) a = id;
            }
            shape.AddConstraintToSegment(0, a!, 1, 0, 1, false, false);
            shape.SetSegmentFixedDirection(0, 1, 0);
            if (!shape.CheckDefaultParameterValues(0, 0)) throw new RebarIssue("BAR_TYPE", "Bộ giải Revit không xác nhận được Shape thẳng tham số; cần nạp shape hợp lệ từ thư viện.");
            var created = RebarShape.Create(doc, shape, null, RebarStyle.Standard, StirrupTieAttachmentType.InteriorFace, 0, RebarHookOrientation.Left, 0, RebarHookOrientation.Right, 0);
            created.Name = "KCV - Thanh thẳng tham số - " + created.Id.IntegerValue;
            log("Đã bổ sung RebarShape seed có tham số thật: ID=" + created.Id.IntegerValue);
        }
        catch (Exception ex) when (!(ex is RebarIssue)) { throw new RebarIssue("BAR_TYPE", "Không khởi tạo được shape tham số. Có thể nạp RebarShape từ thư viện Revit rồi chạy lại: " + ex.Message, ex); }
        finally { app.SharedParametersFilename = previous; if (File.Exists(path)) File.Delete(path); }
    }
}
