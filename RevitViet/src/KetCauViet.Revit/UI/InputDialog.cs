using System.Drawing;
using System.Windows.Forms;
using Autodesk.Revit.UI;
using KetCauViet.Core;
using Form = System.Windows.Forms.Form;
using ComboBox = System.Windows.Forms.ComboBox;
using TextBox = System.Windows.Forms.TextBox;

namespace KetCauViet.Revit.UI;

internal sealed class Field
{
    public string Key { get; }
    public string Label { get; }
    public string Value { get; }
    public string[]? Options { get; }
    public string Group { get; }
    public Field(string key, string label, string value = "", string[]? options = null, string group = "Thông số")
    { Key = key; Label = label; Value = value; Options = options; Group = group; }
}

internal sealed class InputDialog : Form
{
    private readonly Dictionary<string, Control> controls = new();
    private readonly Dictionary<string, string> labels = new();
    private InputDialog(string title, string description, Field[] fields)
    {
        Text = "Kết Cấu Việt · " + title;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false; MaximizeBox = false;
        var largest = fields.GroupBy(f => f.Group).Select(g => g.Count()).DefaultIfEmpty(1).Max();
        ClientSize = new Size(780, Math.Min(Screen.PrimaryScreen.WorkingArea.Height - 100, 180 + largest * 52));
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 6) };
        foreach (var group in fields.GroupBy(f => f.Group))
        {
            var page = new TabPage(group.Key) { AutoScroll = true, Padding = new Padding(6) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            var row = 0;
            foreach (var field in group)
            {
                layout.Controls.Add(new Label { Text = field.Label, AutoSize = true, MaximumSize = new Size(380, 0), Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 8) }, 0, row);
                Control control;
                if (field.Options != null)
                {
                    if (field.Options.Length == 0) throw new InvalidOperationException("Không có lựa chọn cho: " + field.Label);
                    var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, DropDownWidth = 690 };
                    combo.Items.AddRange(field.Options); combo.SelectedIndex = Math.Max(0, Array.IndexOf(field.Options, field.Value)); control = combo;
                }
                else control = new TextBox { Text = field.Value, Dock = DockStyle.Fill };
                control.Margin = new Padding(3, 8, 3, 8); controls.Add(field.Key, control); labels.Add(field.Key, field.Label);
                layout.Controls.Add(control, 1, row++);
            }
            page.Controls.Add(layout); tabs.TabPages.Add(page);
        }
        var note = new Label { Text = description, AutoSize = true, MaximumSize = new Size(780, 0), Dock = DockStyle.Top, Padding = new Padding(18, 12, 18, 12) };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Height = 58, Dock = DockStyle.Bottom, Padding = new Padding(12) };
        var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "Thực hiện", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        AcceptButton = ok; CancelButton = cancel; Controls.Add(tabs); Controls.Add(buttons); Controls.Add(note);
    }

    public string TextValue(string key) => controls[key].Text.Trim();
    public int Index(string key) => ((ComboBox)controls[key]).SelectedIndex;
    public double Number(string key, double min = 0, double max = double.MaxValue)
    {
        var number = Engineering.ParseNumber(TextValue(key));
        if (number < min || number > max) throw new ArgumentException($"{labels[key]}: giá trị phải từ {min} đến {max}.");
        return number;
    }
    public int Integer(string key, int min = 1, int max = 10000)
    {
        var n = Number(key, min, max);
        if (n != Math.Truncate(n)) throw new ArgumentException(labels[key] + ": cần nhập số nguyên.");
        return (int)n;
    }

    public static InputDialog Ask(UIApplication app, string title, string description, params Field[] fields)
    {
        var dialog = new InputDialog(title, description, fields);
        if (dialog.ShowDialog(new WindowOwner(app.MainWindowHandle)) != DialogResult.OK)
        { dialog.Dispose(); throw new System.OperationCanceledException(); }
        return dialog;
    }

    internal sealed class WindowOwner : IWin32Window
    {
        public IntPtr Handle { get; }
        public WindowOwner(IntPtr handle) => Handle = handle;
    }
}
