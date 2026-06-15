using System.ComponentModel;

namespace FractalAnimator.UI;

/// <summary>
/// A lightweight code editor: a monospace text box with a synced line-number gutter. Used for the live
/// fractal-formula editor so writing and debugging an algorithm feels like an editor, not a label.
/// </summary>
public sealed class CodeEditor : UserControl
{
    readonly GutterTextBox _textBox;
    readonly Panel _gutter;

    public CodeEditor()
    {
        BackColor = Theme.SurfaceAlt;
        _gutter = new Panel { Dock = DockStyle.Left, Width = 46, BackColor = Theme.SurfaceAlt };
        _gutter.Paint += PaintGutter;
        _textBox = new GutterTextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsTab = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            BorderStyle = BorderStyle.None,
            Font = new Font("Cascadia Mono", 9.5f, FontStyle.Regular, GraphicsUnit.Point),
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
        };
        if (_textBox.Font.Name != "Cascadia Mono")
            _textBox.Font = new Font("Consolas", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        _textBox.RepaintGutter += () => _gutter.Invalidate();
        _textBox.TextChanged += (s, e) => CodeChanged?.Invoke(this, e);
        Controls.Add(_textBox);
        Controls.Add(_gutter);
    }

    /// <summary>Raised whenever the code text changes (for live recompile/preview).</summary>
    public event EventHandler? CodeChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Code
    {
        // The native Win32 edit control only breaks lines on CRLF, so normalize on the way in.
        get => _textBox.Text;
        set => _textBox.Text = value.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
    }

    void PaintGutter(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.SurfaceAlt);
        var firstChar = _textBox.GetCharIndexFromPosition(new Point(1, 1));
        var firstLine = _textBox.GetLineFromCharIndex(firstChar);
        var lineHeight = Math.Max(1, _textBox.Font.Height);
        var totalLines = _textBox.Lines.Length == 0 ? 1 : _textBox.Lines.Length;
        using var brush = new SolidBrush(Theme.TextDim);
        using var font = new Font(_textBox.Font.FontFamily, _textBox.Font.Size, FontStyle.Regular, GraphicsUnit.Point);
        var visible = _gutter.Height / lineHeight + 1;
        for (var i = 0; i <= visible; i++)
        {
            var line = firstLine + i;
            if (line >= totalLines) break;
            var charIndex = _textBox.GetFirstCharIndexFromLine(line);
            if (charIndex < 0) continue;
            var y = _textBox.GetPositionFromCharIndex(charIndex).Y;
            e.Graphics.DrawString((line + 1).ToString(), font, brush, new RectangleF(0, y, _gutter.Width - 6, lineHeight),
                new StringFormat { Alignment = StringAlignment.Far });
        }
    }

    /// <summary>A text box that signals when it scrolls or repaints so the gutter can follow.</summary>
    sealed class GutterTextBox : TextBox
    {
        public event Action? RepaintGutter;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // WM_PAINT, WM_HSCROLL, WM_VSCROLL, WM_KEYUP, WM_LBUTTONUP, WM_MOUSEWHEEL.
            if (m.Msg is 0x000F or 0x0114 or 0x0115 or 0x0101 or 0x0202 or 0x020A)
                RepaintGutter?.Invoke();
        }
    }
}
