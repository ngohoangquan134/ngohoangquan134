using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KetCauViet.Core;
using KetCauViet.Revit.Infrastructure;

namespace KetCauViet.Revit.Tools;

internal sealed class RebarConfigurationIssue : RebarIssue
{
    public string DetailCode { get; }
    public RebarConfigurationIssue(string code, string message, Exception? inner = null) : base("BAR_TYPE", "[" + code + "] " + message, inner) => DetailCode = code;
}

// A fresh catalog per external-command invocation. Never static and never shared
// between documents. The dialog stores TypeChoice values, not native API wrappers.
internal sealed class RebarTypeCatalog : IDisposable
{
    private readonly ToolContext context;
    private readonly Document document;
    private readonly TypeSelectionScope<RebarBarType> scope;
    public IList<string> Rejected { get; } = new List<string>();
    public Document Document => document;
    public RebarTypeCatalog(ToolContext context)
    {
        this.context = context; document = context.Doc;
        scope = new TypeSelectionScope<RebarBarType>(IsCurrent, Read);
    }

    private bool IsCurrent()
    {
        if (!document.IsValidObject) return false;
        var active = context.App.ActiveUIDocument?.Document;
        return active != null && active.IsValidObject && document.Equals(active);
    }
    public void EnsureCurrent()
    {
        try { scope.EnsureCurrent(); }
        catch (TypeSelectionFailure ex) { throw Configuration(ex); }
    }
    private static RebarConfigurationIssue Configuration(TypeSelectionFailure ex) => new(ex.Code, ex.Message, ex);
    private static TypeStateData MissingData(int id) => new(id, "", "", "", double.NaN, double.NaN, double.NaN, double.NaN);

    private LiveType<RebarBarType>? Read(int id)
    {
        try { return ReadNative(id); }
        catch (RebarConfigurationIssue) { throw; }
        catch (Exception ex) { throw new RebarConfigurationIssue("TYPE_READ", "Revit API không đọc được Type ID " + id + " từ dự án hiện tại. Chọn lại type. " + ex.Message, ex); }
    }

    private LiveType<RebarBarType>? ReadNative(int id)
    {
        var element = document.GetElement(new ElementId(id));
        if (element == null) return null;
        // Test lifetime before reading Document, UniqueId or any property.
        if (!element.IsValidObject) return new(null, MissingData(id), valid: false);
        if (!(element is RebarBarType type)) return new(null, MissingData(id), barType: false);
        var owner = type.Document;
        if (!owner.IsValidObject || !owner.Equals(document)) return new(type, MissingData(id), ownerMatches: false);
        var data = new TypeStateData(type.Id.IntegerValue, type.UniqueId, Revision(type), type.Name,
            RebarTemplates.NominalDiameter(type) * 304.8, RebarTemplates.Diameter(type) * 304.8, type.StandardBendDiameter * 304.8, type.StirrupTieBendDiameter * 304.8);
        return new(type, data);
    }

    private static string Revision(RebarBarType type)
    {
        // VersionGuid alone does not change at every transaction in Revit 2023.
        // Snapshot live type parameters too, without retaining any Parameter wrappers.
        var values = type.Parameters.Cast<Parameter>().OrderBy(p => p.Id.IntegerValue).Select(p =>
        {
            string value;
            switch (p.StorageType)
            {
                case StorageType.Double: value = p.AsDouble().ToString("R", CultureInfo.InvariantCulture); break;
                case StorageType.Integer: value = p.AsInteger().ToString(CultureInfo.InvariantCulture); break;
                case StorageType.ElementId: value = p.AsElementId().IntegerValue.ToString(CultureInfo.InvariantCulture); break;
                case StorageType.String: value = p.AsString() ?? ""; break;
                default: value = ""; break;
            }
            return p.Id.IntegerValue + ":" + p.StorageType + ":" + value.Length + ":" + value;
        });
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", values)));
        return type.VersionGuid.ToString("D") + "/" + BitConverter.ToString(hash).Replace("-", "");
    }

    public IList<TypeChoice> Collect(bool stirrup, string role)
    {
        EnsureCurrent(); var choices = new List<TypeChoice>();
        foreach (var id in new FilteredElementCollector(document).OfClass(typeof(RebarBarType)).WhereElementIsElementType().ToElementIds())
        {
            try { var choice = scope.Capture(id.IntegerValue); scope.Resolve(choice, stirrup); choices.Add(choice); }
            catch (TypeSelectionFailure ex) { Rejected.Add(role + " ID " + id.IntegerValue + ": " + ex.Code + " " + ex.Message); }
            catch (RebarConfigurationIssue ex) { Rejected.Add(role + " ID " + id.IntegerValue + ": " + ex.Message); }
        }
        if (choices.Count == 0) throw new RebarConfigurationIssue("NO_VALID_TYPES", "Dự án " + document.Title + " không có Rebar Bar Type hợp lệ cho " + role + ". Nạp hoặc tạo loại thép trong dự án này (Structure → Reinforcement → Rebar → Edit Type/Duplicate), kiểm tra đường kính và đường kính uốn, rồi chạy lại."
            + (Rejected.Count > 0 ? "\n" + string.Join("\n", Rejected.Take(6)) : ""));
        return choices.OrderBy(t => t.NominalMm).ThenBy(t => t.Name).ThenBy(t => t.ElementId).ToList();
    }

    public TypeChoice Capture(RebarBarType type)
    {
        EnsureCurrent();
        if (type == null || !type.IsValidObject) throw new RebarConfigurationIssue("INVALID_OBJECT", "Không giữ lại type đã mất hiệu lực sau transaction.");
        if (!type.Document.Equals(document)) throw new RebarConfigurationIssue("FOREIGN_DOCUMENT", "Không nhận type của dự án khác.");
        try
        {
            var choice = scope.Capture(type.Id.IntegerValue);
            if (choice.UniqueId != type.UniqueId) throw new RebarConfigurationIssue("REPLACED_ID", "ElementId không còn trỏ tới type được yêu cầu.");
            return choice;
        }
        catch (TypeSelectionFailure ex) { throw Configuration(ex); }
    }

    public RebarBarType Resolve(TypeChoice choice, bool stirrup = false)
    {
        try { return RebarTemplates.Validate(scope.Resolve(choice, stirrup), document, stirrup); }
        catch (TypeSelectionFailure ex) { throw Configuration(ex); }
    }

    public T ResolveElement<T>(int id, string uniqueId, string role, string errorKind = "REBAR_API") where T : Element
    {
        EnsureCurrent(); var element = document.GetElement(new ElementId(id));
        if (element == null || !element.IsValidObject) throw new RebarIssue(errorKind, role + " ID " + id + " đã bị xóa hoặc hoàn tác.");
        if (!(element is T typed) || element.UniqueId != uniqueId || !element.Document.Equals(document))
            throw new RebarIssue(errorKind, role + " ID " + id + " không còn đúng định danh, lớp hoặc Document.");
        return typed;
    }

    public void Preflight(RebarLayoutSettings settings, bool hasCage, bool hasGrid, Action<string>? log = null)
    {
        EnsureCurrent();
        var selected = new List<(string Role, TypeChoice Choice, bool Stirrup)> { ("Thép dọc/chính", settings.Main, false) };
        if (hasCage) selected.Add(("Thép đai", settings.Tie, true));
        if (hasGrid) selected.Add(("Thép phân bố", settings.Distribution, false));
        IReadOnlyList<string> failures;
        try { failures = scope.Preflight(selected); }
        catch (TypeSelectionFailure ex) { throw Configuration(ex); }
        if (failures.Count > 0) throw new RebarConfigurationIssue("PREFLIGHT", string.Join("\n\n", failures) + "\n\nDừng cấu hình chung; mở lại lệnh để chọn type. Không thử cùng type lỗi cho từng cấu kiện.");
        foreach (var item in selected)
        {
            var type = Resolve(item.Choice, item.Stirrup);
            log?.Invoke(item.Role + ": ID=" + item.Choice.ElementId + "; UniqueId=" + item.Choice.UniqueId + "; version=" + item.Choice.Revision + "; " + Label(item.Choice)
                + "; owner.Equals(current)=" + type.Document.Equals(document) + "; ReferenceEquals=" + ReferenceEquals(type.Document, document));
        }
    }

    public static string Label(TypeChoice choice) => choice.Name + " · Ø" + ToolContext.N(choice.NominalMm) + " mm · ID " + choice.ElementId;

    public void Dispose() => scope.Dispose();
}
