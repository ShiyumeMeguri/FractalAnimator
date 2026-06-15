using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FractalAnimator.Core;
using FractalAnimator.Interp;
using FractalAnimator.Keyframes;
using FractalAnimator.Rendering;

namespace FractalAnimator.UI;

public sealed class MainForm : Form, IExportable
{
    readonly IndicatorSelectorPanel _indiPanel;
    readonly Button _browseBtn, _genBtn;
    readonly ProgressPanel _progressPanel;
    readonly GenOptionsPanel _genOpts;
    readonly Panel _interpPanel, _loaderPanel;
    readonly ComboBox _interpSel, _loaderSel;
    readonly Label _frameNum;
    readonly ToolTip _tips = new();
    ImageLoaderConfigure? _loaderCfg; OptionConfigure<Interpolator>? _interpCfg;
    bool _rendering; VideoRenderer? _renderer;

    // The live formula studio is the headline experience, so it leads the list.
    static readonly Dictionary<Type, Func<ImageLoaderConfigure>> LoaderFactories = new()
    {
        [typeof(FractalScriptLoaderConfigure)] = () => new FractalScriptLoaderConfigure(),
        [typeof(SimpleMandelbrotLoaderConfigure)] = () => new SimpleMandelbrotLoaderConfigure(),
        [typeof(RayMarchFractalLoaderConfigure)] = () => new RayMarchFractalLoaderConfigure(),
        [typeof(FzImageLoaderConfigure)] = () => new FzImageLoaderConfigure(),
        [typeof(KfImageLoaderConfigure)] = () => new KfImageLoaderConfigure(),
        [typeof(KfbLoaderConfigure)] = () => new KfbLoaderConfigure(),
        [typeof(MapleMandelMakerLoaderConfigure)] = () => new MapleMandelMakerLoaderConfigure(),
    };
    static readonly Dictionary<Type, Func<OptionConfigure<Interpolator>>> InterpFactories = new()
    {
        [typeof(LinearInterpolatorConfigure)] = () => new LinearInterpolatorConfigure(),
        [typeof(SlopeAccelInterpolatorConfigure)] = () => new SlopeAccelInterpolatorConfigure(),
        [typeof(AccelInterpolatorConfigure)] = () => new AccelInterpolatorConfigure(),
    };

    public MainForm()
    {
        Text = Localization.Get("title");
        Width = 1400; Height = 860; MinimumSize = new Size(1080, 680);
        StartPosition = FormStartPosition.CenterScreen;
        var iconPath = AssetPaths.Get("mandelbrot1.png");
        if (File.Exists(iconPath)) try { Icon = Icon.ExtractAssociatedIcon(iconPath); } catch { }

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize));
        Controls.Add(root);

        var menu = BuildMenu(); MainMenuStrip = menu; root.Controls.Add(menu, 0, 0);

        // Left: the wide fractal source panel (with its big live preview). Right: tabs for the
        // secondary settings so they don't crowd the window.
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        body.ColumnStyles.Add(new(SizeType.Percent, 65));
        body.ColumnStyles.Add(new(SizeType.Percent, 35));
        root.Controls.Add(body, 0, 1);

        _loaderPanel = Grp(Localization.Get("panel.image"));
        body.Controls.Add(_loaderPanel, 0, 0);
        _frameNum = new Label { Text = Localization.Get("label.empty"), Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
        _loaderPanel.Controls.Add(_frameNum);
        _loaderSel = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var t in LoaderFactories.Keys) _loaderSel.Items.Add(t);
        _loaderSel.Format += (_, e) => { if (e.ListItem is Type t) e.Value = Localization.Get("class.hywt.fractal.animator.ui." + t.Name); };
        _loaderSel.SelectedIndexChanged += (_, _) => SwapLoader();
        _loaderPanel.Controls.Add(_loaderSel);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        body.Controls.Add(tabs, 1, 0);

        var timelinePage = new TabPage(Localization.Get("panel.interpolator")) { Padding = new Padding(6) };
        _interpPanel = new Panel { Dock = DockStyle.Fill };
        _interpSel = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var t in InterpFactories.Keys) _interpSel.Items.Add(t);
        _interpSel.Format += (_, e) => { if (e.ListItem is Type t) e.Value = Localization.Get("class.hywt.fractal.animator.ui." + t.Name); };
        _interpSel.SelectedIndexChanged += (_, _) => SwapInterp();
        _interpPanel.Controls.Add(_interpSel);
        timelinePage.Controls.Add(_interpPanel);
        tabs.TabPages.Add(timelinePage);

        var labelsPage = new TabPage("Scale labels") { Padding = new Padding(6) };
        _indiPanel = new IndicatorSelectorPanel([
            typeof(KfScaleIndicator), typeof(FxScaleIndicator), typeof(OdometerIndicator),
            typeof(ExponentialScaleIndicator), typeof(GoogologyIndicator)
        ]) { Dock = DockStyle.Fill };
        labelsPage.Controls.Add(_indiPanel);
        tabs.TabPages.Add(labelsPage);

        var outputPage = new TabPage(Localization.Get("gen_options.title")) { Padding = new Padding(6) };
        _genOpts = new GenOptionsPanel { Dock = DockStyle.Fill };
        outputPage.Controls.Add(_genOpts);
        tabs.TabPages.Add(outputPage);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        bottom.RowStyles.Add(new(SizeType.AutoSize));
        bottom.RowStyles.Add(new(SizeType.AutoSize));
        root.Controls.Add(bottom, 0, 2);

        _progressPanel = new ProgressPanel { Dock = DockStyle.Top, Height = 22 };
        bottom.Controls.Add(_progressPanel, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2) };
        _browseBtn = new Button { Text = Localization.Get("label.browse"), Width = 150, Height = 34 };
        _browseBtn.Click += (_, _) => { try { if (_loaderCfg?.Get() is ImageLoader l) using (var b = new ImageBrowser(l)) b.ShowDialog(this); else ShowError(Localization.Get("message.missing_image")); } catch (Exception ex) { ShowError(ex); } };
        actions.Controls.Add(_browseBtn);
        _genBtn = new Button { Text = Localization.Get("label.generate"), Width = 180, Height = 34, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
        _genBtn.Click += async (_, _) => { try { if (_loaderCfg?.Get() != null) { if (!_rendering) await Generate(); else _renderer?.Abort(); } else ShowError(Localization.Get("message.missing_image")); } catch (Exception ex) { ShowError(ex); } };
        actions.Controls.Add(_genBtn);
        var simdInfo = new Label { Text = PerturbationEngine.SimdAccelerated ? $"  Engine: SIMD ×{PerturbationEngine.VectorLanes} · {Environment.ProcessorCount} cores" : "  Engine: scalar", AutoSize = true, ForeColor = Theme.TextDim, Padding = new Padding(8, 10, 0, 0) };
        actions.Controls.Add(simdInfo);
        bottom.Controls.Add(actions, 0, 1);

        _tips.SetToolTip(_browseBtn, "Render and scroll through the keyframes before exporting a video.");
        _tips.SetToolTip(_genBtn, "Render the zoom to a video file with ffmpeg.");
        _tips.SetToolTip(_loaderSel, "Choose where keyframes come from. The formula studio compiles your own SIMD fractal at runtime.");

        Theme.Apply(this);
        Load += (_, _) => Theme.DarkTitleBar(this);

        if (File.Exists("default.fap"))
            try { ImportJson(JsonNode.Parse(File.ReadAllText("default.fap", Encoding.UTF8))!.AsObject()); } catch { }

        _loaderSel.SelectedIndex = 0; _interpSel.SelectedIndex = 0;
    }

    public JsonObject ExportJson()
    {
        var o = new JsonObject { ["genOptions"] = _genOpts.ExportJson(), ["indicators"] = _indiPanel.ExportJson() };
        if (_loaderCfg != null) o["loader"] = new JsonObject { ["type"] = _loaderCfg.GetType().FullName, ["data"] = _loaderCfg.ExportJson() };
        if (_interpCfg != null) o["interpolator"] = new JsonObject { ["type"] = _interpCfg.GetType().FullName, ["data"] = _interpCfg.ExportJson() };
        return o;
    }
    public void ImportJson(JsonObject o)
    {
        if (o["genOptions"] is JsonObject g) _genOpts.ImportJson(g);
        if (o["loader"] is JsonObject lo)
        {
            var tn = lo.GetString("type"); var t = Resolve(tn, LoaderFactories.Keys);
            if (t != null) { _loaderSel.SelectedItem = t; _loaderCfg = LoaderFactories[t](); _loaderCfg.SetOnLoadCallable(async () => { _frameNum.Text = string.Format(Localization.Get("image.amount"), _loaderCfg.Get()?.Size() ?? 0); await Task.CompletedTask; }); _loaderCfg.Init(); if (lo["data"] is JsonObject ld) _loaderCfg.ImportJson(ld); Repl(_loaderPanel, _loaderCfg, _loaderSel, _frameNum); }
        }
        if (o["interpolator"] is JsonObject io)
        {
            var tn = io.GetString("type"); var t = Resolve(tn, InterpFactories.Keys);
            if (t != null) { _interpSel.SelectedItem = t; _interpCfg = InterpFactories[t](); _interpCfg.Init(); if (io["data"] is JsonObject id) _interpCfg.ImportJson(id); Repl(_interpPanel, _interpCfg, _interpSel); }
        }
        if (o["indicators"] is JsonObject ind) _indiPanel.ImportJson(ind);
    }

    async Task Generate()
    {
        _renderer = new VideoRenderer();
        var mgr = _loaderCfg?.Get(); var interp = _interpCfg?.Get();
        if (mgr == null || interp == null) { ShowError(Localization.Get("message.missing_image")); return; }
        _renderer.SetInterpolator(interp); _renderer.SetMergeFrames(_genOpts.GetBlend());
        var sel = _indiPanel.GetSelected();
        if (sel.Count == 0)
        {
            if (MessageBox.Show(this, Localization.Get("message.missing_indicator"), Localization.Get("message.warning"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        }
        foreach (var t in sel) { if (Activator.CreateInstance(t) is IScaleIndicator ind) { ind.SetFont(_indiPanel.GetSelectedFont()); _renderer.AddScaleIndicator(ind); } }
        using var dlg = new SaveFileDialog { Filter = Localization.Get("file.flv") + "|*.mp4;*.flv;*.mkv;*.mov", DefaultExt = "mp4", AddExtension = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _progressPanel.Start(_renderer); SetRendering(true);
        try
        {
            var p = new RenderParams(_genOpts.GetW(), _genOpts.GetH(), _genOpts.GetFps(), _genOpts.GetBlend(), _genOpts.GetSt(), _genOpts.GetEt(), _genOpts.GetFfmpeg(), _genOpts.GetEnc());
            await Task.Run(() => _renderer.FfmpegRender(mgr, p, dlg.FileName));
            MessageBox.Show(this, Localization.Get("message.completed"), Localization.Get("message.info"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) { /* user pressed Stop */ }
        catch (Exception ex) { ShowError(ex); }
        finally { SetRendering(false); _progressPanel.StopProgress(); }
    }

    void SwapLoader()
    {
        if (_loaderSel.SelectedItem is not Type t) return;
        _loaderCfg = LoaderFactories[t](); _loaderCfg.SetOnLoadCallable(async () => { _frameNum.Text = string.Format(Localization.Get("image.amount"), _loaderCfg.Get()?.Size() ?? 0); await Task.CompletedTask; });
        _frameNum.Text = Localization.Get("label.empty"); _loaderCfg.Init(); Repl(_loaderPanel, _loaderCfg, _loaderSel, _frameNum);
    }
    void SwapInterp()
    {
        if (_interpSel.SelectedItem is not Type t) return;
        _interpCfg = InterpFactories[t](); _interpCfg.Init(); Repl(_interpPanel, _interpCfg, _interpSel);
    }

    static void Repl(Panel parent, Control content, params Control[] keep)
    {
        foreach (var c in parent.Controls.OfType<Control>().ToArray()) { if (!keep.Contains(c)) { parent.Controls.Remove(c); c.Dispose(); } }
        content.Dock = DockStyle.Fill; parent.Controls.Add(content); content.BringToFront();
        Theme.Apply(content);
    }

    MenuStrip BuildMenu()
    {
        var m = new MenuStrip(); var f = new ToolStripMenuItem(Localization.Get("menu.files"));
        var load = new ToolStripMenuItem(Localization.Get("menu.files.load"));
        load.Click += (_, _) => { using var d = new OpenFileDialog { Filter = Localization.Get("file.fap") + "|*.fap", DefaultExt = "fap" }; if (d.ShowDialog(this) == DialogResult.OK) ImportJson(JsonNode.Parse(File.ReadAllText(d.FileName, Encoding.UTF8).Trim())!.AsObject()); };
        f.DropDownItems.Add(load);
        var save = new ToolStripMenuItem(Localization.Get("menu.files.save"));
        save.Click += (_, _) => { using var d = new SaveFileDialog { Filter = Localization.Get("file.fap") + "|*.fap", DefaultExt = "fap", AddExtension = true }; if (d.ShowDialog(this) == DialogResult.OK) File.WriteAllText(d.FileName, ExportJson().ToJsonString(new JsonSerializerOptions { WriteIndented = false }), Encoding.UTF8); };
        f.DropDownItems.Add(save); m.Items.Add(f); return m;
    }

    void SetRendering(bool v) { _rendering = v; _browseBtn.Enabled = !v; if (_loaderCfg != null) _loaderCfg.Enabled = !v; _genBtn.Text = v ? Localization.Get("label.abort") : Localization.Get("label.generate"); _genBtn.BackColor = v ? Theme.Bad : Theme.Surface; }
    void ShowError(Exception e) => ShowError(e.Message + "\n" + e);
    void ShowError(string m) => MessageBox.Show(this, m, Localization.Get("message.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
    static Panel Grp(string title)
    {
        var p = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Padding = new(4), Tag = "card", BackColor = Theme.SurfaceAlt };
        p.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 20, TextAlign = ContentAlignment.MiddleCenter, Font = new(SystemFonts.DefaultFont, FontStyle.Bold), ForeColor = Theme.Text }); return p;
    }
    static Type? Resolve(string name, IEnumerable<Type> candidates)
    {
        var t = Type.GetType(name); if (t != null) return candidates.FirstOrDefault(c => c == t || c.FullName == t.FullName);
        return candidates.FirstOrDefault(c => c.FullName == name || c.Name == name.Split(',').First().Split('.').Last());
    }
}
