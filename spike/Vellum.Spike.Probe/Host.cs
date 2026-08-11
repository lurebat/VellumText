using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Vellum.Spike.Probe;

public static class Host
{
    private static bool _initialized;

    /// <summary>
    /// Headless Avalonia but with the REAL Skia render/text stack, so shaping, font fallback and
    /// bidi are genuine rather than the headless stub.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (_initialized) return;

        AppBuilder.Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        _initialized = true;
    }

    public static string ResultsDir
    {
        get
        {
            // bin/Debug/net8.0 -> project -> spike
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 6 && dir is not null; i++)
            {
                var candidate = Path.Combine(dir, "results");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }

            var fallback = Path.Combine(AppContext.BaseDirectory, "results");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    /// <summary>Renders a drawing callback to a PNG so visual claims have evidence.</summary>
    public static void RenderToPng(string fileName, int width, int height, Action<DrawingContext> draw)
    {
        var rtb = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            draw(ctx);
        }

        var path = Path.Combine(ResultsDir, fileName);
        rtb.Save(path);
        Console.WriteLine($"[png] {path}");
    }
}
