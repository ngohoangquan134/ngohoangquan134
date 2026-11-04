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
        Write("COMMAND " + tool + "; Revit " + Documentation.Version + "; KCV rebar 2.0.1; times=" + DateTimeOffset.Now.ToString("O"));
    }
    public void Write(string message) => writer.WriteLine(DateTimeOffset.Now.ToString("O") + " " + message);
    public void Dispose() => writer.Dispose();
}

internal static class RebarExecution
{
    public static void ReportConfiguration(ToolContext c, string title, RebarConfigurationIssue issue, string settings = "")
    {
        using var log = new RebarRunLog(title);
        log.Write("CONFIGURATION_STOP before transactions; project=" + (c.Doc.IsValidObject ? c.Doc.Title : "đã đóng") + "; " + settings);
        log.Write(issue.ToString()); ShowConfiguration(title, issue, log.Path, 0);
    }

    private static void ShowConfiguration(string title, RebarConfigurationIssue issue, string path, int processed)
    {
        var dialog = new TaskDialog("Kết Cấu Việt · Cấu hình loại thép")
        {
            MainInstruction = "Đã dừng trước khi tiếp tục bố trí",
            MainContent = "[BAR_TYPE] " + issue.Message + "\n\nChưa lưu kết quả của lần chạy này. Đã hoàn tác toàn bộ thay đổi mới nếu batch đã bắt đầu. Thép cũ được giữ nguyên.\nSố host đã kiểm tra trước khi dừng: " + processed + "\n\nLog: " + path,
            CommonButtons = TaskDialogCommonButtons.Close
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Mở log kiểm tra cấu hình");
        if (dialog.Show() == TaskDialogResult.CommandLink1) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public static void Run(ToolContext c, string title, IList<Element> elements, string settings, Action<Element, Action<string>> action,
        bool verifyOnly = false, Action<Action<string>>? preflight = null)
    {
        using var log = new RebarRunLog(title);
        log.Write("Project=" + (c.Doc.IsValidObject ? c.Doc.Title : "đã đóng") + "; " + settings);
        try { preflight?.Invoke(log.Write); }
        catch (RebarConfigurationIssue ex)
        {
            log.Write("CONFIGURATION_STOP before TransactionGroup; plannedHosts=" + elements.Count + "; " + ex);
            ShowConfiguration(title, ex, log.Path, 0); return;
        }

        // Freeze only scoped identifiers. Refresh host wrappers after every rollback too.
        var failures = new List<(ElementId Id, string Reason)>();
        var hostKeys = new List<(int Id, string UniqueId)>();
        foreach (var e in elements)
        {
            var id = ElementId.InvalidElementId;
            try
            {
                if (!e.IsValidObject) throw new RebarIssue("HOST", "Lựa chọn host đã mất hiệu lực. Hãy chọn lại cấu kiện.");
                id = e.Id;
                if (!e.Document.Equals(c.Doc)) throw new RebarIssue("HOST", "Host thuộc Document khác.");
                hostKeys.Add((id.IntegerValue, e.UniqueId));
            }
            catch (Exception ex)
            {
                var reason = "ID " + (id.IntegerValue == -1 ? "không truy xuất được" : id.IntegerValue.ToString(CultureInfo.InvariantCulture)) + " [HOST] " + ex.Message;
                failures.Add((id, reason)); log.Write("ERROR " + reason + "; " + ex);
            }
        }
        var done = 0; var skipped = 0; var processed = failures.Count;
        RebarConfigurationIssue? common = null;
        using var group = new TransactionGroup(c.Doc, "KCV · " + title); group.Start();
        foreach (var key in hostKeys)
        {
            try { preflight?.Invoke(log.Write); }
            catch (RebarConfigurationIssue ex) { common = ex; break; }
            var id = key.Id.ToString(CultureInfo.InvariantCulture);
            using var tx = new Transaction(c.Doc, "KCV · " + title + " · ID " + id);
            var processor = new Failures(log, id);
            try
            {
                var element = c.Doc.GetElement(new ElementId(key.Id));
                if (element == null || !element.IsValidObject || element.UniqueId != key.UniqueId) throw new RebarIssue("HOST", "Host đã bị xóa hoặc ElementId trỏ tới phần tử khác.");
                log.Write("BEGIN ID=" + id + "; category=" + element.Category?.Name + "; type=" + element.Name + "; typeId=" + element.GetTypeId().IntegerValue);
                tx.Start();
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(processor).SetClearAfterRollback(true).SetForcedModalHandling(false));
                action(element, text => log.Write("ID=" + id + " " + text));
                c.Doc.Regenerate(); var status = tx.Commit();
                if (status != TransactionStatus.Committed) throw new RebarIssue("REBAR_API", processor.Messages.Count > 0 ? string.Join(" | ", processor.Messages) : "Revit không commit host: " + status);
                done++; processed++; log.Write("SUCCESS ID=" + id);
            }
            catch (RebarConfigurationIssue ex)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                log.Write("CONFIGURATION_STOP ID=" + id + "; " + ex); common = ex; break;
            }
            catch (Exception ex)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); processed++;
                var code = (ex as RebarIssue)?.Kind ?? "REBAR_API";
                if (code == "EXISTING") skipped++; else failures.Add((new ElementId(key.Id), "ID " + id + " [" + code + "] " + ex.Message));
                log.Write((code == "EXISTING" ? "SKIP" : "ERROR") + " ID=" + id + " [" + code + "] " + ex);
            }
        }
        if (common != null)
        {
            group.RollBack(); log.Write("RESULT configurationStop; verified=" + done + "; saved=0; remaining=" + (elements.Count - processed) + "; " + common);
            ShowConfiguration(title, common, log.Path, processed); return;
        }
        if (done > 0 && !verifyOnly)
        {
            if (group.Assimilate() != TransactionStatus.Committed) throw new RebarIssue("REBAR_API", "Không lưu được nhóm giao dịch. Xem log: " + log.Path);
        }
        else group.RollBack();
        log.Write($"RESULT verified={done}; saved={(verifyOnly ? 0 : done)}; skipped={skipped}; failed={failures.Count}; verifyOnly={verifyOnly}");
        var main = (verifyOnly ? $"Kiểm tra đạt: {done}. Không lưu thép mới hoặc thay đổi model." : $"Hoàn thành: {done}.") + $" Giữ nguyên host đã có thép: {skipped}. Lỗi: {failures.Count}.";
        if (failures.Count > 0) main += "\n\n" + string.Join("\n\n", failures.Take(4).Select(f => f.Reason));
        main += "\n\nLog chi tiết: " + log.Path;
        var summary = new TaskDialog("Kết Cấu Việt · " + title) { MainInstruction = title, MainContent = main, ExpandedContent = string.Join("\n", failures.Select(f => f.Reason)), CommonButtons = TaskDialogCommonButtons.Close };
        summary.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Mở log chi tiết");
        if (failures.Count > 0) summary.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Chọn các cấu kiện bị lỗi");
        var result = summary.Show();
        if (result == TaskDialogResult.CommandLink1) Process.Start(new ProcessStartInfo(log.Path) { UseShellExecute = true });
        if (result == TaskDialogResult.CommandLink2) c.Ui.Selection.SetElementIds(failures.Where(f => f.Id.IntegerValue != -1 && c.Doc.GetElement(f.Id) != null).Select(f => f.Id).ToList());
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
