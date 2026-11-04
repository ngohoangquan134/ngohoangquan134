using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using KetCauViet.Revit.Infrastructure;

namespace KetCauViet.Revit.Tools;

internal sealed class RebarRunLog : IDisposable
{
    public string Path { get; }
    private readonly StreamWriter writer;
    public RebarRunLog(string tool)
    {
        var root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KetCauViet", "Logs");
        Directory.CreateDirectory(root);
        Path = System.IO.Path.Combine(root, "rebar-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".log");
        writer = new StreamWriter(Path, false, new UTF8Encoding(true)) { AutoFlush = true };
        Write("COMMAND " + tool + "; Revit " + Documentation.Version + "; KCV rebar 2.0; times=" + DateTimeOffset.Now.ToString("O"));
    }
    public void Write(string message) => writer.WriteLine(DateTimeOffset.Now.ToString("O") + " " + message);
    public void Dispose() => writer.Dispose();
}

internal static class RebarExecution
{
    public static void Run(ToolContext c, string title, IList<Element> elements, string settings, Action<Element, Action<string>> action, bool verifyOnly = false)
    {
        using var log = new RebarRunLog(title);
        log.Write("Project=" + c.Doc.Title + "; " + settings);
        var done = 0; var skipped = 0; var failures = new List<(ElementId Id, string Reason)>();
        using var group = new TransactionGroup(c.Doc, "KCV · " + title);
        group.Start();
        foreach (var element in elements)
        {
            var id = ToolContext.Id(element);
            log.Write("BEGIN ID=" + id + "; category=" + element.Category?.Name + "; type=" + element.Name + "; typeId=" + element.GetTypeId().IntegerValue);
            using var tx = new Transaction(c.Doc, "KCV · " + title + " · ID " + id);
            var processor = new Failures(log, id);
            try
            {
                tx.Start();
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(processor).SetClearAfterRollback(true).SetForcedModalHandling(false));
                action(element, text => log.Write("ID=" + id + " " + text));
                c.Doc.Regenerate();
                var status = tx.Commit();
                if (status != TransactionStatus.Committed) throw new RebarIssue("REBAR_API", processor.Messages.Count > 0 ? string.Join(" | ", processor.Messages) : "Revit không commit giao dịch cấu kiện: " + status);
                done++; log.Write("SUCCESS ID=" + id);
            }
            catch (Exception ex)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                var code = (ex as RebarIssue)?.Kind ?? "REBAR_API";
                if (code == "EXISTING") skipped++;
                else failures.Add((element.Id, "ID " + id + " [" + code + "] " + ex.Message));
                log.Write((code == "EXISTING" ? "SKIP" : "ERROR") + " ID=" + id + " [" + code + "] " + ex);
            }
        }
        if (done > 0 && !verifyOnly)
        {
            if (group.Assimilate() != TransactionStatus.Committed) throw new RebarIssue("REBAR_API", "Không lưu được nhóm giao dịch bố trí thép. Xem log: " + log.Path);
        }
        else group.RollBack();
        log.Write($"RESULT verified={done}; saved={(verifyOnly ? 0 : done)}; skipped={skipped}; failed={failures.Count}; verifyOnly={verifyOnly}");
        var main = (verifyOnly ? $"Kiểm tra đạt: {done}. Không lưu thép mới hoặc thay đổi model." : $"Hoàn thành: {done}.") + $" Giữ nguyên host đã có thép: {skipped}. Lỗi: {failures.Count}.";
        if (failures.Count > 0) main += "\n\n" + string.Join("\n\n", failures.Take(4).Select(f => f.Reason));
        main += "\n\nLog chi tiết: " + log.Path;
        var dialog = new TaskDialog("Kết Cấu Việt · " + title) { MainInstruction = title, MainContent = main, ExpandedContent = string.Join("\n", failures.Select(f => f.Reason)), CommonButtons = TaskDialogCommonButtons.Close };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Mở log chi tiết");
        if (failures.Count > 0) dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Chọn các cấu kiện bị lỗi");
        var result = dialog.Show();
        if (result == TaskDialogResult.CommandLink1) Process.Start(new ProcessStartInfo(log.Path) { UseShellExecute = true });
        if (result == TaskDialogResult.CommandLink2) c.Ui.Selection.SetElementIds(failures.Select(f => f.Id).ToList());
    }

    private sealed class Failures : IFailuresPreprocessor
    {
        private readonly RebarRunLog log; private readonly string id;
        public List<string> Messages { get; } = new();
        public Failures(RebarRunLog log, string id) { this.log = log; this.id = id; }
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var message in accessor.GetFailureMessages())
            {
                var text = message.GetSeverity() + ": " + message.GetDescriptionText();
                Messages.Add(text); log.Write("REVIT_FAILURE ID=" + id + " " + text + "; elements=" + string.Join(",", message.GetFailingElementIds().Select(x => x.IntegerValue)));
            }
            // Keep warnings/errors visible in the log and roll back this host. Never
            // suppress assertions or commit a cage after Revit reported a failure.
            return Messages.Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
