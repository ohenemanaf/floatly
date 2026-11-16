using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Floatly.Services;

public static class ScreenshotService
{
    public static Rectangle VirtualBounds => SystemInformation.VirtualScreen;

    public static Bitmap CaptureAllDisplays()
    {
        var bounds = VirtualBounds;
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    public static Bitmap CapturePrimary()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("No primary display found.");
        var bitmap = new Bitmap(bounds.Width, bounds.Height);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    public static string Save(Bitmap bitmap)
    {
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures)) pictures = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = Path.Combine(pictures, "Floatly Captures");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"Floatly-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }
}
