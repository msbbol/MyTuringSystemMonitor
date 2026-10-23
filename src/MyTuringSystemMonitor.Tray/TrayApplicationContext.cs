using System.Diagnostics;
using System.Drawing;
using System.ServiceProcess;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace MyTuringSystemMonitor.Tray;

/// <summary>
/// The notification-area icon: shows the monitor's state at a glance and offers quick actions.
/// Everything else (settings, themes, logs) lives in the web control panel it opens.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly MonitorClient _client = new();
    private readonly NotifyIcon _icon = new();
    private readonly Timer _timer = new() { Interval = 3000 };

    private readonly ToolStripMenuItem _statusItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _openItem = new("Open control panel");
    private readonly ToolStripMenuItem _pauseItem = new("Pause display");
    private readonly ToolStripMenuItem _restartItem = new("Restart monitor");
    private readonly ToolStripMenuItem _serviceMenu = new("Windows service");
    private readonly ToolStripMenuItem _serviceStart = new("Start service");
    private readonly ToolStripMenuItem _serviceStop = new("Stop service");
    private readonly ToolStripMenuItem _serviceRestart = new("Restart service");

    private string? _lastState;
    private bool _polling;
    private bool _paused;

    public TrayApplicationContext()
    {
        _openItem.Font = new Font(_openItem.Font, FontStyle.Bold);
        _openItem.Click += (_, _) => OpenPanel();
        _pauseItem.Click += async (_, _) => await ControlAsync(_paused ? "resume" : "pause");
        _restartItem.Click += async (_, _) => await ControlAsync("restart");

        _serviceStart.Click += (_, _) => { ServiceControl.Start(); _ = PollSoonAsync(); };
        _serviceStop.Click += (_, _) => { ServiceControl.Stop(); _ = PollSoonAsync(); };
        _serviceRestart.Click += (_, _) => { ServiceControl.Restart(); _ = PollSoonAsync(); };
        _serviceMenu.DropDownItems.AddRange([_serviceStart, _serviceStop, _serviceRestart]);

        var exitItem = new ToolStripMenuItem("Exit tray icon");
        exitItem.Click += (_, _) => ExitThread();

        var title = new ToolStripMenuItem("Turing System Monitor") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            title,
            _statusItem,
            new ToolStripSeparator(),
            _openItem,
            _pauseItem,
            _restartItem,
            new ToolStripSeparator(),
            _serviceMenu,
            new ToolStripSeparator(),
            exitItem,
        ]);
        menu.Opening += (_, _) => UpdateServiceMenu();

        _icon.ContextMenuStrip = menu;
        _icon.Icon = TrayIcons.For(TrayIcons.Idle);
        _icon.Text = "Turing System Monitor";
        _icon.DoubleClick += (_, _) => OpenPanel();
        _icon.Visible = true;

        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            var status = await _client.GetStatusAsync();
            if (status is null) ShowOffline();
            else ShowStatus(status.Monitor);
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task PollSoonAsync()
    {
        await Task.Delay(1500);
        await PollAsync();
    }

    private void ShowStatus(MonitorInfo m)
    {
        var state = m.State.ToLowerInvariant();
        _paused = state is "paused" or "stopped";

        var (label, color) = state switch
        {
            "running" => ("Running", TrayIcons.Running),
            "starting" => ("Starting", TrayIcons.Busy),
            "reconnecting" => ("Reconnecting to display", TrayIcons.Busy),
            "paused" => ("Paused", TrayIcons.Idle),
            "stopped" => ("Stopped", TrayIcons.Idle),
            "faulted" => ("Waiting for display", TrayIcons.Problem),
            _ => (m.State, TrayIcons.Idle),
        };

        var where = m.DisplayProvider.Equals("Null", StringComparison.OrdinalIgnoreCase) ? "dry run" : m.Port;
        var detail = string.Join(" · ", new[] { m.Theme, where }.Where(s => !string.IsNullOrWhiteSpace(s)));
        _statusItem.Text = string.IsNullOrEmpty(detail) ? label : $"{label} — {detail}";

        _icon.Icon = TrayIcons.For(color);
        SetTooltip($"Turing monitor: {label}" + (m.Theme is null ? "" : $" ({m.Theme})"));

        _pauseItem.Text = _paused ? "Resume display" : "Pause display";
        _pauseItem.Enabled = true;
        _restartItem.Enabled = true;
        _openItem.Enabled = true;

        NotifyTransition(state, m);
        _lastState = state;
    }

    private void ShowOffline()
    {
        var installed = ServiceControl.Query() is not null;
        _statusItem.Text = installed ? "Service not running" : "Service not installed";
        _icon.Icon = TrayIcons.For(TrayIcons.Problem);
        SetTooltip($"Turing monitor: {_statusItem.Text}");

        _pauseItem.Enabled = false;
        _restartItem.Enabled = false;
        _openItem.Enabled = false;

        if (_lastState is not null and not "offline")
            _icon.ShowBalloonTip(4000, "Turing System Monitor", "The monitor service stopped responding.", ToolTipIcon.Warning);
        _lastState = "offline";
    }

    /// <summary>Balloon only for changes worth interrupting for — not for routine sleep/wake reconnects.</summary>
    private void NotifyTransition(string state, MonitorInfo m)
    {
        if (_lastState is null || _lastState == state) return;

        if (state == "faulted")
            _icon.ShowBalloonTip(4000, "Display not found",
                $"{m.LastError} The monitor keeps retrying.", ToolTipIcon.Warning);
        else if (state == "running" && _lastState is "faulted" or "offline")
            _icon.ShowBalloonTip(3000, "Turing System Monitor", "Display connected.", ToolTipIcon.Info);
    }

    private void UpdateServiceMenu()
    {
        var status = ServiceControl.Query();
        _serviceMenu.Enabled = status is not null;
        _serviceMenu.Text = status is null ? "Windows service (not installed)" : $"Windows service ({Describe(status.Value)})";
        _serviceStart.Enabled = status == ServiceControllerStatus.Stopped;
        _serviceStop.Enabled = status == ServiceControllerStatus.Running;
        _serviceRestart.Enabled = status == ServiceControllerStatus.Running;
    }

    private static string Describe(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => "running",
        ServiceControllerStatus.Stopped => "stopped",
        ServiceControllerStatus.StartPending => "starting",
        ServiceControllerStatus.StopPending => "stopping",
        _ => status.ToString().ToLowerInvariant(),
    };

    private async Task ControlAsync(string action)
    {
        if (!await _client.ControlAsync(action))
            _icon.ShowBalloonTip(3000, "Turing System Monitor", "Couldn't reach the monitor service.", ToolTipIcon.Error);
        await PollAsync();
    }

    private void OpenPanel()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_client.PanelUri.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open {_client.PanelUri}\n\n{ex.Message}", "Turing System Monitor",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetTooltip(string text) =>
        _icon.Text = text.Length <= 127 ? text : text[..124] + "...";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _icon.Visible = false;
            _icon.Dispose();
            _client.Dispose();
        }
        base.Dispose(disposing);
    }
}
