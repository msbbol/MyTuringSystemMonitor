using System.Drawing;
using MyTuringSystemMonitor.Sensors;

namespace MyTuringSystemMonitor.Rendering;

/// <summary>Turns a theme + sensor snapshot into a bitmap frame ready to push to the display.</summary>
public interface IThemeRenderer
{
    Bitmap Render(Theme theme, SensorSnapshot snapshot, int width, int height);
}
