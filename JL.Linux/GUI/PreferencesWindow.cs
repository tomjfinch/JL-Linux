using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using JL.Core.Config;
using JL.Core.Utilities;
using JL.Linux.Config;
using Microsoft.Data.Sqlite;

namespace JL.Linux.GUI;

internal sealed class PreferencesWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly List<Action<SqliteConnection, Dictionary<string, string>>> _savers = [];

    public PreferencesWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        Title = "JL Preferences";
        Width = 680;
        Height = 640;
        MinWidth = 520;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        UiConfig ui = UiConfig.Instance;
        CoreConfigManager core = CoreConfigManager.Instance;

        TabControl tabs = new();
        tabs.Items.Add(Page("General",
            Header("Text capture"),
            Bool("Capture text from the clipboard", core.CaptureTextFromClipboard, nameof(CoreConfigManager.CaptureTextFromClipboard)),
            Bool("Capture text from WebSocket (e.g. Textractor, LunaTranslator)", core.CaptureTextFromWebSocket, nameof(CoreConfigManager.CaptureTextFromWebSocket)),
            Text("WebSocket addresses (one per line)", string.Join('\n', core.WebSocketUris.Select(static u => u.OriginalString)), nameof(CoreConfigManager.WebSocketUris), multiline: true),
            Bool("Reconnect to WebSocket automatically", core.AutoReconnectToWebSocket, nameof(CoreConfigManager.AutoReconnectToWebSocket)),
            Bool("Only capture text containing Japanese", ui.OnlyCaptureTextWithJapaneseChars, Ui(nameof(UiConfig.OnlyCaptureTextWithJapaneseChars))),
            Bool("Ignore text identical to the current text", ui.DiscardIdenticalText, Ui(nameof(UiConfig.DiscardIdenticalText))),
            Bool("Trim surrounding whitespace", core.TextBoxTrimWhiteSpaceCharacters, nameof(CoreConfigManager.TextBoxTrimWhiteSpaceCharacters)),
            Bool("Remove newlines", core.TextBoxRemoveNewlines, nameof(CoreConfigManager.TextBoxRemoveNewlines)),
            Number("Ignore text longer than (0 = no limit)", ui.MaxTextLengthToCapture, Ui(nameof(UiConfig.MaxTextLengthToCapture)), 0, 100000, 10, true),
            Number("Backlog size (0 = off, -1 = unlimited)", ui.MaxBacklogCapacity, Ui(nameof(UiConfig.MaxBacklogCapacity)), -1, 100000, 100, true),
            Header("Other"),
            Bool("Check for new JL releases on startup", core.CheckForJLUpdatesOnStartUp, nameof(CoreConfigManager.CheckForJLUpdatesOnStartUp)),
            Bool("Track how often each term is looked up", core.TrackTermLookupCounts, nameof(CoreConfigManager.TrackTermLookupCounts)),
            Text("mpv IPC socket path", core.MpvNamedPipePath, nameof(CoreConfigManager.MpvNamedPipePath))));

        tabs.Items.Add(Page("Lookup",
            EnumChoice("Look up words on", ui.LookupTrigger, Ui(nameof(UiConfig.LookupTrigger))),
            Note("Mouse move: hover. Shift/Control/Alt: hover while holding the key. Left click: click a word."),
            Number("Max search length (characters)", ui.MaxSearchLength, Ui(nameof(UiConfig.MaxSearchLength)), 1, 200, 1, true),
            Bool("Highlight the matched text", ui.HighlightLongestMatch, Ui(nameof(UiConfig.HighlightLongestMatch))),
            Number("Results shown before mining mode (0 = all)", ui.MaxNumResultsNotInMiningMode, Ui(nameof(UiConfig.MaxNumResultsNotInMiningMode)), 0, 100, 1, true),
            Header("Popup"),
            Number("Max width", ui.PopupMaxWidth, Ui(nameof(UiConfig.PopupMaxWidth)), 200, 4000, 10, true),
            Number("Max height", ui.PopupMaxHeight, Ui(nameof(UiConfig.PopupMaxHeight)), 100, 4000, 10, true),
            Number("Horizontal offset from cursor", ui.PopupXOffset, Ui(nameof(UiConfig.PopupXOffset)), -500, 500, 1, true),
            Number("Vertical offset from cursor", ui.PopupYOffset, Ui(nameof(UiConfig.PopupYOffset)), -500, 500, 1, true)));

        tabs.Items.Add(Page("Appearance",
            Header("Main window"),
            Bool("Always on top", ui.AlwaysOnTop, Ui(nameof(UiConfig.AlwaysOnTop))),
            Text("Font (comma-separated fallbacks)", ui.MainWindowFont, Ui(nameof(UiConfig.MainWindowFont))),
            Number("Font size", ui.MainWindowFontSize, Ui(nameof(UiConfig.MainWindowFontSize)), 6, 200, 1, false),
            Text("Text color (#AARRGGBB)", ui.MainWindowTextColor, Ui(nameof(UiConfig.MainWindowTextColor))),
            Text("Background color (#AARRGGBB)", ui.MainWindowBackgroundColor, Ui(nameof(UiConfig.MainWindowBackgroundColor))),
            Number("Background opacity (0–1)", ui.MainWindowOpacity, Ui(nameof(UiConfig.MainWindowOpacity)), 0, 1, 0.05, false),
            Header("Popup"),
            Text("Font", ui.PopupFont, Ui(nameof(UiConfig.PopupFont))),
            Number("Spelling font size", ui.PrimarySpellingFontSize, Ui(nameof(UiConfig.PrimarySpellingFontSize)), 6, 200, 1, false),
            Number("Readings font size", ui.ReadingsFontSize, Ui(nameof(UiConfig.ReadingsFontSize)), 6, 200, 1, false),
            Number("Definitions font size", ui.DefinitionsFontSize, Ui(nameof(UiConfig.DefinitionsFontSize)), 6, 200, 1, false),
            Number("Dictionary name font size", ui.DictTypeFontSize, Ui(nameof(UiConfig.DictTypeFontSize)), 6, 100, 1, false),
            Text("Background color", ui.PopupBackgroundColor, Ui(nameof(UiConfig.PopupBackgroundColor))),
            Number("Background opacity (0–1)", ui.PopupOpacity, Ui(nameof(UiConfig.PopupOpacity)), 0, 1, 0.05, false),
            Text("Spelling color", ui.PrimarySpellingColor, Ui(nameof(UiConfig.PrimarySpellingColor))),
            Text("Readings color", ui.ReadingsColor, Ui(nameof(UiConfig.ReadingsColor))),
            Text("Definitions color", ui.DefinitionsColor, Ui(nameof(UiConfig.DefinitionsColor))),
            Text("Deconjugation info color", ui.DeconjugationInfoColor, Ui(nameof(UiConfig.DeconjugationInfoColor))),
            Text("Frequency color", ui.FrequencyColor, Ui(nameof(UiConfig.FrequencyColor))),
            Text("Dictionary name color", ui.DictTypeColor, Ui(nameof(UiConfig.DictTypeColor)))));

        Button ankiSetup = new() { Content = "Configure decks, note types and fields…" };
        ankiSetup.Click += (_, _) => new AnkiSetupWindow().Show(this);

        tabs.Items.Add(Page("Mining & audio",
            Header("Anki (needs Anki running with the AnkiConnect add-on)"),
            Bool("Enable Anki mining", core.AnkiIntegration, nameof(CoreConfigManager.AnkiIntegration)),
            Text("AnkiConnect address", core.AnkiConnectUri.OriginalString, nameof(CoreConfigManager.AnkiConnectUri)),
            Text("AnkiConnect API key (optional)", core.AnkiConnectApiKey, nameof(CoreConfigManager.AnkiConnectApiKey)),
            Bool("Allow duplicate cards", core.AllowDuplicateCards, nameof(CoreConfigManager.AllowDuplicateCards)),
            Bool("Notify when a card is added", core.NotifyWhenMiningSucceeds, nameof(CoreConfigManager.NotifyWhenMiningSucceeds)),
            Bool("Open the new note in Anki after mining", core.AutoShowAnkiNoteAfterMining, nameof(CoreConfigManager.AutoShowAnkiNoteAfterMining)),
            Bool("Mine to a text file instead of Anki (Resources/mined_*.txt)", ui.MineToFileInsteadOfAnki, Ui(nameof(UiConfig.MineToFileInsteadOfAnki))),
            ankiSetup,
            Header("Audio"),
            Bool("Play audio automatically on lookup", ui.AutoPlayAudio, Ui(nameof(UiConfig.AutoPlayAudio))),
            Text("Audio player command (blank = auto: mpv, then ffplay)", ui.AudioPlayerCommand, Ui(nameof(UiConfig.AudioPlayerCommand))),
            Note("The file path is appended, e.g. \"mpv --no-video --volume=70\".")));

        Button save = new() { Content = "Save", MinWidth = 90, IsDefault = true };
        save.Click += (_, _) => SaveAndClose();
        Button cancel = new() { Content = "Cancel", MinWidth = 90, IsCancel = true };
        cancel.Click += (_, _) => Close();

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(12)
        };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        DockPanel root = new();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabs);
        Content = root;
    }

    private static string Ui(string propertyName) => UiConfig.KeyFor(propertyName);

    private void SaveAndClose()
    {
        try
        {
            using SqliteConnection connection = ConfigDBManager.CreateReadWriteDBConnection();
            Dictionary<string, string> configs = ConfigDBManager.GetAllConfigs(connection);
            foreach (Action<SqliteConnection, Dictionary<string, string>> saver in _savers)
            {
                saver(connection, configs);
            }

            CoreConfigManager.Instance.ApplyPreferences(connection, ConfigDBManager.GetAllConfigs(connection));
            UiConfig.Load(connection, ConfigDBManager.GetAllConfigs(connection));
        }
        catch (Exception ex)
        {
            LoggerManager.Logger.Error(ex, "Saving preferences failed");
            _mainWindow.Notify(JL.Core.Frontend.NotificationLevel.Error, $"Couldn't save preferences: {ex.Message}");
            return;
        }

        _mainWindow.OnPreferencesChanged();
        Close();
    }

    // ---------------------------------------------------------------- form builders

    private static TabItem Page(string header, params Control[] rows)
    {
        StackPanel panel = new() { Spacing = 6, Margin = new Thickness(16, 12, 16, 16) };
        foreach (Control row in rows)
        {
            panel.Children.Add(row);
        }

        return new TabItem
        {
            Header = header,
            Content = new ScrollViewer { Content = panel }
        };
    }

    private static TextBlock Header(string text)
    {
        return new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 15, Margin = new Thickness(0, 10, 0, 2) };
    }

    private static TextBlock Note(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, -2, 0, 4)
        };
    }

    private static Control Row(string label, Control control)
    {
        TextBlock labelBlock = new()
        {
            Text = label,
            Width = 280,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        DockPanel row = new();
        DockPanel.SetDock(labelBlock, Dock.Left);
        row.Children.Add(labelBlock);
        row.Children.Add(control);
        return row;
    }

    private CheckBox Bool(string label, bool value, string key)
    {
        CheckBox checkBox = new() { Content = label, IsChecked = value };
        _savers.Add((c, configs) => UiConfig.Save(c, configs, key, UiConfig.Format(checkBox.IsChecked is true)));
        return checkBox;
    }

    private Control Number(string label, double value, string key, double min, double max, double step, bool integer)
    {
        NumericUpDown numeric = new()
        {
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            Increment = (decimal)step,
            Value = (decimal)Math.Clamp(value, min, max),
            FormatString = integer ? "0" : "0.##",
            MinWidth = 140,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        _savers.Add((c, configs) =>
        {
            double result = (double)(numeric.Value ?? (decimal)value);
            UiConfig.Save(c, configs, key, integer ? UiConfig.Format((int)Math.Round(result)) : UiConfig.Format(result));
        });

        return Row(label, numeric);
    }

    private Control Text(string label, string value, string key, bool multiline = false)
    {
        TextBox textBox = new()
        {
            Text = value,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 70 : 0
        };

        _savers.Add((c, configs) => UiConfig.Save(c, configs, key, (textBox.Text ?? "").Trim()));
        return Row(label, textBox);
    }

    private Control EnumChoice<T>(string label, T value, string key) where T : struct, System.Enum
    {
        ComboBox comboBox = new()
        {
            ItemsSource = System.Enum.GetValues<T>(),
            SelectedItem = value,
            MinWidth = 180
        };

        _savers.Add((c, configs) =>
        {
            T selected = comboBox.SelectedItem is T t ? t : value;
            UiConfig.Save(c, configs, key, selected.ToString());
        });

        return Row(label, comboBox);
    }
}
