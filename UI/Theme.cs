using System.Runtime.InteropServices;

namespace FractalAnimator.UI;

/// <summary>
/// A small dark theme for the Windows Forms tree. Colors are applied recursively, with per-control
/// styling for the widgets the app uses, plus a dark title bar on Windows 10/11.
/// </summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(30, 30, 33);
    public static readonly Color Surface = Color.FromArgb(43, 43, 48);
    public static readonly Color SurfaceAlt = Color.FromArgb(37, 37, 41);
    public static readonly Color Border = Color.FromArgb(64, 64, 70);
    public static readonly Color Text = Color.FromArgb(228, 228, 230);
    public static readonly Color TextDim = Color.FromArgb(150, 150, 156);
    public static readonly Color Accent = Color.FromArgb(86, 156, 214);
    public static readonly Color AccentText = Color.White;
    public static readonly Color Good = Color.FromArgb(106, 191, 105);
    public static readonly Color Bad = Color.FromArgb(224, 108, 108);

    public static void Apply(Control control)
    {
        StyleOne(control);
        foreach (Control child in control.Controls)
            Apply(child);
    }

    static void StyleOne(Control control)
    {
        switch (control)
        {
            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Accent;
                button.BackColor = Surface;
                button.ForeColor = Text;
                break;
            case CheckBox checkBox:
                checkBox.ForeColor = Text;
                checkBox.BackColor = Color.Transparent;
                break;
            case TextBox textBox:
                textBox.BackColor = Surface;
                textBox.ForeColor = Text;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox comboBox:
                comboBox.BackColor = Surface;
                comboBox.ForeColor = Text;
                comboBox.FlatStyle = FlatStyle.Flat;
                break;
            case NumericUpDown numeric:
                numeric.BackColor = Surface;
                numeric.ForeColor = Text;
                numeric.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ListBox listBox:
                listBox.BackColor = SurfaceAlt;
                listBox.ForeColor = Text;
                listBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case Label label:
                label.ForeColor = Text;
                label.BackColor = Color.Transparent;
                break;
            case MenuStrip menu:
                menu.BackColor = SurfaceAlt;
                menu.ForeColor = Text;
                menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
                foreach (ToolStripItem item in menu.Items) StyleMenuItem(item);
                break;
            case TabControl tab:
                tab.DrawMode = TabDrawMode.OwnerDrawFixed;
                tab.SizeMode = TabSizeMode.Fixed;
                tab.ItemSize = new Size(122, 26);
                tab.DrawItem -= DrawDarkTab;
                tab.DrawItem += DrawDarkTab;
                foreach (TabPage page in tab.TabPages) page.BackColor = Background;
                break;
            case ProgressBar:
                break;
            default:
                control.ForeColor = Text;
                if (control.Tag as string != "card")
                    control.BackColor = Background;
                break;
        }
    }

    static void DrawDarkTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tab || e.Index < 0) return;
        var selected = e.Index == tab.SelectedIndex;
        using var back = new SolidBrush(selected ? Accent : Surface);
        e.Graphics.FillRectangle(back, e.Bounds);
        TextRenderer.DrawText(e.Graphics, tab.TabPages[e.Index].Text, tab.Font, e.Bounds,
            selected ? AccentText : Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    static void StyleMenuItem(ToolStripItem item)
    {
        item.ForeColor = Text;
        item.BackColor = SurfaceAlt;
        if (item is ToolStripMenuItem menuItem)
            foreach (ToolStripItem child in menuItem.DropDownItems)
                StyleMenuItem(child);
    }

    public static void DarkTitleBar(Form form)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var enabled = 1;
            // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (Windows 10 2004+ / 11).
            DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Accent;
        public override Color MenuItemSelectedGradientBegin => Accent;
        public override Color MenuItemSelectedGradientEnd => Accent;
        public override Color MenuItemBorder => Accent;
        public override Color MenuBorder => Border;
        public override Color ToolStripDropDownBackground => SurfaceAlt;
        public override Color ImageMarginGradientBegin => SurfaceAlt;
        public override Color ImageMarginGradientMiddle => SurfaceAlt;
        public override Color ImageMarginGradientEnd => SurfaceAlt;
        public override Color MenuItemPressedGradientBegin => SurfaceAlt;
        public override Color MenuItemPressedGradientEnd => SurfaceAlt;
    }
}
