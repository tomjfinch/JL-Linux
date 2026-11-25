namespace JL.Linux.Services;

/// <summary>
/// Monitor screenshots and clipboard images for Anki mining, via the usual Linux tools.
/// </summary>
internal static class ScreenCapture
{
    public static byte[]? GetScreenshotPng()
    {
        if (Shell.IsWayland)
        {
            // wlroots compositors (Sway, Hyprland...) and KDE 6 with grim support
            if (Shell.CommandExists("grim"))
            {
                byte[]? png = Shell.RunForBytes("grim", ["-t", "png", "-"]);
                if (png is not null)
                {
                    return png;
                }
            }

            byte[]? viaFile = CaptureToTempFile("spectacle", file => ["-b", "-n", "-f", "-o", file])
                ?? CaptureToTempFile("gnome-screenshot", file => ["-f", file]);

            if (viaFile is not null)
            {
                return viaFile;
            }
        }

        if (Shell.CommandExists("maim"))
        {
            byte[]? png = Shell.RunForBytes("maim", ["-f", "png"]);
            if (png is not null)
            {
                return png;
            }
        }

        if (Shell.CommandExists("import"))
        {
            byte[]? png = Shell.RunForBytes("import", ["-silent", "-window", "root", "png:-"]);
            if (png is not null)
            {
                return png;
            }
        }

        return CaptureToTempFile("scrot", file => ["-o", file])
            ?? CaptureToTempFile("spectacle", file => ["-b", "-n", "-f", "-o", file])
            ?? CaptureToTempFile("gnome-screenshot", file => ["-f", file]);
    }

    public static byte[]? GetClipboardImagePng()
    {
        if (Shell.IsWayland && Shell.CommandExists("wl-paste"))
        {
            byte[]? png = Shell.RunForBytes("wl-paste", ["--no-newline", "--type", "image/png"]);
            if (png is not null)
            {
                return png;
            }
        }

        if (Shell.CommandExists("xclip"))
        {
            return Shell.RunForBytes("xclip", ["-selection", "clipboard", "-t", "image/png", "-o"]);
        }

        return null;
    }

    private static byte[]? CaptureToTempFile(string command, Func<string, string[]> args)
    {
        if (!Shell.CommandExists(command))
        {
            return null;
        }

        string file = Path.Join(Path.GetTempPath(), $"jl-shot-{Environment.ProcessId}.png");
        try
        {
            if (Shell.Run(command, args(file), 10000) && File.Exists(file))
            {
                return File.ReadAllBytes(file);
            }

            return null;
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }
}
