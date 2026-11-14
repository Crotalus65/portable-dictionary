namespace PortableDictionary.Services;

public static class WindowPlacement
{
    public static Rectangle CreateDefault()
    {
        var work = Screen.PrimaryScreen?.WorkingArea ?? Screen.AllScreens.FirstOrDefault()?.WorkingArea;
        if (work is not { Width: > 0, Height: > 0 } area)
        {
            return new Rectangle(80, 80, 720, 580);
        }

        var width = Math.Min(720, area.Width);
        var height = Math.Min(580, area.Height);
        var x = area.Left + (area.Width - width) / 2;
        var y = area.Top + (area.Height - height) / 2;
        return new Rectangle(x, y, width, height);
    }
}
