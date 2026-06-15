using System.Globalization;
using System.Text.Json.Nodes;
using FractalAnimator.Core;
using FractalAnimator.Interp;
using FractalAnimator.Keyframes;
using FractalAnimator.Rendering;

namespace FractalAnimator.UI;

// A label-on-the-left, field-on-the-right row. Backed by a 2-column TableLayoutPanel so the field
// never overlaps the label (plain Dock.Left + Dock.Fill is z-order fragile and can overlap).
public class BinaryPanel : Panel
{
    readonly TableLayoutPanel _layout;
    public BinaryPanel()
    {
        Height = 32;
        _layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(_layout);
    }
    public void AddLeft(Control c) { c.Anchor = AnchorStyles.Left; _layout.Controls.Add(c, 0, 0); }
    public void AddRight(Control c) { c.Dock = DockStyle.Fill; _layout.Controls.Add(c, 1, 0); }
}

public abstract class OptionConfigure<T> : UserControl, IExportable
{
    public abstract T Get();
    public abstract void Init();
    public abstract JsonObject ExportJson();
    public abstract void ImportJson(JsonObject obj);
}

public abstract class ImageLoaderConfigure : OptionConfigure<ImageLoader?>
{
    Func<Task>? _onLoad;
    public void SetOnLoadCallable(Func<Task> cb) => _onLoad = cb;
    protected Task OnLoadAsync() => _onLoad?.Invoke() ?? Task.CompletedTask;
}

public abstract class InterpolatorConfigure<T> : OptionConfigure<Interpolator> where T : Interpolator
{
    protected List<KeyPoint> _points = []; protected ListBox _list = new();
    protected FlowLayoutPanel _inputPanel = new(); readonly Dictionary<string, LabeledField> _fields = new(StringComparer.Ordinal);

    public override void Init()
    {
        Controls.Clear(); Dock = DockStyle.Fill;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize));
        Controls.Add(root);
        _inputPanel = new() { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        root.Controls.Add(_inputPanel, 0, 0);
        AddField("time", 0); AddField("image", 0);
        _points = []; _list.Dock = DockStyle.Fill;
        _list.DoubleClick += (_, _) => { if (_list.SelectedItem is KeyPoint k) foreach (var d in k.GetData()) SetField(d.Key, d.Value.ToString(CultureInfo.InvariantCulture)); };
        root.Controls.Add(_list, 0, 1); Sync();
        var btnP = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var addB = new Button { Text = Localization.Get("op.insert") }; addB.Click += (_, _) => { _points.Add(new(GetMap())); Sync(); }; btnP.Controls.Add(addB);
        var delB = new Button { Text = Localization.Get("op.delete") }; delB.Click += (_, _) => { var i = _list.SelectedIndex; if (i >= 0) { _points.RemoveAt(i); Sync(); } }; btnP.Controls.Add(delB);
        var modB = new Button { Text = Localization.Get("op.update") }; modB.Click += (_, _) => { var i = _list.SelectedIndex; if (i >= 0) { _points.RemoveAt(i); _points.Insert(i, new(GetMap())); Sync(); } }; btnP.Controls.Add(modB);
        root.Controls.Add(btnP, 0, 2);
    }
    protected Dictionary<string, double> GetMap() => _fields.ToDictionary(e => e.Key, e => double.Parse(e.Value.Field.Text, CultureInfo.InvariantCulture), StringComparer.Ordinal);
    protected void Sync() { _points.Sort(); _list.Items.Clear(); foreach (var p in _points) _list.Items.Add(p); }
    protected void AddField(string k, double d) { var f = new LabeledField(Localization.Get("indicator.key." + k)); f.Field.Text = d.ToString(CultureInfo.InvariantCulture); _fields[k] = f; _inputPanel.Controls.Add(f); }
    protected void SetField(string k, string v) { _fields[k].Field.Text = v; }
    protected void AddDefault() { _points.Add(new(GetMap())); Sync(); }

    public override JsonObject ExportJson()
    {
        var arr = new JsonArray(); foreach (var p in _points) { var o = new JsonObject(); foreach (var d in p.GetData()) o[d.Key] = d.Value; arr.Add(o); }
        return new JsonObject { ["points"] = arr };
    }
    public override void ImportJson(JsonObject obj)
    {
        _points.Clear(); if (obj["points"] is JsonArray arr) foreach (var n in arr) { var o = n!.AsObject(); var d = new Dictionary<string, double>(StringComparer.Ordinal); foreach (var e in o) d[e.Key] = e.Value!.GetValue<double>(); _points.Add(new(d)); }
        Sync();
    }

    sealed class LabeledField : BinaryPanel { public TextBox Field { get; } public LabeledField(string label) { Width = 250; AddLeft(new Label { Text = label + ": ", AutoSize = true, Width = 120, TextAlign = ContentAlignment.MiddleLeft }); Field = new TextBox { Width = 100 }; AddRight(Field); } }
}

public sealed class LinearInterpolatorConfigure : InterpolatorConfigure<LinearInterpolator>
{
    public override Interpolator Get() { var n = _points.Count; var x = new double[n]; var y = new double[n]; for (var i = 0; i < n; i++) { x[i] = _points[i].GetX(); y[i] = _points[i].GetY(); } return new LinearInterpolator(x, y); }
    public override void Init()
    {
        base.Init();
        // A working default: a 60-second linear zoom through 102 levels (~1e30×) onto a minibrot.
        SetField("time", "0"); SetField("image", "0"); AddDefault();
        SetField("time", "60"); SetField("image", "102"); AddDefault();
        SetField("time", "0"); SetField("image", "0");
    }
}
public sealed class AccelInterpolatorConfigure : InterpolatorConfigure<AccelInterpolator>
{
    public override Interpolator Get() { var a = new double[_points.Count][]; for (var i = 0; i < _points.Count; i++) a[i] = [_points[i].GetX(), _points[i].GetY(), _points[i].GetData("accT"), _points[i].GetData("decT")]; return new AccelInterpolator(a); }
    public override void Init() { base.Init(); AddField("accT", 5); AddField("decT", 5); AddDefault(); }
}
public sealed class SlopeAccelInterpolatorConfigure : InterpolatorConfigure<SlopeAccelInterpolator>
{
    public override Interpolator Get() { var a = new double[_points.Count][]; for (var i = 0; i < _points.Count; i++) a[i] = [_points[i].GetX(), _points[i].GetY(), _points[i].GetData("maxTrans")]; return new SlopeAccelInterpolator(a); }
    public override void Init() { base.Init(); AddField("maxTrans", 10); AddDefault(); }
}

public sealed class GenOptionsPanel : UserControl, IExportable
{
    readonly NumericUpDown _w = new() { Minimum = 1, Maximum = 100000, Value = 1920 };
    readonly NumericUpDown _h = new() { Minimum = 1, Maximum = 100000, Value = 1080 };
    readonly NumericUpDown _fps = new() { Minimum = 1, Maximum = 1000, Value = 60 };
    readonly NumericUpDown _blend = new() { Minimum = 1, Maximum = 64, Value = 4 };
    readonly NumericUpDown _st = new() { Minimum = 0, Maximum = 100000, Value = 2 };
    readonly NumericUpDown _et = new() { Minimum = 0, Maximum = 100000, Value = 2 };
    readonly TextBox _ffmpeg = new() { Text = "ffmpeg" };
    readonly ComboBox _enc = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    public GenOptionsPanel()
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        Controls.Add(p);
        p.Controls.Add(MakeLine(Localization.Get("gen_options.width"), _w));
        p.Controls.Add(MakeLine(Localization.Get("gen_options.height"), _h));
        p.Controls.Add(MakeLine(Localization.Get("gen_options.fps"), _fps));
        p.Controls.Add(MakeLine(Localization.Get("gen_options.image_blending"), _blend));
        p.Controls.Add(MakeLine(Localization.Get("gen_options.start_time"), _st));
        p.Controls.Add(MakeLine(Localization.Get("gen_options.end_time"), _et));
        p.Controls.Add(MakeLine(Localization.Get("gen_options.ffmpeg"), _ffmpeg));
        foreach (var e in Enum.GetValues<EncodingParam>()) _enc.Items.Add(e);
        _enc.SelectedItem = EncodingParam.X264;
        p.Controls.Add(MakeLine(Localization.Get("gen_options.encoder"), _enc));
    }
    public int GetW() => (int)_w.Value; public int GetH() => (int)_h.Value; public int GetFps() => (int)_fps.Value;
    public int GetBlend() => (int)_blend.Value; public int GetSt() => (int)_st.Value; public int GetEt() => (int)_et.Value;
    public string GetFfmpeg() => _ffmpeg.Text; public EncodingParam GetEnc() => (EncodingParam)(_enc.SelectedItem ?? EncodingParam.X264);
    public JsonObject ExportJson() => new()
    {
        ["width"] = GetW(), ["height"] = GetH(), ["fps"] = GetFps(), ["imageBlending"] = GetBlend(),
        ["startTime"] = GetSt(), ["endTime"] = GetEt(), ["ffmpegCmd"] = GetFfmpeg(), ["selectedParam"] = GetEnc().ToString()
    };
    public void ImportJson(JsonObject o)
    {
        if (o["width"] != null) _w.Value = o.GetInt("width"); if (o["height"] != null) _h.Value = o.GetInt("height");
        if (o["fps"] != null) _fps.Value = o.GetInt("fps"); if (o["imageBlending"] != null) _blend.Value = o.GetInt("imageBlending");
        if (o["startTime"] != null) _st.Value = o.GetInt("startTime"); if (o["endTime"] != null) _et.Value = o.GetInt("endTime");
        if (o["ffmpegCmd"] != null) _ffmpeg.Text = o.GetString("ffmpegCmd");
        if (o["selectedParam"] != null) _enc.SelectedItem = Enum.Parse<EncodingParam>(o.GetString("selectedParam"), true);
    }
    static Control MakeLine(string l, Control c) { var p = new BinaryPanel { Width = 260 }; p.AddLeft(new Label { Text = l, AutoSize = true, Width = 120, TextAlign = ContentAlignment.MiddleLeft }); c.Width = 120; p.AddRight(c); return p; }
}

public sealed class FontChooserForm : Form, IExportable
{
    readonly ListBox _list = new(); readonly CheckBox _internal = new();
    readonly TextBox _preview = new() { Multiline = true, ReadOnly = true, Text = "The quick brown fox jumps over the lazy dog.\r\n0123456789" };
    readonly TextBox _search = new();
    Font _font = FontRepository.Instance.CreateRoboto(20);
    public FontChooserForm()
    {
        Text = Localization.Get("font_chooser.title"); Width = 640; Height = 360; FormBorderStyle = FormBorderStyle.SizableToolWindow;
        _preview.Font = _font;
        var families = FontFamily.Families.Select(f => f.Name).OrderBy(x => x).ToArray();
        _search.Dock = DockStyle.Top; _search.TextChanged += (_, _) => { var q = _search.Text.ToLowerInvariant(); _list.Items.Clear(); foreach (var n in families.Where(n => n.ToLowerInvariant().Contains(q))) _list.Items.Add(n); };
        Controls.Add(_search);
        _list.Dock = DockStyle.Fill; foreach (var n in families) _list.Items.Add(n);
        _list.SelectedIndexChanged += (_, _) => { if (!_internal.Checked && _list.SelectedItem is string n) { _font = new(n, 20, FontStyle.Regular, GraphicsUnit.Pixel); _preview.Font = _font; } };
        Controls.Add(_list);
        var bp = new Panel { Dock = DockStyle.Bottom, Height = 100 };
        _preview.Dock = DockStyle.Top; _preview.Height = 60; bp.Controls.Add(_preview);
        var ap = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32 };
        _internal.Text = Localization.Get("label.internal"); _internal.CheckedChanged += (_, _) => UpdateSel(); ap.Controls.Add(_internal);
        var close = new Button { Text = Localization.Get("label.close") }; close.Click += (_, _) => Hide(); ap.Controls.Add(close);
        bp.Controls.Add(ap); Controls.Add(bp); _internal.Checked = true;
    }
    public Font GetFont() => _font;
    public JsonObject ExportJson() => new() { ["fontName"] = _font.FontFamily.Name, ["internal"] = _internal.Checked };
    public void ImportJson(JsonObject o)
    {
        _internal.Checked = o.GetBool("internal");
        if (_internal.Checked) LoadDef();
        else { _list.Enabled = true; _list.SelectedItem = o.GetString("fontName"); }
        UpdateSel();
    }
    void UpdateSel()
    {
        _list.Enabled = !_internal.Checked; _search.Enabled = !_internal.Checked;
        if (_internal.Checked) LoadDef(); else if (_list.SelectedItem is string n) _font = new(n, 20, FontStyle.Regular, GraphicsUnit.Pixel);
        _preview.Font = _font;
    }
    void LoadDef() => _font = FontRepository.Instance.CreateRoboto(20);
}

public sealed class IndicatorSelectorPanel : UserControl, IExportable
{
    readonly FontChooserForm _fontChooser = new();
    readonly Dictionary<Type, CheckBox> _map = new();
    public IndicatorSelectorPanel(Type[] types)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        Controls.Add(p); var first = true;
        foreach (var t in types)
        {
            var ip = new Panel { Width = 210, Height = 24 };
            var cb = new CheckBox { Dock = DockStyle.Left, Width = 24, Checked = first }; first = false;
            var lbl = new Label { Text = Localization.Get("indicator.class.hywt.fractal.animator.indicator." + t.Name), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            ip.Controls.Add(lbl); ip.Controls.Add(cb); p.Controls.Add(ip); _map[t] = cb;
        }
        var fb = new Button { Text = Localization.Get("label.font"), AutoSize = true }; fb.Click += (_, _) => _fontChooser.Show(); p.Controls.Add(fb);
    }
    public List<Type> GetSelected() => _map.Where(e => e.Value.Checked).Select(e => e.Key).ToList();
    public Font GetSelectedFont() => _fontChooser.GetFont();
    public JsonObject ExportJson()
    {
        var arr = new JsonArray(); foreach (var t in GetSelected()) arr.Add(t.FullName);
        return new JsonObject { ["selected"] = arr, ["font"] = _fontChooser.ExportJson() };
    }
    public void ImportJson(JsonObject o)
    {
        foreach (var cb in _map.Values) cb.Checked = false;
        if (o["selected"] is JsonArray arr) foreach (var n in arr) { var cn = n?.GetValue<string>(); if (cn == null) continue; var t = _map.Keys.FirstOrDefault(k => k.FullName == cn || k.Name == cn.Split('.').Last()); if (t != null && _map.TryGetValue(t, out var c)) c.Checked = true; }
        if (o["font"] is JsonObject f) _fontChooser.ImportJson(f);
    }
}

public sealed class ProgressPanel : ProgressBar
{
    System.Windows.Forms.Timer? _timer; long _start;
    public ProgressPanel() { Minimum = 0; Maximum = 1000; Style = ProgressBarStyle.Continuous; }
    public void Start(VideoRenderer r)
    {
        Visible = true; _start = Environment.TickCount64; _timer?.Stop(); _timer = new() { Interval = 16 };
        _timer.Tick += (_, _) =>
        {
            if (r.IsFinished()) { _timer.Stop(); return; }
            var a = r.GetRenderedFrames(); var b = r.GetTotalFrames(); if (b <= 0) return;
            var pr = (double)a / b; var now = Environment.TickCount64;
            var eta = pr <= 0 ? double.PositiveInfinity : ((now - _start) / pr - (now - _start)) / 1000.0;
            Value = Math.Min(Maximum, (int)(a * 1000.0 / b));
            if (Parent is Form f) f.Text = string.Format(CultureInfo.InvariantCulture, Localization.Get("progress_bar.format"), pr * 100, a, b, r.GetRenderedKeyframes(), r.GetTotalKeyframes(), Utils.FormatSeconds(eta));
        }; _timer.Start();
    }
    public void StopProgress() { _timer?.Stop(); }
}

public sealed class ImageBrowser : Form
{
    readonly NumericUpDown _spinner = new(); readonly ImageLoader _mgr;
    readonly Label _info = new(); readonly ImagePanel _img = new(); int _ord;
    public ImageBrowser(ImageLoader mgr)
    {
        _mgr = mgr; _ord = 0; Text = Localization.Get("image_browser.title"); Width = 854; Height = 480;
        MouseWheel += (_, e) => { if (e.Delta > 0) ZoomIn(); else ZoomOut(); };
        _img.Dock = DockStyle.Fill; Controls.Add(_img);
        var c = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40 }; c.Controls.Add(_info);
        c.Controls.Add(new Label { Text = Localization.Get("image_browser.image"), AutoSize = true });
        _spinner.Width = 100; _spinner.Minimum = 0; _spinner.Maximum = Math.Max(0, mgr.Size() - 1);
        WireSpinner();
        c.Controls.Add(_spinner); Controls.Add(c);
        Theme.Apply(this);
        Load += (_, _) => Theme.DarkTitleBar(this);
        Shown += (_, _) => ShowFrame(0);
    }
    void ZoomIn() { _ord++; if (_ord >= _mgr.Size()) _ord = 0; ShowFrame(_ord); }
    void ZoomOut() { _ord--; if (_ord < 0) _ord = _mgr.Size() - 1; ShowFrame(_ord); }
    void ShowFrame(int i)
    {
        var f = _mgr.Get(i); var s = f.GetScale();
        _info.Text = string.Format(CultureInfo.InvariantCulture, Localization.Get("image_browser.format"), s.GetZooms(), s.GetMagnification());
        _img.SetImage(new Bitmap(f.GetImage())); if (_spinner.Value != i) { _spinner.ValueChanged -= _handler; _spinner.Value = i; _spinner.ValueChanged += _handler; } f.Dispose();
    }
    EventHandler? _handler;
    void WireSpinner() { _handler = (_, _) => { _ord = (int)_spinner.Value; ShowFrame(_ord); }; _spinner.ValueChanged += _handler; }
    sealed class ImagePanel : Panel { Bitmap? _img; protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); if (_img == null) return; var ar = (double)_img.Width / _img.Height; var nw = Width; var nh = (int)(Width / ar); if (nh > Height) { nh = Height; nw = (int)(Height * ar); } e.Graphics.DrawImage(_img, (Width - nw) / 2, (Height - nh) / 2, nw, nh); } public void SetImage(Bitmap b) { _img?.Dispose(); _img = b; Invalidate(); } }
}
