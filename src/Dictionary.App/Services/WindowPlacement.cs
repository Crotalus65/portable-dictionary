using PortableDictionary.Core;

namespace PortableDictionary.Services;

public static class WindowPlacement
{
    public static Rectangle Restore(AppSettings settings)
    {
        var desired = new Rectangle(settings.WindowX, settings.WindowY, settings.WindowWidth, settings.WindowHeight);
        var screens = Screen.AllScreens.Select(screen => screen.WorkingArea).ToArray();
        if (screens.Length == 0)
        {
            return new Rectangle(80, 80, 720, 580);
        }

        var best = screens
            .Select(area => new { Area = area, Intersection = Rectangle.Intersect(area, desired) })
            .OrderByDescending(item => (long)item.Intersection.Width * item.Intersection.Height)
            .First();
        var visible = best.Intersection.Width >= Math.Min(120, desired.Width) &&
                      best.Intersection.Height >= Math.Min(80, desired.Height);
        var work = visible ? best.Area : Screen.PrimaryScreen?.WorkingArea ?? screens[0];

        var width = Math.Min(Math.Max(420, desired.Width), Math.Max(320, work.Width));
        var height = Math.Min(Math.Max(320, desired.Height), Math.Max(240, work.Height));
        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);
        var x = visible ? desired.X : work.Left + Math.Max(0, (work.Width - width) / 2);
        var y = visible ? desired.Y : work.Top + Math.Max(0, (work.Height - height) / 2);
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        return new Rectangle(x, y, width, height);
    }
}
