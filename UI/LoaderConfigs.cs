using System.Globalization;
using System.Text.Json.Nodes;
using FractalAnimator.Core;
using FractalAnimator.Keyframes;

namespace FractalAnimator.UI;

public sealed class KfImageLoaderConfigure : FolderLoaderBase
{
    protected override string DescKey => "image.kf.description";
    protected override string DiaDescKey => "image.kf.tooltip";
    protected override ImageLoader Make(string p) => new KfPngImageLoader(p);
    public override JsonObject ExportJson() => new() { ["path"] = _path };
    public override void ImportJson(JsonObject o) { _ = LoadPath(o.GetString("path")); }
}

public sealed class KfbLoaderConfigure : FolderLoaderBase
{
    protected override string DescKey => "image.kfb.description";
    protected override string DiaDescKey => "image.kf.tooltip";
    protected override ImageLoader Make(string p) => new KfbLoader(p);
    public override JsonObject ExportJson() => new() { ["path"] = _path };
    public override void ImportJson(JsonObject o) { _ = LoadPath(o.GetString("path")); }
}

public sealed class FzImageLoaderConfigure : FolderLoaderBase
{
    protected override string DescKey => "image.fz.description";
    protected override string DiaDescKey => "image.fz.tooltip";
    protected override ImageLoader Make(string p) => new FzImageLoader(p);
    public override JsonObject ExportJson() => new() { ["path"] = _path };
    public override void ImportJson(JsonObject o) { _ = LoadPath(o.GetString("path")); }
}

public sealed class MapleMandelMakerLoaderConfigure : FileLoaderBase
{
    protected override string DescKey => "image.maple.description";
    protected override string LabelKey => "image.maple.config";
    protected override ImageLoader Make(string p) => new MapleMandelMakerLoader(p);
    public override JsonObject ExportJson() => new() { ["path"] = _path };
    public override void ImportJson(JsonObject o) { _ = LoadPath(o.GetString("path")); }
}

public sealed class SimpleMandelbrotLoaderConfigure : ImageLoaderConfigure
{
    ImageLoader? _mgr; TextBox _re = null!, _im = null!, _magn = null!, _iter = null!; Button _btn = null!;
    public override ImageLoader? Get() => _mgr;
    public override void Init()
    {
        Controls.Clear(); Dock = DockStyle.Fill;
        var p = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Top, Height = 40, Text = Localization.Get("image.mandelbrot.description") };
        Controls.Add(p);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        Controls.Add(panel);
        panel.Controls.Add(new Label { Text = Localization.Get("image.mandelbrot.real") });
        _re = new TextBox { Width = 300, Text = "-1.99999911758766165543764649311537154663" }; panel.Controls.Add(_re);
        panel.Controls.Add(new Label { Text = Localization.Get("image.mandelbrot.imag") });
        _im = new TextBox { Width = 300, Text = "-4.2402439547240753390707694210131039e-13" }; panel.Controls.Add(_im);
        panel.Controls.Add(new Label { Text = Localization.Get("image.mandelbrot.magn") });
        _magn = new TextBox { Width = 300, Text = "5.070602e+30" }; panel.Controls.Add(_magn);
        panel.Controls.Add(new Label { Text = Localization.Get("image.mandelbrot.iter") });
        _iter = new TextBox { Width = 300, Text = "1024" }; panel.Controls.Add(_iter);
        _btn = new Button { Text = Localization.Get("label.set"), AutoSize = true };
        _btn.Click += async (_, _) =>
        {
            try { var zooms = Math.Log2(double.Parse(_magn.Text, CultureInfo.InvariantCulture)); _mgr = new SimpleMandelbrotLoader(BigDecimalCompat.Parse(_re.Text), BigDecimalCompat.Parse(_im.Text), zooms, int.Parse(_iter.Text, CultureInfo.InvariantCulture)); await OnLoadAsync(); }
            catch { MessageBox.Show(this, Localization.Get("message.invalidnum"), Localization.Get("message.error"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        panel.Controls.Add(_btn);
    }
    public override JsonObject ExportJson() => new() { ["real"] = _re.Text, ["imag"] = _im.Text, ["magn"] = double.Parse(_magn.Text, CultureInfo.InvariantCulture), ["iter"] = int.Parse(_iter.Text, CultureInfo.InvariantCulture) };
    public override void ImportJson(JsonObject o) { _re.Text = o.GetString("real"); _im.Text = o.GetString("imag"); _magn.Text = o.GetDouble("magn").ToString("R", CultureInfo.InvariantCulture); _iter.Text = o.GetInt("iter").ToString(); _btn.PerformClick(); }
}

public sealed class RayMarchFractalLoaderConfigure : ImageLoaderConfigure
{
    ImageLoader? _mgr;
    readonly NumericUpDown _w = new() { Minimum = 100, Maximum = 4000, Value = 800 };
    readonly NumericUpDown _h = new() { Minimum = 100, Maximum = 4000, Value = 600 };
    readonly NumericUpDown _dur = new() { Minimum = 1, Maximum = 300, Value = 10 };
    readonly NumericUpDown _fps = new() { Minimum = 1, Maximum = 120, Value = 30 };
    Button _btn = null!;
    public override ImageLoader? Get() => _mgr;
    public override void Init()
    {
        Controls.Clear(); Dock = DockStyle.Fill;
        var p = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Top, Height = 40, Text = "CPU ray-marched 3D Mandelbulb fractal (procedural)" };
        Controls.Add(p);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        Controls.Add(panel);
        panel.Controls.Add(MakePair("Width", _w)); panel.Controls.Add(MakePair("Height", _h));
        panel.Controls.Add(MakePair("Duration (s)", _dur)); panel.Controls.Add(MakePair("FPS", _fps));
        _btn = new Button { Text = Localization.Get("label.set"), AutoSize = true };
        _btn.Click += async (_, _) =>
        {
            try { _mgr = new RayMarchFractalLoader((int)_w.Value, (int)_h.Value, (double)_dur.Value * 2.0, (double)_fps.Value); await OnLoadAsync(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        panel.Controls.Add(_btn);
    }
    public override JsonObject ExportJson() => new() { ["width"] = (int)_w.Value, ["height"] = (int)_h.Value, ["duration"] = (double)_dur.Value, ["fps"] = (double)_fps.Value };
    public override void ImportJson(JsonObject o) { _w.Value = o.GetInt("width"); _h.Value = o.GetInt("height"); _dur.Value = (decimal)o.GetDouble("duration"); _fps.Value = (decimal)o.GetDouble("fps"); _btn.PerformClick(); }
    static Control MakePair(string l, Control c) { var p = new BinaryPanel { Width = 260 }; p.AddLeft(new Label { Text = l, AutoSize = true, Width = 120, TextAlign = ContentAlignment.MiddleLeft }); c.Width = 100; p.AddRight(c); return p; }
}

/// <summary>
/// The live fractal-formula studio: pick a preset, edit its C# in the code editor and it compiles
/// automatically (cached until the source changes); an inline preview re-renders on every change, and
/// the deep zoom — bounded or endless — builds itself when you hit render. No manual compile step.
/// </summary>
public sealed class FractalScriptLoaderConfigure : ImageLoaderConfigure
{
    IFractal? _fractal;
    ImageLoader? _mgr;
    string? _compiledSource;   // the editor text _fractal was compiled from (compile cache key)
    string? _loaderSignature;  // settings _mgr was built from (loader cache key)
    bool _suppressPreview;

    readonly ComboBox _preset = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly CodeEditor _editor = new() { Dock = DockStyle.Fill };
    readonly FractalPreview _preview = new() { Dock = DockStyle.Fill };
    readonly System.Windows.Forms.Timer _debounce = new() { Interval = 450 };
    readonly Button _recompileBtn = new() { Text = "Recompile", AutoSize = true, Width = 110, Height = 28 };
    readonly FlowLayoutPanel _paramPanel = new() { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true };
    readonly TextBox _real = new() { Dock = DockStyle.Fill, Text = "-1.99999911758766165543764649311537154663" };
    readonly TextBox _imag = new() { Dock = DockStyle.Fill, Text = "-4.2402439547240753390707694210131039e-13" };
    readonly NumericUpDown _zoomLevels = new() { Minimum = 1, Maximum = 1_000_000, Value = 102, Dock = DockStyle.Fill };
    readonly NumericUpDown _iter = new() { Minimum = 50, Maximum = 5_000_000, Value = 1500, Increment = 250, Dock = DockStyle.Fill };
    readonly NumericUpDown _iterPerZoom = new() { Minimum = 0, Maximum = 100_000, Value = 24, Dock = DockStyle.Fill };
    readonly NumericUpDown _resolution = new() { Minimum = 200, Maximum = 8000, Value = 1000, Increment = 100, Dock = DockStyle.Fill };
    readonly NumericUpDown _density = new() { Minimum = 0.01m, Maximum = 20m, Value = 0.167m, Increment = 0.05m, DecimalPlaces = 3, Dock = DockStyle.Fill };
    readonly CheckBox _infinite = new() { Text = "Endless zoom (renders until you press Stop)", AutoSize = true };
    readonly Label _magnLabel = new() { AutoSize = true, ForeColor = Theme.TextDim, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
    readonly Label _status = new() { AutoSize = true, Text = "", ForeColor = Theme.TextDim, Padding = new Padding(8, 6, 0, 0) };

    public FractalScriptLoaderConfigure()
    {
        foreach (var name in FractalCompiler.BuiltInNames) _preset.Items.Add(name);
        _preset.SelectedIndex = 0;
        _preset.SelectedIndexChanged += (_, _) => LoadPreset();
        _debounce.Tick += (_, _) => { _debounce.Stop(); RenderPreviewNow(); };
    }

    // The loader is built lazily from the current settings whenever something asks for it (e.g. render).
    public override ImageLoader? Get()
    {
        try { EnsureLoaderCurrent(); } catch { _mgr = null; }
        return _mgr;
    }

    public override void Init()
    {
        _suppressPreview = true;
        Controls.Clear();
        Dock = DockStyle.Fill;

        // ShaderToy-style: all the controls on the left, a big full-height live preview on the right.
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var desc = new Label { Text = Localization.Get("image.fractalscript.description"), Dock = DockStyle.Top, AutoSize = true, ForeColor = Theme.TextDim, Padding = new Padding(2, 2, 2, 4) };
        left.Controls.Add(desc, 0, 0);

        var presetRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, AutoSize = true };
        presetRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        presetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        presetRow.Controls.Add(new Label { Text = Localization.Get("label.preset"), AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 6, 0) }, 0, 0);
        presetRow.Controls.Add(_preset, 1, 0);
        left.Controls.Add(presetRow, 0, 1);

        _editor.Code = GetPresetSource(_preset.SelectedIndex);
        left.Controls.Add(_editor, 0, 2);

        left.Controls.Add(BuildFields(), 0, 3);

        var actionRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _recompileBtn.Click += (_, _) => { _compiledSource = null; RenderPreviewNow(); };
        actionRow.Controls.Add(_recompileBtn);
        actionRow.Controls.Add(_status);
        left.Controls.Add(actionRow, 0, 4);

        left.Controls.Add(_paramPanel, 0, 5);
        root.Controls.Add(left, 0, 0);

        var previewBox = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(6, 0, 0, 0) };
        previewBox.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewBox.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewBox.Controls.Add(new Label { Text = "Live preview — double-click to recenter · scroll to zoom", Dock = DockStyle.Fill, AutoSize = true, ForeColor = Theme.TextDim, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 0, 0, 4) }, 0, 0);
        previewBox.Controls.Add(_preview, 0, 1);
        root.Controls.Add(previewBox, 1, 0);

        Controls.Add(root);
        WireLiveEvents();
        UpdateMagnitudeLabel();
        UpdateInfiniteState();
        _suppressPreview = false;
        _preview.HandleCreated += (_, _) => SchedulePreview();
        SchedulePreview();
    }

    void WireLiveEvents()
    {
        _editor.CodeChanged += (_, _) => SchedulePreview();
        _real.TextChanged += (_, _) => SchedulePreview();
        _imag.TextChanged += (_, _) => SchedulePreview();
        _iter.ValueChanged += (_, _) => SchedulePreview();
        _iterPerZoom.ValueChanged += (_, _) => SchedulePreview();
        _resolution.ValueChanged += (_, _) => SchedulePreview();
        _density.ValueChanged += (_, _) => SchedulePreview();
        _zoomLevels.ValueChanged += (_, _) => { UpdateMagnitudeLabel(); SchedulePreview(); };
        _infinite.CheckedChanged += (_, _) => { UpdateInfiniteState(); SchedulePreview(); };
        _preview.PointPicked += Recenter;
        _preview.ZoomChanged += delta =>
            _zoomLevels.Value = Math.Clamp((int)_zoomLevels.Value + delta, (int)_zoomLevels.Minimum, (int)_zoomLevels.Maximum);
    }

    Control BuildFields()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 6,
            AutoSize = true,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 6; i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var row = 0;
        AddWide(grid, Localization.Get("image.mandelbrot.real"), _real, row++);
        AddWide(grid, Localization.Get("image.mandelbrot.imag"), _imag, row++);
        AddPair(grid, "Zoom levels (×2)", _zoomLevels, "", _magnLabel, row++);
        AddPair(grid, Localization.Get("image.mandelbrot.iter"), _iter, "Iterations / level", _iterPerZoom, row++);
        AddPair(grid, "Keyframe resolution", _resolution, "Color density", _density, row++);
        grid.Controls.Add(_infinite, 0, row);
        grid.SetColumnSpan(_infinite, 4);
        return grid;
    }

    static void AddPair(TableLayoutPanel grid, string leftLabel, Control left, string rightLabel, Control right, int row)
    {
        grid.Controls.Add(new Label { Text = leftLabel, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 5, 0, 0) }, 0, row);
        grid.Controls.Add(left, 1, row);
        grid.Controls.Add(new Label { Text = rightLabel, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(6, 5, 0, 0) }, 2, row);
        grid.Controls.Add(right, 3, row);
    }

    static void AddWide(TableLayoutPanel grid, string label, Control field, int row)
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 5, 0, 0) }, 0, row);
        grid.Controls.Add(field, 1, row);
        grid.SetColumnSpan(field, 3);
    }

    void UpdateMagnitudeLabel()
    {
        var exponent = (double)_zoomLevels.Value * 0.30103;
        _magnLabel.Text = exponent < 6
            ? $"≈ {Math.Pow(10, exponent):N0}× magnification"
            : $"≈ 1e{exponent:F1}× magnification";
    }

    void UpdateInfiniteState() => _zoomLevels.Enabled = !_infinite.Checked;

    void LoadPreset()
    {
        var index = _preset.SelectedIndex;
        if (index >= 0) _editor.Code = GetPresetSource(index);
    }

    void SchedulePreview()
    {
        if (_suppressPreview) return;
        _debounce.Stop();
        _debounce.Start();
    }

    void RenderPreviewNow()
    {
        if (!EnsureCompiled() || _fractal == null) return;
        try
        {
            var real = BigDecimalCompat.Parse(_real.Text);
            var imag = BigDecimalCompat.Parse(_imag.Text);
            var fractal = _fractal;
            var zooms = (double)_zoomLevels.Value;
            var iterations = (int)_iter.Value;
            var density = (double)_density.Value;
            _preview.RenderPreview((size, token) =>
            {
                var buffer = new float[size * size];
                PerturbationEngine.Render(fractal, real, imag, zooms, size, size, iterations, buffer, token);
                return FractalColoring.Colorize(buffer, size, size, density);
            });
        }
        catch (Exception ex)
        {
            SetStatus("Invalid coordinate — " + ex.Message.Split('\n').FirstOrDefault(), Theme.Bad);
        }
    }

    // Compile only when the source actually changed; otherwise reuse the cached fractal.
    bool EnsureCompiled()
    {
        if (_fractal != null && _compiledSource == _editor.Code) return true;
        try
        {
            _paramPanel.Controls.Clear();
            var compiled = FractalCompiler.Compile(_editor.Code);
            if (compiled == null)
            {
                SetStatus("No IFractal type found in the source.", Theme.Bad);
                _fractal = null;
                _compiledSource = null;
                return false;
            }
            _fractal = compiled;
            _compiledSource = _editor.Code;
            var simd = _fractal is ISimdFractal && PerturbationEngine.SimdAccelerated;
            SetStatus($"Compiled: {_fractal.Name}   [{(simd ? $"SIMD ×{PerturbationEngine.VectorLanes}" : "scalar")}]", Theme.Good);
            BuildParamSliders();
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("Compile error — " + ex.Message.Split('\n').FirstOrDefault(), Theme.Bad);
            _fractal = null;
            _compiledSource = null;
            return false;
        }
    }

    void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    void BuildParamSliders()
    {
        if (_fractal == null) return;
        foreach (var parameter in _fractal.GetParams())
        {
            var panel = new BinaryPanel { Width = 280 };
            panel.AddLeft(new Label { Text = parameter.Label, AutoSize = true, Width = 110, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text });
            var numeric = new NumericUpDown
            {
                Minimum = (decimal)parameter.Min,
                Maximum = (decimal)parameter.Max,
                Value = (decimal)Math.Clamp(parameter.Value, parameter.Min, parameter.Max),
                DecimalPlaces = 4,
                Increment = 0.001m,
                Width = 140,
            };
            var captured = parameter;
            numeric.ValueChanged += (_, _) =>
            {
                var value = (double)numeric.Value;
                captured.Value = value;
                _fractal!.SetParam(captured.Name, value);
                SchedulePreview();
            };
            Theme.Apply(panel);
            panel.AddRight(numeric);
            _paramPanel.Controls.Add(panel);
        }
        Theme.Apply(_paramPanel);
    }

    // Move the center to the point the user double-clicked in the preview, at full precision.
    void Recenter(double fractionX, double fractionY)
    {
        try
        {
            var real = BigDecimalCompat.Parse(_real.Text);
            var imag = BigDecimalCompat.Parse(_imag.Text);
            var zooms = (double)_zoomLevels.Value;
            var spacingLog2 = 2.0 - zooms;
            var scaleExp = (int)Math.Floor(spacingLog2);
            var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp);
            var precision = Math.Max(40, (int)(zooms * 0.30103) + 24);
            var context = new MathContextCompat(precision);
            var power = BigDecimalCompat.Pow2(scaleExp);
            var newReal = real.Add(BigDecimalCompat.FromDouble(fractionX * mantissa).Multiply(power, context), context);
            var newImag = imag.Add(BigDecimalCompat.FromDouble(fractionY * mantissa).Multiply(power, context), context);
            _suppressPreview = true;
            _real.Text = newReal.ToPlainString();
            _imag.Text = newImag.ToPlainString();
            _suppressPreview = false;
            SchedulePreview();
        }
        catch
        {
        }
    }

    // Build (or reuse) the loader from the current settings, compiling first if needed.
    void EnsureLoaderCurrent()
    {
        if (!EnsureCompiled() || _fractal == null) { _mgr = null; return; }
        var signature = LoaderSignature();
        if (_mgr != null && signature == _loaderSignature) return;
        var real = BigDecimalCompat.Parse(_real.Text);
        var imag = BigDecimalCompat.Parse(_imag.Text);
        var resolution = (int)_resolution.Value;
        var density = (double)_density.Value;
        var baseIterations = (int)_iter.Value;
        var iterationsPerZoom = (double)_iterPerZoom.Value;
        if (_infinite.Checked)
            _mgr = new InfiniteFractalLoader(_fractal, real, imag, 2_000_000, baseIterations,
                iterationsPerZoom, resolution, resolution, density);
        else
            _mgr = new FractalScriptLoader(_fractal, real, imag, (double)_zoomLevels.Value, baseIterations,
                resolution, resolution, density, iterationsPerZoom);
        _loaderSignature = signature;
        _ = OnLoadAsync();
    }

    string LoaderSignature()
    {
        var parameters = _fractal == null ? "" : string.Join(",", _fractal.GetParams().Select(p => p.Value));
        return string.Join("|", _editor.Code.Length, _editor.Code.GetHashCode(), _real.Text, _imag.Text,
            _zoomLevels.Value, _iter.Value, _iterPerZoom.Value, _resolution.Value, _density.Value, _infinite.Checked, parameters);
    }

    static string GetPresetSource(int index) =>
        index >= 0 && index < FractalCompiler.BuiltInNames.Count ? FractalCompiler.GetBuiltInSource(index) : "";

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounce.Dispose();
        base.Dispose(disposing);
    }

    public override JsonObject ExportJson()
    {
        var parameters = new JsonArray();
        if (_fractal != null)
            foreach (var p in _fractal.GetParams())
                parameters.Add(new JsonObject { ["name"] = p.Name, ["value"] = p.Value });
        return new JsonObject
        {
            ["preset"] = _preset.SelectedIndex,
            ["code"] = _editor.Code,
            ["real"] = _real.Text,
            ["imag"] = _imag.Text,
            ["zoomLevels"] = (int)_zoomLevels.Value,
            ["iter"] = (int)_iter.Value,
            ["iterPerZoom"] = (int)_iterPerZoom.Value,
            ["resolution"] = (int)_resolution.Value,
            ["density"] = (double)_density.Value,
            ["infinite"] = _infinite.Checked,
            ["params"] = parameters,
        };
    }

    public override void ImportJson(JsonObject o)
    {
        _suppressPreview = true;
        var index = o.GetInt("preset");
        _preset.SelectedIndex = index >= 0 && index < _preset.Items.Count ? index : 0;
        if (o["code"] is not null) _editor.Code = o.GetString("code");
        _real.Text = o.GetString("real");
        _imag.Text = o.GetString("imag");
        if (o["zoomLevels"] is not null) _zoomLevels.Value = Math.Clamp(o.GetInt("zoomLevels"), 1, 1_000_000);
        if (o["iter"] is not null) _iter.Value = Math.Clamp(o.GetInt("iter"), 50, 5_000_000);
        if (o["iterPerZoom"] is not null) _iterPerZoom.Value = Math.Clamp(o.GetInt("iterPerZoom"), 0, 100_000);
        if (o["resolution"] is not null) _resolution.Value = Math.Clamp(o.GetInt("resolution"), 200, 8000);
        if (o["density"] is not null) _density.Value = (decimal)Math.Clamp(o.GetDouble("density"), 0.01, 20);
        if (o["infinite"] is not null) _infinite.Checked = o.GetBool("infinite");
        EnsureCompiled();
        if (o["params"] is JsonArray parameters && _fractal != null)
            foreach (var node in parameters)
            {
                var po = node!.AsObject();
                _fractal.SetParam(po.GetString("name"), po.GetDouble("value"));
            }
        UpdateMagnitudeLabel();
        UpdateInfiniteState();
        _suppressPreview = false;
        SchedulePreview();
    }
}

public abstract class FolderLoaderBase : ImageLoaderConfigure
{
    protected ImageLoader? _mgr; protected Label? _lbl; protected string? _path;
    protected abstract string DescKey { get; } protected abstract string DiaDescKey { get; } protected abstract ImageLoader Make(string p);
    public override ImageLoader? Get() => _mgr;
    public override void Init()
    {
        Controls.Clear(); Dock = DockStyle.Fill; _lbl = new Label { Text = Localization.Get("image.dir"), Dock = DockStyle.Top, Height = 24 };
        Controls.Add(_lbl); var pt = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Top, Height = 40, Text = Localization.Get(DescKey) };
        Controls.Add(pt); var btn = new Button { Text = Localization.Get("label.select"), Dock = DockStyle.Top, Height = 32 };
        btn.Click += async (_, _) => { using var d = new FolderBrowserDialog(); if (d.ShowDialog(this) == DialogResult.OK) await LoadPath(d.SelectedPath); };
        Controls.Add(btn);
    }
    protected async Task LoadPath(string p) { _mgr = Make(p); if (_lbl != null) _lbl.Text = p; _path = p; await OnLoadAsync(); }
}

public abstract class FileLoaderBase : ImageLoaderConfigure
{
    protected ImageLoader? _mgr; protected Label? _lbl; protected string? _path;
    protected abstract string DescKey { get; } protected abstract string LabelKey { get; } protected abstract ImageLoader Make(string p);
    public override ImageLoader? Get() => _mgr;
    public override void Init()
    {
        Controls.Clear(); Dock = DockStyle.Fill; _lbl = new Label { Text = Localization.Get(LabelKey), Dock = DockStyle.Top, Height = 24 };
        Controls.Add(_lbl); var pt = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Top, Height = 40, Text = Localization.Get(DescKey) };
        Controls.Add(pt); var btn = new Button { Text = Localization.Get("label.select"), Dock = DockStyle.Top, Height = 32 };
        btn.Click += async (_, _) => { using var d = new OpenFileDialog(); if (d.ShowDialog(this) == DialogResult.OK) await LoadPath(d.FileName); };
        Controls.Add(btn);
    }
    protected async Task LoadPath(string p) { _mgr = Make(p); if (_lbl != null) _lbl.Text = p; _path = p; await OnLoadAsync(); }
}
