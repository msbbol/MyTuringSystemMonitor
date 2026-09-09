using System.IO.Ports;

namespace MyTuringSystemMonitor.Hardware;

/// <summary>
/// Resolves the COM port to use. An explicit port (e.g. "COM4") is returned as-is; "AUTO"
/// enumerates available ports and picks the only one, otherwise it asks the user to be explicit.
/// </summary>
public static class ComPortResolver
{
    public const string Auto = "AUTO";

    /// <summary>Lists serial ports currently present on the system.</summary>
    public static IReadOnlyList<string> AvailablePorts() =>
        SerialPort.GetPortNames().Distinct().OrderBy(p => p).ToArray();

    /// <summary>
    /// Returns the port to open. If <paramref name="configured"/> is a concrete port it is
    /// returned unchanged (we don't second-guess it). If it is "AUTO", we pick the single
    /// available port; with zero or several ports we throw with guidance rather than guess wrong.
    /// </summary>
    /// <remarks>
    /// TODO (nice-to-have): match by known VID/PID or USB serial string (USB35INCHIPS /
    /// USB35INCHIPSV2) via SetupAPI/WMI so AUTO works even with several serial devices present.
    /// </remarks>
    public static string Resolve(string configured)
    {
        if (!string.Equals(configured, Auto, StringComparison.OrdinalIgnoreCase))
            return configured;

        var ports = AvailablePorts();
        return ports.Count switch
        {
            1 => ports[0],
            0 => throw new InvalidOperationException(
                "comPort is AUTO but no serial ports were found. Plug in the display or set comPort explicitly in config.json."),
            _ => throw new InvalidOperationException(
                $"comPort is AUTO but several serial ports were found ({string.Join(", ", ports)}). " +
                "Set comPort explicitly in config.json."),
        };
    }
}
