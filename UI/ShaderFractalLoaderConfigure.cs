using System.Globalization;
using System.Text.Json.Nodes;
using FractalAnimator.Core;
using FractalAnimator.Keyframes;

namespace FractalAnimator.UI;

/// <summary>
/// The FractalParadise studio: the ported Fractal.shader / Newton shaders as a single modular C#
/// engine. The fractal style, complex power, inversion (reverse), Julia constant, orbit-trap colors
/// and iteration count are all switched in code — no shader variants. A Unity <c>.mat</c> can be
/// loaded directly. The inline preview re-renders live; the video zooms from the start level to the
/// shown level. Coloring defaults to Fractal.shader's B-style orbit trap.
/// </summary>
public sealed class ShaderFractalLoaderConfigure : ImageLoaderConfigure
{
    static readonly (ShaderFractalType type, string name)[] Styles =
    [
        (ShaderFractalType.Mandelbrot, "Mandelbrot (zᵖ + c)"),
        (ShaderFractalType.BurningShip, "Burning Ship"),
        (ShaderFractalType.Feather, "Feather"),
        (ShaderFractalType.Sfx, "Sfx"),
        (ShaderFractalType.Henon, "Hénon"),
        (ShaderFractalType.Duffing, "Duffing"),
        (ShaderFractalType.Ikeda, "Ikeda"),
        (ShaderFractalType.Chirikov, "Chirikov (standard map)"),
        (ShaderFractalType.Newton, "Newton (transcendental)"),
        (ShaderFractalType.NewtonFlower, "Newton Flower"),
    ];

    ImageLoader? _mgr;
    string? _loaderSignature;
    bool _suppressPreview;

    readonly FractalPreview _preview = new() { Dock = DockStyle.Fill };
    readonly System.Windows.Forms.Timer _debounce = new() { Interval = 350 };
    readonly ComboBox _style = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox _centerX = new() { Dock = DockStyle.Fill, Text = "3.0" };
    readonly TextBox _centerY = new() { Dock = DockStyle.Fill, Text = "9.0" };
    readonly NumericUpDown _powerReal = new() { Minimum = -8, Maximum = 8, Value = 2, Increment = 0.01m, DecimalPlaces = 2, Dock = DockStyle.Fill };
    readonly NumericUpDown _powerImag = new() { Minimum = -8, Maximum = 8, Value = 0, Increment = 0.01m, DecimalPlaces = 2, Dock = DockStyle.Fill };
    readonly NumericUpDown _viewZoom = new() { Minimum = -10, Maximum = 40, Value = 8, Increment = 1, DecimalPlaces = 2, Dock = DockStyle.Fill };
    readonly NumericUpDown _startZoom = new() { Minimum = -10, Maximum = 40, Value = -2, Increment = 1, DecimalPlaces = 2, Dock = DockStyle.Fill };
    readonly NumericUpDown _cx = new() { Minimum = -1_000_000, Maximum = 1_000_000, Value = 1, Increment = 1000, Dock = DockStyle.Fill };
    readonly NumericUpDown _cy = new() { Minimum = -1_000_000, Maximum = 1_000_000, Value = 1, Increment = 1000, Dock = DockStyle.Fill };
    readonly NumericUpDown _leafOffset = new() { Minimum = -3, Maximum = 3, Value = 1, Increment = 0.01m, DecimalPlaces = 2, Dock = DockStyle.Fill };
    readonly NumericUpDown _iterations = new() { Minimum = 50, Maximum = 5000, Value = 500, Increment = 50, Dock = DockStyle.Fill };
    readonly NumericUpDown _resolution = new() { Minimum = 200, Maximum = 3000, Value = 900, Increment = 100, Dock = DockStyle.Fill };
    readonly NumericUpDown _colorR = new() { Minimum = 0, Maximum = 20, Value = 10m, Increment = 0.25m, DecimalPlaces = 2, Dock = DockStyle.Fill };
    readonly NumericUpDown _colorG = new() { Minimum = 0, Maximum = 20, Value = 2.5m, Increment = 0.25m, DecimalPlaces = 2, Dock = DockStyle.Fill };
    readonly NumericUpDown _colorB = new() { Minimum = 0, Maximum = 20, Value = 0.75m, Increment = 0.25m, DecimalPlaces = 2, Dock = DockStyle.Fill };
    readonly CheckBox _reverse = new() { Text = "Reverse (1/c inversion)", AutoSize = true };
    readonly CheckBox _useJulia = new() { Text = "Julia (constant c)", AutoSize = true };

    public ShaderFractalLoaderConfigure()
    {
        foreach (var (_, name) in Styles) _style.Items.Add(name);
        _style.SelectedIndex = 0;
        _debounce.Tick += (_, _) => { _debounce.Stop(); RenderPreviewNow(); };
    }

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

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoScroll = true, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var desc = new Label { Text = "FractalParadise shaders, ported to one modular engine. Pick a style, tweak, and zoom.", AutoSize = true, ForeColor = Theme.TextDim, Padding = new Padding(2, 2, 2, 4) };
        AddSpan(fields, desc, 4);

        var loadButton = new Button { Text = "Load Unity .mat…", AutoSize = true, Height = 28 };
        loadButton.Click += (_, _) => LoadMaterialDialog();
        AddSpan(fields, loadButton, 4);

        var row = fields.RowCount;
        AddCell(fields, "Style", 0, row);
        fields.Controls.Add(_style, 1, row);
        fields.SetColumnSpan(_style, 3);

        AddPair(fields, "Center X", _centerX, "Center Y", _centerY);
        AddPair(fields, "Power (real)", _powerReal, "Power (imag)", _powerImag);
        AddPair(fields, "Start zoom", _startZoom, "View / end zoom", _viewZoom);
        AddPair(fields, "Julia cX", _cx, "Julia cY", _cy);
        AddPair(fields, "Leaf offset", _leafOffset, "Iterations", _iterations);
        AddPair(fields, "Color R", _colorR, "Color G", _colorG);
        AddPair(fields, "Color B", _colorB, "Keyframe res", _resolution);

        var toggles = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        toggles.Controls.Add(_reverse);
        toggles.Controls.Add(_useJulia);
        AddSpan(fields, toggles, 4);

        AddSpan(fields, new Label { Text = "Double-click preview to recenter · scroll to zoom. Coloring = Fractal.shader B-style.", AutoSize = true, ForeColor = Theme.TextDim }, 4);

        root.Controls.Add(fields, 0, 0);

        var previewBox = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(6, 0, 0, 0) };
        previewBox.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewBox.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewBox.Controls.Add(new Label { Text = "Live preview (shows the end-of-zoom view)", Dock = DockStyle.Fill, AutoSize = true, ForeColor = Theme.TextDim }, 0, 0);
        previewBox.Controls.Add(_preview, 0, 1);
        root.Controls.Add(previewBox, 1, 0);

        Controls.Add(root);

        WireLiveEvents();
        _suppressPreview = false;
        _preview.HandleCreated += (_, _) => SchedulePreview();
        SchedulePreview();
    }

    void WireLiveEvents()
    {
        _style.SelectedIndexChanged += (_, _) => SchedulePreview();
        foreach (var box in new[] { _centerX, _centerY }) box.TextChanged += (_, _) => SchedulePreview();
        foreach (var numeric in new[] { _powerReal, _powerImag, _viewZoom, _cx, _cy, _leafOffset, _iterations, _colorR, _colorG, _colorB, _resolution })
            numeric.ValueChanged += (_, _) => SchedulePreview();
        _reverse.CheckedChanged += (_, _) => SchedulePreview();
        _useJulia.CheckedChanged += (_, _) => SchedulePreview();
        _preview.PointPicked += Recenter;
        _preview.ZoomChanged += delta =>
            _viewZoom.Value = Math.Clamp(_viewZoom.Value + delta, _viewZoom.Minimum, _viewZoom.Maximum);
    }

    ShaderColorParams BuildParams() => new()
    {
        Type = Styles[Math.Clamp(_style.SelectedIndex, 0, Styles.Length - 1)].type,
        PowerReal = (double)_powerReal.Value,
        PowerImag = (double)_powerImag.Value,
        Reverse = _reverse.Checked,
        UseJulia = _useJulia.Checked,
        JuliaCXRaw = (double)_cx.Value,
        JuliaCYRaw = (double)_cy.Value,
        LeafOrbitOffset = (double)_leafOffset.Value,
        ColorR = (double)_colorR.Value,
        ColorG = (double)_colorG.Value,
        ColorB = (double)_colorB.Value,
        Iterations = (int)_iterations.Value,
    };

    void SchedulePreview()
    {
        if (_suppressPreview) return;
        _debounce.Stop();
        _debounce.Start();
    }

    void RenderPreviewNow()
    {
        var parameters = BuildParams();
        var centerX = ParseDouble(_centerX.Text);
        var centerY = ParseDouble(_centerY.Text);
        var zooms = (double)_viewZoom.Value;
        _preview.RenderPreview((size, token) =>
            new ShaderFractalImage(parameters, centerX, centerY, zooms, size, size).Render(token));
    }

    void Recenter(double fractionX, double fractionY)
    {
        var fullWidth = Math.Pow(2.0, 2.0 - (double)_viewZoom.Value);
        var centerX = ParseDouble(_centerX.Text) + fractionX * fullWidth;
        var centerY = ParseDouble(_centerY.Text) + fractionY * fullWidth;
        _suppressPreview = true;
        _centerX.Text = centerX.ToString("R", CultureInfo.InvariantCulture);
        _centerY.Text = centerY.ToString("R", CultureInfo.InvariantCulture);
        _suppressPreview = false;
        SchedulePreview();
    }

    void EnsureLoaderCurrent()
    {
        var signature = Signature();
        if (_mgr != null && signature == _loaderSignature) return;
        var parameters = BuildParams();
        var centerX = ParseDouble(_centerX.Text);
        var centerY = ParseDouble(_centerY.Text);
        var start = (double)_startZoom.Value;
        var end = (double)_viewZoom.Value;
        if (end < start) (start, end) = (end, start);
        _mgr = new ShaderFractalLoader(parameters, centerX, centerY, start, end, (int)_resolution.Value, (int)_resolution.Value);
        _loaderSignature = signature;
        _ = OnLoadAsync();
    }

    string Signature() => string.Join("|", _style.SelectedIndex, _centerX.Text, _centerY.Text,
        _powerReal.Value, _powerImag.Value, _startZoom.Value, _viewZoom.Value, _cx.Value, _cy.Value,
        _leafOffset.Value, _iterations.Value, _resolution.Value, _colorR.Value, _colorG.Value, _colorB.Value,
        _reverse.Checked, _useJulia.Checked);

    void LoadMaterialDialog()
    {
        using var dialog = new OpenFileDialog { Filter = "Unity material|*.mat", Title = "Load a FractalParadise material" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { LoadMaterial(dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Localization.Get("message.error"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    void LoadMaterial(string path)
    {
        var floats = new Dictionary<string, double>(StringComparer.Ordinal);
        double colorR = 10, colorG = 2.5, colorB = 0.75;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("- _Color:", StringComparison.Ordinal))
            {
                colorR = ExtractComponent(line, "r:") ?? colorR;
                colorG = ExtractComponent(line, "g:") ?? colorG;
                colorB = ExtractComponent(line, "b:") ?? colorB;
                continue;
            }
            if (!line.StartsWith("- ", StringComparison.Ordinal)) continue;
            var body = line[2..];
            var colon = body.IndexOf(':');
            if (colon < 0) continue;
            var key = body[..colon].Trim();
            if (double.TryParse(body[(colon + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                floats[key] = value;
        }

        _suppressPreview = true;
        if (floats.TryGetValue("_FractalType", out var t)) _style.SelectedIndex = Math.Clamp((int)t, 0, Styles.Length - 1);
        if (floats.TryGetValue("_CenterX", out var cxv)) _centerX.Text = cxv.ToString("R", CultureInfo.InvariantCulture);
        if (floats.TryGetValue("_CenterY", out var cyv)) _centerY.Text = cyv.ToString("R", CultureInfo.InvariantCulture);
        SetNumeric(_powerReal, floats, "_PowerReal");
        SetNumeric(_powerImag, floats, "_PowerImag");
        SetNumeric(_cx, floats, "_CX");
        SetNumeric(_cy, floats, "_CY");
        SetNumeric(_leafOffset, floats, "leafOrbitOffset");
        SetNumeric(_iterations, floats, "_Iterations");
        if (floats.TryGetValue("_Reverse", out var rev)) _reverse.Checked = rev > 0.5;
        if (floats.TryGetValue("_UseJulia", out var ju)) _useJulia.Checked = ju > 0.5;
        // Scale -> the view zoom level: full width = Scale * 2e-5 = 2^(2 - zooms).
        if (floats.TryGetValue("_Scale", out var scale) && scale > 0)
        {
            var viewZooms = 2.0 - Math.Log2(scale * 2e-5);
            _startZoom.Value = (decimal)Math.Clamp(viewZooms, (double)_startZoom.Minimum, (double)_startZoom.Maximum);
            _viewZoom.Value = (decimal)Math.Clamp(viewZooms, (double)_viewZoom.Minimum, (double)_viewZoom.Maximum);
        }
        _colorR.Value = (decimal)Math.Clamp(colorR, 0, 20);
        _colorG.Value = (decimal)Math.Clamp(colorG, 0, 20);
        _colorB.Value = (decimal)Math.Clamp(colorB, 0, 20);
        _suppressPreview = false;
        SchedulePreview();
    }

    static double? ExtractComponent(string line, string token)
    {
        var index = line.IndexOf(token, StringComparison.Ordinal);
        if (index < 0) return null;
        var rest = line[(index + token.Length)..].TrimStart();
        var end = 0;
        while (end < rest.Length && (char.IsDigit(rest[end]) || rest[end] is '.' or '-' or '+' or 'e' or 'E')) end++;
        return double.TryParse(rest[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    static void SetNumeric(NumericUpDown control, Dictionary<string, double> floats, string key)
    {
        if (!floats.TryGetValue(key, out var value)) return;
        control.Value = (decimal)Math.Clamp(value, (double)control.Minimum, (double)control.Maximum);
    }

    static double ParseDouble(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0;

    static void AddPair(TableLayoutPanel grid, string leftLabel, Control left, string rightLabel, Control right)
    {
        var row = grid.RowCount;
        AddCell(grid, leftLabel, 0, row);
        grid.Controls.Add(left, 1, row);
        AddCell(grid, rightLabel, 2, row);
        grid.Controls.Add(right, 3, row);
    }

    static void AddCell(TableLayoutPanel grid, string label, int column, int row) =>
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(column == 0 ? 0 : 6, 5, 0, 0) }, column, row);

    static void AddSpan(TableLayoutPanel grid, Control control, int span)
    {
        var row = grid.RowCount;
        grid.Controls.Add(control, 0, row);
        grid.SetColumnSpan(control, span);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounce.Dispose();
        base.Dispose(disposing);
    }

    public override JsonObject ExportJson() => new()
    {
        ["style"] = _style.SelectedIndex,
        ["centerX"] = _centerX.Text,
        ["centerY"] = _centerY.Text,
        ["powerReal"] = (double)_powerReal.Value,
        ["powerImag"] = (double)_powerImag.Value,
        ["startZoom"] = (double)_startZoom.Value,
        ["viewZoom"] = (double)_viewZoom.Value,
        ["cx"] = (double)_cx.Value,
        ["cy"] = (double)_cy.Value,
        ["leafOffset"] = (double)_leafOffset.Value,
        ["iterations"] = (int)_iterations.Value,
        ["resolution"] = (int)_resolution.Value,
        ["colorR"] = (double)_colorR.Value,
        ["colorG"] = (double)_colorG.Value,
        ["colorB"] = (double)_colorB.Value,
        ["reverse"] = _reverse.Checked,
        ["useJulia"] = _useJulia.Checked,
    };

    public override void ImportJson(JsonObject o)
    {
        _suppressPreview = true;
        if (o["style"] is not null) _style.SelectedIndex = Math.Clamp(o.GetInt("style"), 0, Styles.Length - 1);
        if (o["centerX"] is not null) _centerX.Text = o.GetString("centerX");
        if (o["centerY"] is not null) _centerY.Text = o.GetString("centerY");
        SetFromJson(_powerReal, o, "powerReal");
        SetFromJson(_powerImag, o, "powerImag");
        SetFromJson(_startZoom, o, "startZoom");
        SetFromJson(_viewZoom, o, "viewZoom");
        SetFromJson(_cx, o, "cx");
        SetFromJson(_cy, o, "cy");
        SetFromJson(_leafOffset, o, "leafOffset");
        SetFromJson(_iterations, o, "iterations");
        SetFromJson(_resolution, o, "resolution");
        SetFromJson(_colorR, o, "colorR");
        SetFromJson(_colorG, o, "colorG");
        SetFromJson(_colorB, o, "colorB");
        if (o["reverse"] is not null) _reverse.Checked = o.GetBool("reverse");
        if (o["useJulia"] is not null) _useJulia.Checked = o.GetBool("useJulia");
        _suppressPreview = false;
        SchedulePreview();
    }

    static void SetFromJson(NumericUpDown control, JsonObject o, string key)
    {
        if (o[key] is null) return;
        control.Value = (decimal)Math.Clamp(o.GetDouble(key), (double)control.Minimum, (double)control.Maximum);
    }
}
