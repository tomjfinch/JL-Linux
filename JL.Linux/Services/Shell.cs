using System.Diagnostics;
using JL.Core.Utilities;

namespace JL.Linux.Services;

/// <summary>
/// Small helpers for calling the standard Linux command-line tools JL leans on
/// (wl-paste, xclip, mpv, grim, maim ...).
/// </summary>
internal static class Shell
{
    public static bool IsWayland => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    public static bool CommandExists(string command)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (string dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Join(dir, command)))
            {
                return true;
            }
        }

        return false;
    }

    public static string? FirstAvailable(params string[] commands)
    {
        foreach (string command in commands)
        {
            if (CommandExists(command))
            {
                return command;
            }
        }

        return null;
    }

    public static ProcessStartInfo CreateStartInfo(string fileName, IEnumerable<string> args, bool redirectOutput)
    {
        ProcessStartInfo psi = new(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = true,
            RedirectStandardInput = false
        };

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }

    /// <summary>Runs a command and returns its raw stdout, or null on failure/timeout.</summary>
    public static byte[]? RunForBytes(string fileName, IEnumerable<string> args, int timeoutMs = 5000)
    {
        try
        {
            using Process? process = Process.Start(CreateStartInfo(fileName, args, true));
            if (process is null)
            {
                return null;
            }

            process.ErrorDataReceived += static (_, _) => { };
            process.BeginErrorReadLine();

            using MemoryStream ms = new();
            Task copyTask = process.StandardOutput.BaseStream.CopyToAsync(ms);
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(true);
                }
                catch (InvalidOperationException)
                {
                }

                return null;
            }

            copyTask.Wait(timeoutMs);
            return process.ExitCode is 0 && ms.Length > 0 ? ms.ToArray() : null;
        }
        catch (Exception ex)
        {
            LoggerManager.Logger.Debug(ex, "Running {Command} failed", fileName);
            return null;
        }
    }

    /// <summary>Runs a command and reports whether it exited with code 0.</summary>
    public static bool Run(string fileName, IEnumerable<string> args, int timeoutMs = 5000)
    {
        try
        {
            using Process? process = Process.Start(CreateStartInfo(fileName, args, false));
            if (process is null)
            {
                return false;
            }

            process.ErrorDataReceived += static (_, _) => { };
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(true);
                }
                catch (InvalidOperationException)
                {
                }

                return false;
            }

            return process.ExitCode is 0;
        }
        catch (Exception ex)
        {
            LoggerManager.Logger.Debug(ex, "Running {Command} failed", fileName);
            return false;
        }
    }
}
