using System.Reflection;
using System.Text.Json;
using Avalonia.Threading;
using JL.Core.Dicts;
using JL.Core.Frontend;
using JL.Core.Utilities;
using JL.Linux.GUI;
using JL.Linux.Images;
using JL.Linux.Services;
using Microsoft.Data.Sqlite;

namespace JL.Linux;

/// <summary>
/// Linux implementation of JL.Core's IFrontend - the only contract between the
/// platform-neutral core and the UI. Mirrors JL.Windows/Frontend/WindowsFrontend.cs.
/// </summary>
internal sealed class LinuxFrontend : IFrontend
{
    public static readonly Version Version = Version.Parse(
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('-', '+')[0]);

    private readonly MainWindow _mainWindow;
    private bool _updateNoticeShown;

    public LinuxFrontend(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public Version JLVersion => Version;

    public Task<bool> PlayAudio(byte[] audio, string audioFormat, long requestId) => AudioPlayer.PlayAudio(audio, audioFormat, requestId);

    public Task StopAudio(long requestId) => AudioPlayer.StopAudio(requestId);

    public void Notify(NotificationLevel notificationLevel, string message)
    {
        LoggerManager.Logger.Information("[{Level}] {Message}", notificationLevel, message);
        Dispatcher.UIThread.Post(() => _mainWindow.Notify(notificationLevel, message));
    }

    public Task<bool> ShowYesNoDialogAsync(string text, string caption)
    {
        return Dispatcher.UIThread.InvokeAsync<bool>(() => MessageDialog.ShowYesNo(_mainWindow, caption, text));
    }

    public Task UpdateJL(Uri downloadUrlOfLatestJLRelease)
    {
        // Upstream's updater swaps in a Windows build. On Linux, rebuild from source instead.
        if (!_updateNoticeShown)
        {
            _updateNoticeShown = true;
            Notify(NotificationLevel.Information, "A new JL release is out. Update with: git pull && ./JL.Linux/build.sh");
        }

        return Task.CompletedTask;
    }

    public Task ApplyDictOptions() => Task.CompletedTask;

    public Task CopyFromWebSocket(string text, bool tsukikage)
    {
        if (tsukikage && text.StartsWith('{'))
        {
            // Tsukikage sends {"text": ..., ...}; only the text is used here.
            try
            {
                using JsonDocument doc = JsonDocument.Parse(text);
                foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                {
                    if (property.Name.Equals("text", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind is JsonValueKind.String)
                    {
                        text = property.Value.GetString() ?? "";
                        break;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return Dispatcher.UIThread.InvokeAsync(() => _mainWindow.ReceiveText(text, true)).GetTask();
    }

    public Task<byte[]?> GetImageFromClipboardAsByteArray() => Task.Run(ScreenCapture.GetClipboardImagePng);

    public byte[]? GetMonitorScreenshotAsByteArray() => ScreenCapture.GetScreenshotPng();

    // Windows JL uses SAPI voices. There is no comparable Japanese TTS shipped with Linux
    // desktops, so the TTS audio source simply yields nothing and the next source is tried.
    public Task TextToSpeech(string voiceName, string text, long requestId) => Task.CompletedTask;

    public Task<byte[]?> GetAudioResponseFromTextToSpeech(string text) => Task.FromResult<byte[]?>(null);

    public void SetInstalledVoiceWithHighestPriority()
    {
    }

    // These seed WPF-specific layout settings for the mpv/Tsukikage profiles; nothing to seed here.
    public void InsertSettingsForMpvProfile(SqliteConnection connection, int mpvProfileId)
    {
    }

    public void InsertSettingsForTsukikageProfile(SqliteConnection connection, int tsukikageProfileId)
    {
    }

    public void PopupDictTypeButtonsNeedUpdating()
    {
    }

    public ImageInfo? GetImageInfo(string imagePath) => ImageUtils.GetImageInfo(imagePath);
}
