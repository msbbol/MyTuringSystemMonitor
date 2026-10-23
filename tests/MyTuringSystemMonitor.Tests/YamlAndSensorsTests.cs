using System.Net.NetworkInformation;
using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;
using Xunit;

namespace MyTuringSystemMonitor.Tests;

public class YamlAndSensorsTests
{
    [Fact]
    public void TextWidget_applies_scale_and_unit()
    {
        // MHz -> GHz: scale 0.001, unit " GHz".
        var widget = new TextWidget { Sensor = SensorKeys.CpuClock, Format = "{0:F2}", Scale = 0.001, Unit = " GHz" };
        var snapshot = new SensorSnapshot.Builder().Set(SensorKeys.CpuClock, 2400.0).Build();
        Assert.Equal("2.40 GHz", widget.ResolveText(snapshot));
    }

    [Fact]
    public void MathoudebineThemeLoader_maps_yaml_to_widgets()
    {
        const string yaml = """
        static_images:
          BACKGROUND: { PATH: background.png, X: 0, Y: 0, WIDTH: 320, HEIGHT: 480 }
        static_text:
          DISK_USED_LABEL: { TEXT: "Used:", X: 1, Y: 2, FONT_SIZE: 23, FONT_COLOR: "255, 255, 255" }
        STATS:
          CPU:
            PERCENTAGE:
              TEXT:  { SHOW: True, SHOW_UNIT: True, X: 250, Y: 24, FONT_SIZE: 23, FONT_COLOR: "255, 255, 255" }
              GRAPH: { SHOW: True, X: 155, Y: 55, WIDTH: 150, HEIGHT: 15, MIN_VALUE: 0, MAX_VALUE: 100, BAR_COLOR: "0, 0, 255", BACKGROUND_COLOR: "0, 0, 0" }
          DATE:
            DAY:
              TEXT: { SHOW: True, X: 155, Y: 6, FONT_SIZE: 10, FONT_COLOR: "255, 255, 255" }
        """;

        var dir = Path.Combine(Path.GetTempPath(), "mtsm-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "theme.yaml");
        File.WriteAllText(path, yaml);

        try
        {
            var theme = MathoudebineThemeLoader.Load(path, fontsDir: dir);

            // Static label.
            var label = theme.Widgets.OfType<TextWidget>().First(w => w.Label == "Used:");
            Assert.Equal(1, label.X);
            Assert.True(label.PixelFontSize);

            // CPU percentage text bound to cpu.load with a "%" unit.
            var cpu = theme.Widgets.OfType<TextWidget>().First(w => w.Sensor == SensorKeys.CpuLoad);
            Assert.Equal("%", cpu.Unit);
            Assert.Equal(250, cpu.X);

            // CPU graph -> gauge bound to cpu.load.
            var gauge = theme.Widgets.OfType<GaugeWidget>().First(w => w.Sensor == SensorKeys.CpuLoad);
            Assert.Equal(150, gauge.Width);
            Assert.Equal("#0000FF", gauge.Color);

            // Date widget present.
            Assert.Contains(theme.Widgets, w => w is DateTimeWidget);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(50, 0.5)]
    [InlineData(100, 1.0)]
    [InlineData(200, 1.0)] // clamped
    public void RadialWidget_resolves_clamped_fraction(double value, double expected)
    {
        var widget = new RadialWidget { Sensor = SensorKeys.CpuLoad, Min = 0, Max = 100 };
        var snapshot = new SensorSnapshot.Builder().Set(SensorKeys.CpuLoad, value).Build();
        Assert.Equal(expected, widget.ResolveFraction(snapshot), 3);
    }

    [Fact]
    public void GpuEngineMeter_reports_the_busiest_adapter_like_task_manager()
    {
        const string igpu = "luid_0x00000000_0x00016463";
        const string dgpu = "luid_0x00000000_0x00016709";

        var readings = new (string, double)[]
        {
            // Two processes on the integrated GPU's 3D engine: an engine's load is their sum.
            ($"pid_100_{igpu}_phys_0_eng_0_engtype_3D", 4.6),
            ($"pid_200_{igpu}_phys_0_eng_0_engtype_3D", 6.7),
            // Another engine on the same adapter doesn't add to it — the adapter shows its busiest engine.
            ($"pid_200_{igpu}_phys_0_eng_3_engtype_VideoDecode", 9.0),
            // The sleeping discrete GPU of a switchable-graphics laptop.
            ($"pid_300_{dgpu}_phys_0_eng_2_engtype_3D", 0.0),
            ("garbage instance name", 99),
        };

        Assert.Equal(11.3, GpuEngineMeter.BusiestAdapter(readings)!.Value, 3);
    }

    [Fact]
    public void GpuEngineMeter_follows_the_discrete_gpu_when_it_is_busy_and_clamps()
    {
        var readings = new (string, double)[]
        {
            ("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 12),
            ("pid_2_luid_0x0_0x2_phys_0_eng_0_engtype_3D", 70),
            ("pid_3_luid_0x0_0x2_phys_0_eng_0_engtype_3D", 45),
        };

        Assert.Equal(100, GpuEngineMeter.BusiestAdapter(readings));
        Assert.Null(GpuEngineMeter.BusiestAdapter([]));
    }

    [Fact]
    public void Chart_widget_loads_from_json_and_keeps_a_history_per_series()
    {
        const string json = """
        { "name": "t", "widgets": [
          { "type": "chart", "x": 0, "y": 0, "width": 100, "height": 40, "maxSamples": 3,
            "series": [ { "sensor": "cpu.load", "color": "#35E0D0" }, { "sensor": "gpu.mem.load", "color": "#A3E635" } ] }
        ] }
        """;
        var chart = Assert.IsType<ChartWidget>(Assert.Single(ThemeLoader.Parse(json).Widgets));
        Assert.Equal(2, chart.Series.Count);
        Assert.Equal("#A3E635", chart.Series[1].Color);

        using var bitmap = new System.Drawing.Bitmap(100, 40);
        using var g = System.Drawing.Graphics.FromImage(bitmap);
        foreach (var cpu in new double[] { 10, 20, 30, 40 })
            chart.Render(g, new SensorSnapshot.Builder().Set(SensorKeys.CpuLoad, cpu).Build());

        Assert.Equal([20, 30, 40], chart.History(0));          // capped at maxSamples, newest last
        Assert.All(chart.History(1), v => Assert.True(double.IsNaN(v)));   // missing sensor = gap, not 0
    }

    [Fact]
    public void FrameRateMeter_reports_the_busiest_app_and_ignores_the_compositor()
    {
        var frames = new Dictionary<string, int>
        {
            ["dwm"] = 600,        // the Windows compositor always presents — never the answer
            ["game"] = 288,
            ["chrome"] = 120,
        };

        var busiest = FrameRateMeter.Busiest(frames, seconds: 2, minimumFps: 5);
        Assert.Equal("game", busiest!.Value.App);
        Assert.Equal(144, busiest.Value.Fps, 3);
    }

    [Fact]
    public void FrameRateMeter_reports_nothing_when_idle()
    {
        // Only the compositor, or a trickle of frames from a desktop app: no FPS worth showing.
        Assert.Null(FrameRateMeter.Busiest(new Dictionary<string, int> { ["dwm"] = 600 }, 2, 5));
        Assert.Null(FrameRateMeter.Busiest(new Dictionary<string, int> { ["explorer"] = 4 }, 2, 5));
        Assert.Null(FrameRateMeter.Busiest(new Dictionary<string, int> { ["game"] = 100 }, 0, 5));
    }

    [Fact]
    public void SystemMetrics_reports_physical_memory_consistently()
    {
        var builder = new SensorSnapshot.Builder();
        SystemMetrics.AppendMemory(builder);
        var s = builder.Build();

        var total = s.Get(SensorKeys.RamTotal)!.Value;
        var used = s.Get(SensorKeys.RamUsed)!.Value;
        var free = s.Get(SensorKeys.RamFree)!.Value;

        Assert.True(total > 0.5, $"total RAM {total} GB looks wrong");
        Assert.Equal(total, used + free, 3);
        Assert.InRange(s.Get(SensorKeys.RamLoad)!.Value, 0, 100);
        Assert.Equal(used / total * 100, s.Get(SensorKeys.RamLoad)!.Value, 3);
    }

    [Fact]
    public void Both_real_providers_report_the_same_memory_keys()
    {
        // LHM used to read memory from two same-named devices (Total vs Virtual) and the
        // performance-counter provider had no used/free at all; both now share one source.
        using var counters = new PerformanceCounterSensorProvider();
        counters.Initialize();
        var snapshot = counters.Read();

        foreach (var key in new[] { SensorKeys.RamLoad, SensorKeys.RamUsed, SensorKeys.RamFree, SensorKeys.RamTotal })
            Assert.NotNull(snapshot.Get(key));
    }

    [Fact]
    public void MathoudebineThemeLoader_maps_memory_free_total_and_swap()
    {
        const string yaml = """
        STATS:
          MEMORY:
            VIRTUAL:
              FREE:  { SHOW: True, SHOW_UNIT: True, X: 10, Y: 20, FONT_SIZE: 12 }
              TOTAL: { SHOW: True, SHOW_UNIT: True, X: 10, Y: 40, FONT_SIZE: 12 }
            SWAP:
              GRAPH: { SHOW: True, X: 10, Y: 60, WIDTH: 100, HEIGHT: 8 }
        """;

        var dir = Path.Combine(Path.GetTempPath(), "mtsm-mem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "theme.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var theme = MathoudebineThemeLoader.Load(path, dir);

            var free = theme.Widgets.OfType<TextWidget>().Single(w => w.Sensor == SensorKeys.RamFree);
            Assert.Equal(1024, free.Scale);          // GB sensor shown in MB
            Assert.Equal(" M", free.Unit);
            Assert.Contains(theme.Widgets, w => w is TextWidget t && t.Sensor == SensorKeys.RamTotal);
            Assert.Contains(theme.Widgets, w => w is GaugeWidget g && g.Sensor == SensorKeys.SwapLoad);

            var snapshot = new SensorSnapshot.Builder().Set(SensorKeys.RamFree, 18.5).Build();
            Assert.Equal("18944 M", free.ResolveText(snapshot));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SystemMetrics_reports_disk_usage()
    {
        var builder = new SensorSnapshot.Builder();
        SystemMetrics.AppendDisk(builder);
        var snapshot = builder.Build();

        var total = snapshot.Get(SensorKeys.DiskTotal);
        var load = snapshot.Get(SensorKeys.DiskLoad);

        Assert.NotNull(total);
        Assert.True(total > 0);
        Assert.NotNull(load);
        Assert.InRange(load!.Value, 0, 100);
    }

    [Theory]
    [InlineData(7_400_000, "↓ 7.4M")]
    [InlineData(860_000, "↓ 860K")]
    [InlineData(2_100_000_000, "↓ 2.1G")]
    [InlineData(512, "↓ 512")]
    public void TextWidget_humanizes_byte_rates(double bytes, string expected)
    {
        var widget = new TextWidget { Label = "↓ ", Sensor = SensorKeys.NetDown, Humanize = "bytes" };
        var snapshot = new SensorSnapshot.Builder().Set(SensorKeys.NetDown, bytes).Build();
        Assert.Equal(expected, widget.ResolveText(snapshot));
    }

    [Fact]
    public void TextWidget_binds_to_text_sensor()
    {
        var widget = new TextWidget { TextSensor = SensorKeys.ProcTopName, Fallback = "--" };
        var withValue = new SensorSnapshot.Builder().SetText(SensorKeys.ProcTopName, "firefox").Build();
        Assert.Equal("firefox", widget.ResolveText(withValue));
        Assert.Equal("--", widget.ResolveText(SensorSnapshot.Empty));
    }

    [Fact]
    public void SystemMetrics_reports_clock_pseudo_sensors()
    {
        var builder = new SensorSnapshot.Builder();
        SystemMetrics.AppendTime(builder);
        var s = builder.Build();

        Assert.InRange(s.Get(SensorKeys.TimeSecond)!.Value, 0, 60);
        Assert.InRange(s.Get(SensorKeys.TimeMinute)!.Value, 0, 60);
        Assert.InRange(s.Get(SensorKeys.TimeHour12)!.Value, 0, 12);
        Assert.True(s.Get(SensorKeys.UptimeHours)!.Value >= 0);
    }

    [Fact]
    public void ThemeLoader_parses_gradient_and_custom_widget_types()
    {
        const string json = """
        {
          "name": "t",
          "background": "#111111",
          "backgroundGradientTo": "#222222",
          "widgets": [
            { "type": "panel", "x": 1, "y": 2, "width": 10, "height": 20, "fill": "#333333", "cornerRadius": 5 },
            { "type": "clock", "centerX": 50, "centerY": 60, "radius": 30 },
            { "type": "datetime", "format": "HH:mm", "x": 1, "y": 1 },
            { "type": "radial", "centerX": 5, "centerY": 6, "radius": 10, "sensor": "cpu.load" },
            { "type": "sparkline", "x": 5, "y": 6, "width": 40, "height": 20, "sensor": "cpu.load", "style": "line" }
          ]
        }
        """;

        var theme = ThemeLoader.Parse(json);
        Assert.Equal("#222222", theme.BackgroundGradientTo);
        Assert.Contains(theme.Widgets, w => w is PanelWidget);
        Assert.Contains(theme.Widgets, w => w is AnalogClockWidget);
        Assert.Contains(theme.Widgets, w => w is DateTimeWidget);
        Assert.Contains(theme.Widgets, w => w is RadialWidget);
        Assert.Contains(theme.Widgets, w => w is SparklineWidget);
    }

    [Fact]
    public void ThemeCatalog_discovers_json_and_yaml_themes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mtsm-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "FancyTheme"));
        File.WriteAllText(Path.Combine(dir, "mine.json"), """{ "name": "mine", "widgets": [] }""");
        File.WriteAllText(Path.Combine(dir, "FancyTheme", "theme.yaml"), "STATS: {}\n");

        try
        {
            var entries = ThemeCatalog.Discover(dir);
            Assert.Contains(entries, e => e.Name == "mine" && e.Kind == ThemeKind.Json);
            Assert.Contains(entries, e => e.Name == "FancyTheme" && e.Kind == ThemeKind.Yaml);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DiskIoMeter_keeps_drive_letters_and_orders_them()
    {
        Assert.True(DiskIoMeter.IsDrive("C:"));
        Assert.True(DiskIoMeter.IsDrive("e:"));
        Assert.False(DiskIoMeter.IsDrive("_Total"));
        Assert.False(DiskIoMeter.IsDrive("HarddiskVolume3"));

        var ordered = DiskIoMeter.InLetterOrder(
        [
            new DriveThroughput("E:", 1, 2),
            new DriveThroughput("C:", 3, 4),
            new DriveThroughput("D:", 5, 6),
        ]);

        Assert.Equal(["C:", "D:", "E:"], ordered.Select(d => d.Name));
    }

    [Fact]
    public void NetworkMeter_skips_loopback_and_puts_the_busiest_adapter_first()
    {
        Assert.True(NetworkMeter.IsUsable(OperationalStatus.Up, NetworkInterfaceType.Wireless80211));
        Assert.False(NetworkMeter.IsUsable(OperationalStatus.Down, NetworkInterfaceType.Ethernet));
        Assert.False(NetworkMeter.IsUsable(OperationalStatus.Up, NetworkInterfaceType.Loopback));
        Assert.False(NetworkMeter.IsUsable(OperationalStatus.Up, NetworkInterfaceType.Tunnel));

        // A virtual switch is "Up" but barely used: the real adapter must still be net.1.
        var ordered = NetworkMeter.ByTraffic(
        [
            new InterfaceThroughput("vEthernet (WSL)", 0, 0, 4_000),
            new InterfaceThroughput("Wi-Fi", 1_000, 200, 9_000_000),
            new InterfaceThroughput("vEthernet (Default Switch)", 0, 0, 12_000),
        ]);

        Assert.Equal(["Wi-Fi", "vEthernet (Default Switch)", "vEthernet (WSL)"], ordered.Select(a => a.Name));
    }

    [Fact]
    public void PeakTracker_publishes_the_running_maximum_per_device()
    {
        var peaks = new SystemDynamics.PeakTracker();

        var first = new SensorSnapshot.Builder();
        peaks.Publish(first, SensorKeys.DiskRead(1), "C::read", 120_000);
        Assert.Equal(120_000, first.Build().Get(SensorKeys.Peak(SensorKeys.DiskRead(1))));

        var second = new SensorSnapshot.Builder();
        peaks.Publish(second, SensorKeys.DiskRead(1), "C::read", 900_000);
        var third = new SensorSnapshot.Builder();
        peaks.Publish(third, SensorKeys.DiskRead(1), "C::read", 4_000);
        var snapshot = third.Build();

        // The peak holds once reached, while the live value falls back to what is happening now.
        Assert.Equal(900_000, snapshot.Get(SensorKeys.Peak(SensorKeys.DiskRead(1))));
        Assert.Equal(4_000, snapshot.Get(SensorKeys.DiskRead(1)));

        // A different device in the same slot (adapter unplugged, another one takes net.1)
        // must not inherit the previous device's peak.
        var other = new SensorSnapshot.Builder();
        peaks.Publish(other, SensorKeys.DiskRead(1), "D::read", 7_000);
        Assert.Equal(7_000, other.Build().Get(SensorKeys.Peak(SensorKeys.DiskRead(1))));
    }

    [Fact]
    public void TextWidget_with_an_empty_fallback_draws_nothing_at_all()
    {
        // "12M / 4M" for a drive that exists; a bare "/" would be left behind for one that does not.
        var peak = new TextWidget { Label = "/ ", Sensor = SensorKeys.DiskRead(3), Humanize = "bytes", Fallback = "" };
        var named = new TextWidget { TextSensor = SensorKeys.DiskName(3), Fallback = "" };

        var present = new SensorSnapshot.Builder()
            .Set(SensorKeys.DiskRead(3), 12_000_000)
            .SetText(SensorKeys.DiskName(3), "E:")
            .Build();
        Assert.Equal("/ 12.0M", peak.ResolveText(present));
        Assert.Equal("E:", named.ResolveText(present));

        var absent = new SensorSnapshot.Builder().Build();
        Assert.Equal("", peak.ResolveText(absent));
        Assert.Equal("", named.ResolveText(absent));
    }

    [Fact]
    public void BestFan_prefers_the_named_fan_and_ignores_empty_headers()
    {
        (string, double)[] board =
        [
            ("Fan #1", 0),          // an empty header, not a stopped fan
            ("Chassis Fan", 900),
            ("CPU Fan", 2400),
        ];
        Assert.Equal(2400, LibreHardwareSensorProvider.BestFan(board, "cpu"));

        // Nothing named "CPU": report the fastest fan that is actually spinning.
        Assert.Equal(900, LibreHardwareSensorProvider.BestFan(board.Where(f => f.Item1 != "CPU Fan"), "cpu"));

        // A laptop whose embedded controller exposes no usable fan reading at all.
        Assert.Null(LibreHardwareSensorProvider.BestFan([("Fan #1", 0)], "cpu"));
        Assert.Null(LibreHardwareSensorProvider.BestFan([], "gpu"));
    }
}
