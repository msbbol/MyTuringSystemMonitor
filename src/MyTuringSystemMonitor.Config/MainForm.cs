using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MyTuringSystemMonitor.Configuration;
using MyTuringSystemMonitor.Hardware;
using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;

namespace MyTuringSystemMonitor.Config;

/// <summary>
/// Configuration UI (mirrors the mathoudebine configurator): edit display + monitoring settings,
/// preview the selected theme live, and save/run. Reads and writes the shared config.json.
/// </summary>
public sealed class MainForm : Form
{
    private readonly AppConfig _config;

    private readonly PictureBox _preview = new();
    private readonly ComboBox _model = new();
    private readonly ComboBox _port = new();
    private readonly ComboBox _orientation = new();
    private readonly TrackBar _brightness = new();
    private readonly Label _brightnessValue = new();
    private readonly ComboBox _theme = new();
    private readonly ComboBox _sensors = new();
    private readonly ComboBox _display = new();
    private readonly NumericUpDown _refresh = new();

    public MainForm()
    {
        _config = ConfigLoader.LoadOrCreate(AppPaths.ConfigPath);

        Text = "Turing System Monitor configuration";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 620);
        MinimumSize = new Size(760, 620);
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.White;

        BuildLayout();
        LoadValues();
        UpdatePreview();
    }

    // ---- Layout -------------------------------------------------------------------------------

    private void BuildLayout()
    {
        // Left: live preview.
        _preview.Location = new Point(20, 20);
        _preview.Size = new Size(300, 470);
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.BorderStyle = BorderStyle.None;
        _preview.BackColor = Color.FromArgb(245, 245, 245);
        Controls.Add(_preview);

        var author = new Label
        {
            Text = "Themes: @mathoudebine • App: MyTuringSystemMonitor",
            ForeColor = Color.FromArgb(150, 150, 170),
            AutoSize = true,
            Location = new Point(20, 496),
        };
        Controls.Add(author);

        var rightX = 350;
        var labelW = 170;
        var controlX = rightX + labelW;
        var controlW = 340;
        var y = 24;

        Controls.Add(SectionHeader("Display configuration", rightX, ref y));

        _model.DropDownStyle = ComboBoxStyle.DropDownList;
        _model.Items.AddRange(new object[]
        {
            new Item("Turing 3.5\" (Revision A)", "RevisionA"),
            new Item("Revision B (3.5\")", "RevisionB"),
            new Item("Revision C (5\")", "RevisionC"),
            new Item("Revision E (8.8\")", "RevisionE"),
        });
        AddRow("Smart screen model", _model, rightX, labelW, controlX, controlW, ref y);

        _port.DropDownStyle = ComboBoxStyle.DropDown;
        AddRow("COM port", _port, rightX, labelW, controlX, controlW, ref y);

        _orientation.DropDownStyle = ComboBoxStyle.DropDownList;
        _orientation.Items.AddRange(new object[]
        {
            new Item("Portrait", "Portrait"),
            new Item("Reverse portrait", "ReversePortrait"),
            new Item("Landscape", "Landscape"),
            new Item("Reverse landscape", "ReverseLandscape"),
        });
        _orientation.SelectedIndexChanged += (_, _) => UpdatePreview();
        AddRow("Orientation", _orientation, rightX, labelW, controlX, controlW, ref y);

        // Brightness row (trackbar + % label).
        Controls.Add(new Label { Text = "Brightness", Location = new Point(rightX, y + 4), AutoSize = true });
        _brightnessValue.Location = new Point(controlX, y + 4);
        _brightnessValue.AutoSize = true;
        Controls.Add(_brightnessValue);
        _brightness.Minimum = 0;
        _brightness.Maximum = 100;
        _brightness.TickFrequency = 10;
        _brightness.SmallChange = 1;
        _brightness.LargeChange = 10;
        _brightness.Location = new Point(controlX + 44, y);
        _brightness.Width = controlW - 44;
        _brightness.Scroll += (_, _) => _brightnessValue.Text = $"{_brightness.Value}%";
        Controls.Add(_brightness);
        y += 56;

        Controls.Add(SectionHeader("System Monitor Configuration", rightX, ref y));

        _theme.DropDownStyle = ComboBoxStyle.DropDownList;
        _theme.SelectedIndexChanged += (_, _) => OnThemeChanged();
        AddRow("Theme", _theme, rightX, labelW, controlX, controlW, ref y);

        _sensors.DropDownStyle = ComboBoxStyle.DropDownList;
        _sensors.Items.AddRange(new object[]
        {
            new Item("Automatic (LibreHardwareMonitor, needs admin)", "Lhm"),
            new Item("Performance counters (CPU/RAM, no admin)", "PerfCounter"),
            new Item("Fake (demo data)", "Fake"),
        });
        AddRow("Hardware monitoring", _sensors, rightX, labelW, controlX, controlW, ref y);

        _display.DropDownStyle = ComboBoxStyle.DropDownList;
        _display.Items.AddRange(new object[]
        {
            new Item("Turing Smart Screen", "Turing"),
            new Item("None (headless dry run)", "Null"),
        });
        AddRow("Display output", _display, rightX, labelW, controlX, controlW, ref y);


        _refresh.Minimum = 100;
        _refresh.Maximum = 60000;
        _refresh.Increment = 100;
        AddRow("Refresh interval (ms)", _refresh, rightX, labelW, controlX, controlW, ref y);

        BuildButtons();
    }

    private void BuildButtons()
    {
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 60,
            Padding = new Padding(12),
            BackColor = Color.FromArgb(248, 248, 250),
        };

        buttons.Controls.Add(MakeButton("Save and run", 130, OnSaveAndRun, primary: true));
        buttons.Controls.Add(MakeButton("Save settings", 120, OnSave));
        buttons.Controls.Add(MakeButton("Edit theme", 110, OnEditTheme));
        buttons.Controls.Add(MakeButton("Open themes folder", 150, OnOpenThemes));
        Controls.Add(buttons);
    }

    private static Label SectionHeader(string text, int x, ref int y)
    {
        var label = new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 14f, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(x, y),
        };
        y += 44;
        return label;
    }

    private void AddRow(string caption, Control control, int labelX, int labelW, int controlX, int controlW, ref int y)
    {
        Controls.Add(new Label { Text = caption, Location = new Point(labelX, y + 4), Size = new Size(labelW, 24) });
        control.Location = new Point(controlX, y);
        control.Width = controlW;
        Controls.Add(control);
        y += 46;
    }

    private Button MakeButton(string text, int width, EventHandler onClick, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 34,
            FlatStyle = FlatStyle.System,
            Margin = new Padding(6, 0, 6, 0),
        };
        if (primary)
        {
            button.BackColor = Color.FromArgb(45, 120, 220);
            button.ForeColor = Color.White;
            button.FlatStyle = FlatStyle.Flat;
        }
        button.Click += onClick;
        return button;
    }

    // ---- Values <-> config --------------------------------------------------------------------

    private void LoadValues()
    {
        // COM ports.
        _port.Items.Add("AUTO");
        foreach (var p in ComPortResolver.AvailablePorts())
            _port.Items.Add(p);
        _port.Text = _config.ComPort;

        SelectByTag(_model, _config.Revision, 0);
        SelectByTag(_orientation, _config.Orientation, 0);
        SelectByTag(_sensors, _config.SensorProvider, 0);
        SelectByTag(_display, _config.DisplayProvider, 0);

        _brightness.Value = Math.Clamp(_config.Brightness, 0, 100);
        _brightnessValue.Text = $"{_brightness.Value}%";
        _refresh.Value = Math.Clamp(_config.RefreshIntervalMs, (int)_refresh.Minimum, (int)_refresh.Maximum);

        // Themes.
        foreach (var entry in ThemeCatalog.Discover(AppPaths.ThemesDir))
            _theme.Items.Add(entry.Name);
        if (_theme.Items.Count == 0)
            _theme.Items.Add("default");
        var idx = _theme.Items.IndexOf(_config.Theme ?? "default");
        _theme.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void ApplyToConfig()
    {
        _config.Revision = TagOf(_model) ?? _config.Revision;
        _config.ComPort = string.IsNullOrWhiteSpace(_port.Text) ? "COM4" : _port.Text.Trim();
        _config.Orientation = TagOf(_orientation) ?? _config.Orientation;
        _config.Brightness = _brightness.Value;
        _config.Theme = _theme.SelectedItem?.ToString() ?? _config.Theme;
        _config.SensorProvider = TagOf(_sensors) ?? _config.SensorProvider;
        _config.DisplayProvider = TagOf(_display) ?? _config.DisplayProvider;
        _config.RefreshIntervalMs = (int)_refresh.Value;
    }

    // ---- Preview ------------------------------------------------------------------------------

    /// <summary>
    /// Picking a theme rotates the screen to the axis it was designed for, keeping the user's
    /// reverse-variant choice. Then the preview redraws at the new canvas size.
    /// </summary>
    private void OnThemeChanged()
    {
        try
        {
            var name = _theme.SelectedItem?.ToString();
            var theme = ThemeCatalog.LoadByName(AppPaths.ThemesDir, name, AppPaths.FontsDir);
            if (theme.Orientation is { } axis)
            {
                var aligned = AppConfig.AlignOrientation(
                    TagOf(_orientation) ?? _config.Orientation,
                    axis == ThemeOrientation.Landscape);
                SelectByTag(_orientation, aligned, 0);   // fires SelectedIndexChanged -> UpdatePreview
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Theme orientation sync failed: {ex}");
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        try
        {
            var name = _theme.SelectedItem?.ToString() ?? _config.Theme;
            var theme = ThemeCatalog.LoadByName(AppPaths.ThemesDir, name, AppPaths.FontsDir);
            using var fake = new FakeSensorProvider();
            fake.Initialize();
            var snapshot = fake.Read();
            var (w, h) = PreviewSize();
            var frame = new GdiThemeRenderer().Render(theme, snapshot, w, h);
            _preview.Image?.Dispose();
            _preview.Image = frame;
        }
        catch (Exception ex)
        {
            _preview.Image?.Dispose();
            _preview.Image = null;
            _preview.BackColor = Color.FromArgb(250, 235, 235);
            Debug.WriteLine($"Preview failed: {ex}");
        }
    }

    // ---- Buttons ------------------------------------------------------------------------------

    private void OnSave(object? sender, EventArgs e)
    {
        Save();
        MessageBox.Show(this, $"Settings saved to:\n{AppPaths.ConfigPath}", "Saved",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void OnSaveAndRun(object? sender, EventArgs e)
    {
        Save();

        // If the Windows service is running it owns the COM port — ask it to apply the settings
        // rather than starting a second monitor that would fight it for the screen.
        if (await TryRestartServiceAsync())
        {
            MessageBox.Show(this, "Settings applied: the monitor service restarted with them.",
                "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var exe = ResolveConsoleExe();
        if (exe is null)
        {
            MessageBox.Show(this,
                "Could not find MyTuringSystemMonitor.exe. Build the console project, then run it manually:\n\n" +
                "dotnet run --project src/MyTuringSystemMonitor",
                "Runner not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            // Stop any monitor that's already running so the new one can take the COM port,
            // then start fresh with the just-saved settings.
            StopRunningMonitors();
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to start the monitor:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>POSTs a restart to the local service's control API; false if the service isn't there.</summary>
    private async Task<bool> TryRestartServiceAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var request = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Post, $"http://localhost:{_config.WebPort}/api/control/restart");
            request.Headers.Add("X-Turing-Monitor", "1");   // the service refuses writes without it
            using var response = await http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Stops any running console-runner instances and waits for the COM port to be released.</summary>
    private static void StopRunningMonitors()
    {
        var stoppedAny = false;
        foreach (var process in Process.GetProcessesByName("MyTuringSystemMonitor"))
        {
            try
            {
                if (process.Id == Environment.ProcessId) continue; // never ourselves (different name anyway)
                process.Kill();
                process.WaitForExit(3000);
                stoppedAny = true;
            }
            catch
            {
                // access denied / already gone — ignore and try to start anyway
            }
            finally
            {
                process.Dispose();
            }
        }

        // Give Windows a moment to release the serial handle before the new instance opens it.
        if (stoppedAny)
            Thread.Sleep(400);
    }

    private void OnOpenThemes(object? sender, EventArgs e)
    {
        Directory.CreateDirectory(AppPaths.ThemesDir);
        Process.Start(new ProcessStartInfo(AppPaths.ThemesDir) { UseShellExecute = true });
    }

    private void OnEditTheme(object? sender, EventArgs e)
    {
        var name = _theme.SelectedItem?.ToString();
        var entry = ThemeCatalog.Discover(AppPaths.ThemesDir)
            .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            MessageBox.Show(this, "Select a theme first.", "Edit theme",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
    }

    private void Save()
    {
        ApplyToConfig();
        ConfigLoader.Save(AppPaths.ConfigPath, _config);
    }

    private static string? ResolveConsoleExe()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "MyTuringSystemMonitor.exe"),
            Path.Combine(
                AppContext.BaseDirectory.Replace("MyTuringSystemMonitor.Config", "MyTuringSystemMonitor"),
                "MyTuringSystemMonitor.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    // ---- Small helpers ------------------------------------------------------------------------

    private (int width, int height) PreviewSize()
    {
        var lo = Math.Min(_config.Width, _config.Height);
        var hi = Math.Max(_config.Width, _config.Height);
        var tag = TagOf(_orientation);
        var landscape = tag is "Landscape" or "ReverseLandscape";
        return landscape ? (hi, lo) : (lo, hi);
    }

    private static void SelectByTag(ComboBox combo, string? tag, int fallback)
    {
        for (var i = 0; i < combo.Items.Count; i++)
            if (combo.Items[i] is Item item && string.Equals(item.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        if (combo.Items.Count > 0)
            combo.SelectedIndex = Math.Min(fallback, combo.Items.Count - 1);
    }

    private static string? TagOf(ComboBox combo) => (combo.SelectedItem as Item)?.Tag;

    /// <summary>A combo entry with a display label and an underlying config value.</summary>
    private sealed record Item(string Label, string Tag)
    {
        public override string ToString() => Label;
    }
}
