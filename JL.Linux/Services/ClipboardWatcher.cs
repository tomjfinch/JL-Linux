using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using JL.Core.Utilities;

namespace JL.Linux.Services;

/// <summary>
/// Watches the system clipboard for new text.
///
/// Windows JL gets a clipboard-changed message from the OS. Linux has no single
/// equivalent, so this uses the best available option:
///  1. Wayland with wl-clipboard installed: "wl-paste --watch" pushes every change
///     (works on KDE, Sway, Hyprland and other compositors with the data-control protocol).
///  2. Otherwise (X11, or GNOME on Wayland): poll the clipboard a few times per second.
/// </summary>
internal sealed class ClipboardWatcher : IDisposable
{
    private readonly TopLevel _topLevel;
    private readonly Action<string> _onText;
    private DispatcherTimer? _pollTimer;
    private Process? _wlPasteProcess;
    private string? _lastText;
    private bool _polling;
    private bool _disposed;

    public ClipboardWatcher(TopLevel topLevel, Action<string> onText)
    {
        _topLevel = topLevel;
        _onText = onText;
    }

    public bool IsRunning => _pollTimer is not null || _wlPasteProcess is not null;

    public string Mode { get; private set; } = "off";

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _disposed = false;
        if (Shell.IsWayland && Shell.CommandExists("wl-paste") && TryStartWlPaste())
        {
            return;
        }

        StartPolling();
    }

    public void Stop()
    {
        _pollTimer?.Stop();
        _pollTimer = null;

        Process? process = _wlPasteProcess;
        _wlPasteProcess = null;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
            }

            process.Dispose();
        }

        Mode = "off";
    }

    private bool TryStartWlPaste()
    {
        try
        {
            // Each clipboard change runs "base64 -w0; echo", so every change arrives as one
            // line of base64 - safe even when the copied text itself contains newlines.
            ProcessStartInfo psi = Shell.CreateStartInfo("wl-paste",
                ["--no-newline", "--type", "text", "--watch", "sh", "-c", "base64 -w0; echo"], true);

            Process? process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data))
                {
                    return;
                }

                string text;
                try
                {
                    text = Encoding.UTF8.GetString(Convert.FromBase64String(e.Data.Trim()));
                }
                catch (FormatException)
                {
                    return;
                }

                Dispatcher.UIThread.Post(() => Deliver(text));
            };
            process.ErrorDataReceived += static (_, _) => { };
            process.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                // wl-paste exits at once if the compositor lacks the data-control protocol
                // (e.g. GNOME). Fall back to polling so capture keeps working.
                if (!_disposed && ReferenceEquals(_wlPasteProcess, process))
                {
                    LoggerManager.Logger.Information("wl-paste --watch exited; falling back to clipboard polling");
                    _wlPasteProcess = null;
                    process.Dispose();
                    StartPolling();
                }
            });

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _wlPasteProcess = process;
            Mode = "wl-paste";
            return true;
        }
        catch (Exception ex)
        {
            LoggerManager.Logger.Warning(ex, "Couldn't start wl-paste");
            return false;
        }
    }

    private void StartPolling()
    {
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _pollTimer.Tick += async (_, _) =>
        {
            if (_polling)
            {
                return;
            }

            _polling = true;
            try
            {
                IClipboard? clipboard = _topLevel.Clipboard;
                if (clipboard is null)
                {
                    return;
                }

                string? text = await clipboard.GetTextAsync().ConfigureAwait(true);
                if (text is not null)
                {
                    Deliver(text);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.Logger.Debug(ex, "Clipboard poll failed");
            }
            finally
            {
                _polling = false;
            }
        };
        _pollTimer.Start();
        Mode = "polling";
    }

    private void Deliver(string text)
    {
        if (_disposed || text == _lastText)
        {
            return;
        }

        _lastText = text;
        _onText(text);
    }

    /// <summary>Lets the main window tell the watcher about text it set itself, so it isn't re-captured.</summary>
    public void IgnoreCurrent(string text) => _lastText = text;

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
