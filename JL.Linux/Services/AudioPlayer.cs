using System.Diagnostics;
using JL.Core.Audio;
using JL.Core.Frontend;
using JL.Core.Utilities;

namespace JL.Linux.Services;

/// <summary>
/// Plays word audio by handing a temp file to a command-line player.
/// mpv and ffplay handle every format the audio sources return (mp3, ogg, opus, wav...).
/// </summary>
internal static class AudioPlayer
{
    private static readonly Lock s_lock = new();
    private static Process? s_current;
    private static long s_currentRequestId = -1;
    private static bool s_warnedNoPlayer;

    public static string? CustomPlayerCommand { get; set; }

    public static Task<bool> PlayAudio(byte[] audio, string audioFormat, long requestId)
    {
        return Task.Run(() =>
        {
            if (!AudioUtils.IsCurrentAudioRequest(requestId))
            {
                return false;
            }

            string extension = string.IsNullOrWhiteSpace(audioFormat) ? "mp3" : audioFormat.Trim('.');
            string tempFile = Path.Join(Path.GetTempPath(), $"jl-audio-{Environment.ProcessId}-{requestId}.{extension}");

            try
            {
                File.WriteAllBytes(tempFile, audio);
            }
            catch (IOException ex)
            {
                LoggerManager.Logger.Warning(ex, "Couldn't write temporary audio file");
                return false;
            }

            (string player, List<string> args)? command = GetPlayerCommand(tempFile);
            if (command is null)
            {
                TryDelete(tempFile);
                if (!s_warnedNoPlayer)
                {
                    s_warnedNoPlayer = true;
                    FrontendManager.Frontend.Notify(NotificationLevel.Warning, "No audio player found. Install mpv or ffmpeg (ffplay) to hear word audio.");
                }

                return false;
            }

            StopCurrent();

            try
            {
                Process? process = Process.Start(Shell.CreateStartInfo(command.Value.player, command.Value.args, false));
                if (process is null)
                {
                    TryDelete(tempFile);
                    return false;
                }

                process.EnableRaisingEvents = true;
                process.ErrorDataReceived += static (_, _) => { };
                process.BeginErrorReadLine();
                process.Exited += (_, _) =>
                {
                    TryDelete(tempFile);
                    lock (s_lock)
                    {
                        if (ReferenceEquals(s_current, process))
                        {
                            s_current = null;
                            s_currentRequestId = -1;
                        }
                    }

                    process.Dispose();
                };

                lock (s_lock)
                {
                    s_current = process;
                    s_currentRequestId = requestId;
                }

                return true;
            }
            catch (Exception ex)
            {
                LoggerManager.Logger.Warning(ex, "Couldn't start audio player {Player}", command.Value.player);
                TryDelete(tempFile);
                return false;
            }
        });
    }

    public static Task StopAudio(long requestId)
    {
        lock (s_lock)
        {
            if (s_currentRequestId != requestId && requestId >= 0)
            {
                return Task.CompletedTask;
            }
        }

        StopCurrent();
        return Task.CompletedTask;
    }

    public static void StopCurrent()
    {
        Process? process;
        lock (s_lock)
        {
            process = s_current;
            s_current = null;
            s_currentRequestId = -1;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static (string, List<string>)? GetPlayerCommand(string file)
    {
        string? custom = CustomPlayerCommand;
        if (!string.IsNullOrWhiteSpace(custom))
        {
            // e.g. "mpv --no-video --volume=80" - the file path is appended.
            string[] parts = custom.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            List<string> customArgs = [.. parts[1..], file];
            return (parts[0], customArgs);
        }

        if (Shell.CommandExists("mpv"))
        {
            return ("mpv", new List<string> { "--no-video", "--no-terminal", "--really-quiet", "--no-config", file });
        }

        if (Shell.CommandExists("ffplay"))
        {
            return ("ffplay", new List<string> { "-nodisp", "-autoexit", "-loglevel", "quiet", file });
        }

        if (file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            string? wavPlayer = Shell.FirstAvailable("pw-play", "paplay", "aplay");
            if (wavPlayer is not null)
            {
                return (wavPlayer, new List<string> { file });
            }
        }

        if (Shell.CommandExists("cvlc"))
        {
            return ("cvlc", new List<string> { "--play-and-exit", "--quiet", file });
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
