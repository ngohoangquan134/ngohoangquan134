using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using KetCauViet.Core;
using KetCauViet.Revit.UI;

namespace KetCauViet.Revit.Infrastructure;

internal sealed class ToolContext
{
    public UIApplication App { get; }
    public UIDocument Ui => App.ActiveUIDocument;
    public Document Doc => Ui.Document;
    public ToolContext(UIApplication app) => App = app;
    public static string N(double number) => number.ToString("0.###", CultureInfo.GetCultureInfo("vi-VN"));
    public static string Id(Element element) => element.Id.IntegerValue.ToString(CultureInfo.InvariantCulture);

    public IList<Element> Select(string prompt, Func<Element, bool>? predicate = null)
    {
        predicate ??= _ => true;
        var existing = Ui.Selection.GetElementIds().Select(Doc.GetElement).Where(predicate).ToList();
        if (existing.Count > 0) return existing;
        return Ui.Selection.PickObjects(ObjectType.Element, new Filter(predicate), prompt)
            .Select(r => Doc.GetElement(r.ElementId)).GroupBy(e => e.Id).Select(g => g.First()).ToList();
    }

    public void Write(string name, Action action)
    {
        using var tx = new Transaction(Doc, "KCV · " + name);
        tx.Start(); action();
        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không hoàn tất giao dịch. Thay đổi chưa được lưu.");
    }

    public void Batch(string name, IEnumerable<Element> elements, Action<Element> action)
    {
        var done = 0;
        var errors = new List<string>();
        Write(name, () =>
        {
            foreach (var element in elements)
            {
                using var sub = new SubTransaction(Doc);
                sub.Start();
                try
                {
                    action(element);
                    if (sub.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Giao dịch con bị hoàn tác.");
                    done++;
                }
                catch (Exception ex)
                {
                    if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                    errors.Add("ID " + Id(element) + ": " + ex.Message);
                }
            }
        });
        Summary(name, $"Hoàn thành: {done}. Bỏ qua/lỗi: {errors.Count}.", errors);
    }

    public void Summary(string title, string content, IEnumerable<string>? details = null)
    {
        new TaskDialog("Kết Cấu Việt · " + title)
        { MainInstruction = title, MainContent = content, ExpandedContent = details == null ? "" : string.Join(Environment.NewLine, details), CommonButtons = TaskDialogCommonButtons.Close }.Show();
    }

    public void Report(string title, string summary, IEnumerable<IEnumerable<string>> rows)
    {
        var dialog = new TaskDialog("Kết Cấu Việt · " + title) { MainInstruction = title, MainContent = summary, CommonButtons = TaskDialogCommonButtons.Close };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Lưu báo cáo CSV (Excel, UTF-8)");
        if (dialog.Show() != TaskDialogResult.CommandLink1) return;
        using var save = new SaveFileDialog { Title = "Lưu " + title, Filter = "CSV (*.csv)|*.csv", FileName = "KetCauViet-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv", AddExtension = true };
        if (save.ShowDialog(new InputDialog.WindowOwner(App.MainWindowHandle)) != DialogResult.OK) return;
        File.WriteAllText(save.FileName, Engineering.Csv(rows), new UTF8Encoding(true));
        Summary(title, "Đã lưu báo cáo:\n" + save.FileName);
    }

    public T Choose<T>(string title, IEnumerable<T> items, Func<T, string> label) where T : Element
    {
        var list = items.OrderBy(label).ToList();
        if (list.Count == 0) throw new InvalidOperationException("Chưa có kiểu phù hợp. Hãy nạp family/type trước.");
        using var input = InputDialog.Ask(App, title, "Chọn kiểu đã có trong dự án.", new Field("type", "Kiểu cấu kiện", options: list.Select(e => label(e) + " [" + Id(e) + "]").ToArray()));
        return list[input.Index("type")];
    }

    public static string TypeName(Element element) => element is FamilySymbol symbol ? symbol.FamilyName + " : " + symbol.Name : element.Name;
    public static bool Category(Element e, params BuiltInCategory[] categories) => e.Category != null && categories.Any(c => e.Category.Id.IntegerValue == (int)c);
    public static string Mark(Element e) => e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";

    private sealed class Filter : ISelectionFilter
    {
        private readonly Func<Element, bool> predicate;
        public Filter(Func<Element, bool> predicate) => this.predicate = predicate;
        public bool AllowElement(Element element) => predicate(element);
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
