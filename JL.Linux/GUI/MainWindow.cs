using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using JL.Core;
using JL.Core.Config;
using JL.Core.Frontend;
using JL.Core.Japanese;
using JL.Core.Lookup;
using JL.Core.Network;
using JL.Core.Network.WebSocket;
using JL.Core.Statistics;
using JL.Core.Utilities;
using JL.Core.Utilities.Database;
using JL.Linux.Config;
using JL.Linux.Services;
using Microsoft.Data.Sqlite;

namespace JL.Linux.GUI;

/// <summary>
/// The text window: shows captured text; hovering a word opens the dictionary popup.
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly Border _background;
    private readonly ScrollViewer _scrollViewer;
    private readonly SelectableTextBlock _text;
    private readonly TextBlock _status;
    private readonly PopupWindow _rootPopup = new(0);
    private readonly DispatcherTimer _exitTimer;
    private readonly DispatcherTimer _statusTimer;
    private readonly WindowNotificationManager _notifications;
    private readonly List<string> _backlog = [];
    private readonly MenuItem _alwaysOnTopMenuItem;
    private readonly ClipboardWatcher _clipboardWatcher;

    private int _backlogIndex = -1;
    private int _lastCharIndex = -1;
    private bool _coreReady;
    private bool _closingStarted;
    private bool _cleanedUp;

    public MainWindow()
    {
        Title = "JL";
        MinWidth = 200;
        MinHeight = 60;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;

        string iconPath = Path.Join(AppContext.BaseDirectory, "jl.png");
        if (File.Exists(iconPath))
        {
            Icon = new WindowIcon(iconPath);
        }

        _text = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(10, 6, 10, 6),
            SelectionBrush = new SolidColorBrush(Color.FromArgb(0x90, 0x33, 0x99, 0xFF)),
            ContextFlyout = null
        };

        _scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _text
        };

        _status = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xC0, 0xC0, 0xC0)),
            Margin = new Thickness(10, 0, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsHitTestVisible = false
        };

        DockPanel dock = new();
        DockPanel.SetDock(_status, Dock.Bottom);
        dock.Children.Add(_status);
        dock.Children.Add(_scrollViewer);

        _alwaysOnTopMenuItem = new MenuItem();
        _background = new Border { Child = dock, ContextMenu = BuildContextMenu() };
        Content = _background;

        _exitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _exitTimer.Tick += (_, _) =>
        {
            _exitTimer.Stop();
            if (!_rootPopup.IsLocked && !_rootPopup.IsPointerOverChain() && !_text.IsPointerOver)
            {
                HidePopup();
            }
        };

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            SetIdleStatus();
        };

        _notifications = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.BottomRight,
            MaxItems = 3
        };

        _text.AddHandler(PointerMovedEvent, OnTextPointerMoved, RoutingStrategies.Tunnel);
        _text.AddHandler(PointerPressedEvent, OnTextPointerPressed, RoutingStrategies.Tunnel);
        _text.PointerExited += (_, _) =>
        {
            if (!_rootPopup.IsLocked)
            {
                _exitTimer.Start();
            }
        };

        _clipboardWatcher = new ClipboardWatcher(this, text => ReceiveText(text, false));

        FrontendManager.Frontend = new LinuxFrontend(this);
        InitializeConfig();

        Opened += OnOpened;
    }

    // ---------------------------------------------------------------- startup

    private void InitializeConfig()
    {
        ConfigDBManager.CreateDB();

        SqliteConnection? readOnlyConnection = ConfigDBManager.CreateReadOnlyDBConnection();
        if (readOnlyConnection is not null)
        {
            using (readOnlyConnection)
            {
                ProfileDBUtils.SetCurrentProfileFromDB(readOnlyConnection);
                StatsDBUtils.SetStatsFromDB(readOnlyConnection);
            }
        }

        using (SqliteConnection connection = ConfigDBManager.CreateReadWriteDBConnection())
        {
            CoreConfigManager.Instance.ApplyPreferences(connection, ConfigDBManager.GetAllConfigs(connection));
            UiConfig.Load(connection, ConfigDBManager.GetAllConfigs(connection));
        }

        RegexReplacerUtils.PopulateRegexReplacements();

        UiConfig config = UiConfig.Instance;
        Width = Math.Max(MinWidth, config.MainWindowWidth);
        Height = Math.Max(MinHeight, config.MainWindowHeight);
        if (config.MainWindowLeft != int.MinValue && config.MainWindowTop != int.MinValue)
        {
            Position = new PixelPoint(config.MainWindowLeft, config.MainWindowTop);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        ApplyUiConfig();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        EnsureOnScreen();
        SetStatus("Loading dictionaries…", sticky: true);

        if (CoreConfigManager.Instance.CaptureTextFromClipboard)
        {
            _clipboardWatcher.Start();
        }

        try
        {
            await Task.Run(CoreInitializer.CoreInitialize).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LoggerManager.Logger.Error(ex, "Core initialization failed");
            Notify(NotificationLevel.Error, $"Startup failed: {ex.Message}");
        }

        _coreReady = true;
        SetIdleStatus();

        if (CoreConfigManager.Instance.CheckForJLUpdatesOnStartUp)
        {
            Task.Run(static () => NetworkUtils.CheckForJLUpdates(true)).SafeFireAndForget("Update check failed");
        }
    }

    private void EnsureOnScreen()
    {
        try
        {
            PixelPoint position = Position;
            bool visible = Screens.All.Any(s => s.WorkingArea.Contains(new PixelPoint(position.X + 40, position.Y + 20)));
            if (!visible && Screens.Primary is { } primary)
            {
                Position = new PixelPoint(primary.WorkingArea.X + 100, primary.WorkingArea.Y + 100);
            }
        }
        catch (Exception ex)
        {
            LoggerManager.Logger.Debug(ex, "Couldn't validate window position");
        }
    }

    public void ApplyUiConfig()
    {
        UiConfig config = UiConfig.Instance;
        Topmost = config.AlwaysOnTop;
        _alwaysOnTopMenuItem.Header = (config.AlwaysOnTop ? "✓ " : "    ") + "Always on top";
        _background.Background = Palette.Brush(config.MainWindowBackgroundColor, config.MainWindowOpacity);
        _text.FontFamily = new FontFamily(config.MainWindowFont);
        _text.FontSize = config.MainWindowFontSize;
        _text.Foreground = Palette.Brush(config.MainWindowTextColor);
        AudioPlayer.CustomPlayerCommand = config.AudioPlayerCommand;
        _rootPopup.ApplyAppearance();
    }

    /// <summary>Called after the preferences window saved new settings.</summary>
    public void OnPreferencesChanged()
    {
        ApplyUiConfig();
        if (CoreConfigManager.Instance.CaptureTextFromClipboard)
        {
            _clipboardWatcher.Start();
        }
        else
        {
            _clipboardWatcher.Stop();
        }

        SetIdleStatus();
    }

    // ---------------------------------------------------------------- text capture

    public void ReceiveText(string text, bool fromWebSocket)
    {
        if (_closingStarted || text.Length is 0)
        {
            return;
        }

        UiConfig config = UiConfig.Instance;
        if (config.OnlyCaptureTextWithJapaneseChars && !JapaneseUtils.ContainsJapaneseCharacters(text))
        {
            return;
        }

        string sanitized = TextUtils.SanitizeText(text, false);
        if (sanitized.Length is 0)
        {
            return;
        }

        if (config.MaxTextLengthToCapture > 0 && sanitized.Length > config.MaxTextLengthToCapture)
        {
            return;
        }

        if (config.DiscardIdenticalText && sanitized == _text.Text)
        {
            return;
        }

        AddToBacklog(sanitized);
        ShowText(sanitized);

        int characterCount = CountCharacters(sanitized);
        StatsUtils.IncrementStat(StatType.Characters, characterCount);
        StatsUtils.IncrementStat(StatType.Lines);
        StatsUtils.SetIdleTimeTimerInterval(characterCount);
        StatsUtils.StartTimeStatStopWatch(true);
    }

    private void ShowText(string text)
    {
        if (!_rootPopup.IsLocked)
        {
            HidePopup();
        }

        _text.Text = text;
        _text.SelectionStart = 0;
        _text.SelectionEnd = 0;
        _scrollViewer.Offset = default;
        _lastCharIndex = -1;

        if (!_statusTimer.IsEnabled)
        {
            SetIdleStatus();
        }
    }

    private static int CountCharacters(string text)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c) && !char.IsLowSurrogate(c))
            {
                ++count;
            }
        }

        return count;
    }

    private void AddToBacklog(string text)
    {
        int capacity = UiConfig.Instance.MaxBacklogCapacity;
        if (capacity is 0)
        {
            return;
        }

        _backlog.Add(text);
        if (capacity > 0 && _backlog.Count > capacity)
        {
            _backlog.RemoveRange(0, _backlog.Count - capacity);
        }

        _backlogIndex = _backlog.Count - 1;
    }

    private void MoveInBacklog(int delta)
    {
        if (_backlog.Count is 0)
        {
            return;
        }

        int newIndex = Math.Clamp(_backlogIndex + delta, 0, _backlog.Count - 1);
        if (newIndex == _backlogIndex)
        {
            return;
        }

        _backlogIndex = newIndex;
        ShowText(_backlog[newIndex]);
        SetStatus($"Backlog {newIndex + 1}/{_backlog.Count}");
    }

    private async Task PasteFromClipboard()
    {
        if (Clipboard is null)
        {
            return;
        }

        string? text = await Clipboard.GetTextAsync().ConfigureAwait(true);
        if (!string.IsNullOrEmpty(text))
        {
            _clipboardWatcher.IgnoreCurrent(text);
            ReceiveText(text, false);
        }
    }

    // ---------------------------------------------------------------- lookups

    private void OnTextPointerMoved(object? sender, PointerEventArgs e)
    {
        _exitTimer.Stop();
        if (_rootPopup.IsLocked || !LookupEngine.TriggerSatisfied(e))
        {
            return;
        }

        LookUpAtPointer(e);
    }

    private void OnTextPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPointProperties properties = e.GetCurrentPoint(_text).Properties;
        if (properties.IsLeftButtonPressed)
        {
            if (UiConfig.Instance.LookupTrigger is LookupTrigger.LeftClick)
            {
                HidePopup();
                LookUpAtPointer(e);
                _rootPopup.Lock();
                return;
            }

            if (_rootPopup.IsLocked)
            {
                // Leave mining mode and go back to hover lookups.
                HidePopup();
                LookUpAtPointer(e);
                return;
            }

            if (_rootPopup.IsVisible)
            {
                _rootPopup.Lock();
            }
        }
        else if (properties.IsMiddleButtonPressed && _rootPopup.IsVisible)
        {
            _rootPopup.Lock();
            e.Handled = true;
        }
    }

    private void LookUpAtPointer(PointerEventArgs e)
    {
        if (!_coreReady)
        {
            return;
        }

        string? text = _text.Text;
        int charIndex = text is null ? -1 : LookupEngine.GetCharIndexAtPointer(_text, e);
        if (text is null || charIndex < 0)
        {
            HidePopup();
            return;
        }

        if (charIndex == _lastCharIndex && _rootPopup.IsVisible)
        {
            return;
        }

        _lastCharIndex = charIndex;

        LookupResult[]? results = LookupEngine.Lookup(text, charIndex, out string? lookedUpText);
        if (results is null)
        {
            HidePopup();
            return;
        }

        if (UiConfig.Instance.HighlightLongestMatch)
        {
            _text.SelectionStart = charIndex;
            _text.SelectionEnd = Math.Min(text.Length, charIndex + results[0].MatchedText.Length);
        }

        if (_rootPopup.IsVisible && lookedUpText == _rootPopup.LastLookedUpText)
        {
            return;
        }

        PixelPoint anchor = _text.PointToScreen(e.GetPosition(_text));
        _rootPopup.ShowResults(results, text, charIndex, anchor, lookedUpText);
    }

    private void HidePopup()
    {
        _rootPopup.HideAll();
        _lastCharIndex = -1;
        if (UiConfig.Instance.HighlightLongestMatch && _text.SelectionStart != _text.SelectionEnd)
        {
            _text.SelectionStart = 0;
            _text.SelectionEnd = 0;
        }
    }

    // ---------------------------------------------------------------- keyboard & menu

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        PopupWindow? popup = _rootPopup.DeepestVisible();
        e.Handled = true;

        switch (e.Key)
        {
            case Key.Escape:
                HidePopup();
                break;

            case Key.PageUp or Key.Up:
                MoveInBacklog(-1);
                break;

            case Key.PageDown or Key.Down:
                MoveInBacklog(1);
                break;

            case Key.V when ctrl:
                await PasteFromClipboard().ConfigureAwait(true);
                break;

            case Key.P when ctrl:
                OpenPreferences();
                break;

            case Key.D when ctrl:
                OpenDictionaries();
                break;

            case Key.T when ctrl:
                ToggleAlwaysOnTop();
                break;

            case Key.M when popup is not null:
                popup.MineResult(0);
                break;

            case Key.A when popup is not null:
                popup.PlayAudio(0);
                break;

            case >= Key.D1 and <= Key.D9 when popup is not null:
                popup.MineResult(e.Key - Key.D1);
                break;

            default:
                e.Handled = false;
                break;
        }
    }

    private ContextMenu BuildContextMenu()
    {
        MenuItem preferences = new() { Header = "Preferences…", InputGesture = new KeyGesture(Key.P, KeyModifiers.Control) };
        preferences.Click += (_, _) => OpenPreferences();

        MenuItem dictionaries = new() { Header = "Dictionaries…", InputGesture = new KeyGesture(Key.D, KeyModifiers.Control) };
        dictionaries.Click += (_, _) => OpenDictionaries();

        MenuItem anki = new() { Header = "Anki setup…" };
        anki.Click += (_, _) => new AnkiSetupWindow().Show(this);

        MenuItem paste = new() { Header = "Paste text", InputGesture = new KeyGesture(Key.V, KeyModifiers.Control) };
        paste.Click += async (_, _) => await PasteFromClipboard().ConfigureAwait(true);

        MenuItem clear = new() { Header = "Clear text" };
        clear.Click += (_, _) => ShowText("");

        _alwaysOnTopMenuItem.InputGesture = new KeyGesture(Key.T, KeyModifiers.Control);
        _alwaysOnTopMenuItem.Click += (_, _) => ToggleAlwaysOnTop();

        MenuItem stats = new() { Header = "Statistics…" };
        stats.Click += (_, _) => new StatsWindow().Show(this);

        MenuItem openFolder = new() { Header = "Open JL folder" };
        openFolder.Click += (_, _) => OpenInFileManager(AppInfo.ApplicationPath);

        MenuItem quit = new() { Header = "Quit" };
        quit.Click += (_, _) => Close();

        ContextMenu menu = new();
        menu.Items.Add(preferences);
        menu.Items.Add(dictionaries);
        menu.Items.Add(anki);
        menu.Items.Add(new Separator());
        menu.Items.Add(paste);
        menu.Items.Add(clear);
        menu.Items.Add(_alwaysOnTopMenuItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(stats);
        menu.Items.Add(openFolder);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        return menu;
    }

    private void ToggleAlwaysOnTop()
    {
        UiConfig.Instance.AlwaysOnTop = !UiConfig.Instance.AlwaysOnTop;
        ApplyUiConfig();
    }

    private void OpenPreferences()
    {
        HidePopup();
        new PreferencesWindow(this).Show(this);
    }

    private void OpenDictionaries()
    {
        HidePopup();
        new DictionariesWindow().Show(this);
    }

    private static void OpenInFileManager(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex)
        {
            LoggerManager.Logger.Warning(ex, "xdg-open failed");
        }
    }

    // ---------------------------------------------------------------- status & notifications

    private void SetIdleStatus()
    {
        if (!_coreReady)
        {
            return;
        }

        string source = CoreConfigManager.Instance.CaptureTextFromClipboard
            ? $"clipboard ({_clipboardWatcher.Mode})"
            : "";

        if (CoreConfigManager.Instance.CaptureTextFromWebSocket)
        {
            source = source.Length > 0 ? source + " + WebSocket" : "WebSocket";
        }

        _status.Text = string.IsNullOrEmpty(_text.Text)
            ? $"Capturing from {(source.Length > 0 ? source : "nothing — enable a source in Preferences")} · right-click for menu"
            : "";
    }

    private void SetStatus(string message, bool sticky = false)
    {
        _status.Text = message;
        _statusTimer.Stop();
        if (!sticky)
        {
            _statusTimer.Start();
        }
    }

    public void Notify(NotificationLevel level, string message)
    {
        NotificationType type = level switch
        {
            NotificationLevel.Error => NotificationType.Error,
            NotificationLevel.Warning => NotificationType.Warning,
            NotificationLevel.Success => NotificationType.Success,
            _ => NotificationType.Information
        };

        _notifications.Show(new Notification("JL", message, type, TimeSpan.FromSeconds(level is NotificationLevel.Error ? 8 : 4)));
    }

    // ---------------------------------------------------------------- shutdown

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_cleanedUp)
        {
            return;
        }

        e.Cancel = true;
        if (_closingStarted)
        {
            return;
        }

        _closingStarted = true;
        UiConfig config = UiConfig.Instance;
        config.MainWindowWidth = ClientSize.Width;
        config.MainWindowHeight = ClientSize.Height;
        config.MainWindowLeft = Position.X;
        config.MainWindowTop = Position.Y;

        _rootPopup.HideAll();
        Hide();
        _ = CleanUpAndClose();
    }

    private async Task CleanUpAndClose()
    {
        try
        {
            _clipboardWatcher.Dispose();
            AudioPlayer.StopCurrent();

            using (SqliteConnection connection = ConfigDBManager.CreateReadWriteDBConnection())
            {
                UiConfig.Instance.SaveWindowGeometry(connection);
                StatsUtils.UpdateTimeStats(restartStopWatch: false);
                StatsDBUtils.UpdateLifetimeStats(connection);
                StatsDBUtils.UpdateProfileLifetimeStats(connection);
                ConfigDBManager.AnalyzeAndVacuum(connection, LinuxFrontend.Version);
            }

            await WebSocketUtils.DisconnectFromAllWebSocketConnections().ConfigureAwait(true);
            await WebSocketUtils.DisconnectFromTsukikageWebSocketConnection().ConfigureAwait(true);

            DBUtils.SendOptimizePragmaToAllDBs();
            SqliteConnection.ClearAllPools();
        }
        catch (Exception ex)
        {
            LoggerManager.Logger.Error(ex, "Error while closing");
        }

        _cleanedUp = true;
        Close();
    }
}
