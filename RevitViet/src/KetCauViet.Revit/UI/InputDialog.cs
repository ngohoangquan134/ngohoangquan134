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
    public Field(string key, string label, string value = "", string[]? options = null)
    { Key = key; Label = label; Value = value; Options = options; }
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
        ClientSize = new Size(620, 130 + fields.Length * 48);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
        var note = new Label { Text = description, AutoSize = true, MaximumSize = new Size(570, 0), Margin = new Padding(0, 0, 0, 16) };
        layout.Controls.Add(note, 0, 0); layout.SetColumnSpan(note, 2);
        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            layout.Controls.Add(new Label { Text = field.Label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, i + 1);
            Control control;
            if (field.Options != null)
            {
                if (field.Options.Length == 0) throw new InvalidOperationException("Không có lựa chọn cho: " + field.Label);
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
                combo.Items.AddRange(field.Options); combo.SelectedIndex = 0; control = combo;
            }
            else control = new TextBox { Text = field.Value, Dock = DockStyle.Fill };
            controls.Add(field.Key, control); labels.Add(field.Key, field.Label); layout.Controls.Add(control, 1, i + 1);
        }
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "Thực hiện", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 0, fields.Length + 1); layout.SetColumnSpan(buttons, 2);
        AcceptButton = ok; CancelButton = cancel; Controls.Add(layout);
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
