using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using JL.Core.Config;
using JL.Core.External.AnkiConnect;
using JL.Core.Frontend;
using JL.Core.Mining;
using JL.Core.Utilities;

namespace JL.Linux.GUI;

/// <summary>
/// Pick the deck, note type and field mapping for each kind of card JL mines
/// (word, kanji, name, other). Writes Config/AnkiConfig.json through JL.Core.
/// </summary>
internal sealed class AnkiSetupWindow : Window
{
    private readonly Dictionary<MineType, MineTypeEditor> _editors = [];
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center };
    private string[] _deckNames = [];
    private string[] _modelNames = [];

    public AnkiSetupWindow()
    {
        Title = "JL Anki setup";
        Width = 760;
        Height = 660;
        MinWidth = 560;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        TabControl tabs = new();
        AddEditor(tabs, MineType.Word, "Words", JLFieldUtils.JLFieldsForWordDicts);
        AddEditor(tabs, MineType.Kanji, "Kanji", JLFieldUtils.JLFieldsForKanjiDicts);
        AddEditor(tabs, MineType.Name, "Names", JLFieldUtils.JLFieldsForNameDicts);
        AddEditor(tabs, MineType.Other, "Other", JLFieldUtils.JLFieldsForWordDicts);

        Button reload = new() { Content = "Reload from Anki" };
        reload.Click += async (_, _) => await LoadAsync().ConfigureAwait(true);
        Button save = new() { Content = "Save", MinWidth = 90, IsDefault = true };
        save.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);
        Button close = new() { Content = "Close", MinWidth = 90, IsCancel = true };
        close.Click += (_, _) => Close();

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(reload);
        buttons.Children.Add(save);
        buttons.Children.Add(close);

        DockPanel bottom = new() { Margin = new Thickness(12) };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(_status);

        DockPanel root = new();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(tabs);
        Content = root;

        Opened += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    private void AddEditor(TabControl tabs, MineType mineType, string header, JLField[] allowedFields)
    {
        MineTypeEditor editor = new(allowedFields, _status);
        _editors[mineType] = editor;
        tabs.Items.Add(new TabItem { Header = header, Content = editor.Root });
    }

    private async Task LoadAsync()
    {
        _status.Text = $"Connecting to AnkiConnect at {CoreConfigManager.Instance.AnkiConnectUri}…";

        Dictionary<MineType, AnkiConfig>? existing = null;
        try
        {
            existing = await AnkiConfigUtils.ReadAnkiConfig(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LoggerManager.Logger.Warning(ex, "Couldn't read AnkiConfig");
        }

        string[]? decks = null;
        string[]? models = null;
        try
        {
            decks = await AnkiConnectUtils.GetDeckNames().ConfigureAwait(true);
            models = await AnkiConnectUtils.GetModelNames().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LoggerManager.Logger.Warning(ex, "AnkiConnect request failed");
        }

        _deckNames = decks ?? [];
        _modelNames = models ?? [];

        foreach ((MineType mineType, MineTypeEditor editor) in _editors)
        {
            AnkiConfig? config = existing is not null && existing.TryGetValue(mineType, out AnkiConfig? c) ? c : null;
            await editor.Populate(_deckNames, _modelNames, config, ankiReachable: decks is not null).ConfigureAwait(true);
        }

        _status.Text = decks is null
            ? "Couldn't reach AnkiConnect. Start Anki (with the AnkiConnect add-on) and press Reload. Existing settings are shown."
            : "Connected to Anki. Pick a deck and note type for each tab, map the fields, then Save.";
    }

    private async Task SaveAsync()
    {
        Dictionary<MineType, AnkiConfig> result = [];
        foreach ((MineType mineType, MineTypeEditor editor) in _editors)
        {
            AnkiConfig? config = editor.BuildConfig();
            if (config is not null)
            {
                result[mineType] = config;
            }
        }

        if (result.Count is 0)
        {
            _status.Text = "Nothing to save: choose at least a deck and a note type on one tab.";
            return;
        }

        await AnkiConfigUtils.WriteAnkiConfig(result).ConfigureAwait(true);
        _status.Text = "Saved.";

        if (!CoreConfigManager.Instance.AnkiIntegration)
        {
            FrontendManager.Frontend.Notify(NotificationLevel.Information, "Anki setup saved. Turn on \"Enable Anki mining\" in Preferences to start mining.");
        }
        else
        {
            FrontendManager.Frontend.Notify(NotificationLevel.Success, "Anki setup saved.");
        }
    }

    private sealed class MineTypeEditor
    {
        private readonly JLField[] _allowedFields;
        private readonly TextBlock _status;
        private readonly ComboBox _deck = new() { MinWidth = 320 };
        private readonly ComboBox _model = new() { MinWidth = 320 };
        private readonly StackPanel _fields = new() { Spacing = 4 };
        private readonly TextBox _tags = new() { Watermark = "Tags, separated by spaces (optional)" };
        private readonly Dictionary<string, ComboBox> _fieldBoxes = new(StringComparer.Ordinal);
        private AnkiConfig? _existing;
        private bool _ankiReachable;
        private bool _populating;

        public MineTypeEditor(JLField[] allowedFields, TextBlock status)
        {
            _allowedFields = allowedFields;
            _status = status;

            _model.SelectionChanged += async (_, _) =>
            {
                if (!_populating && _model.SelectedItem is string modelName)
                {
                    await LoadFields(modelName).ConfigureAwait(true);
                }
            };

            Root = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Spacing = 8,
                    Margin = new Thickness(16),
                    Children =
                    {
                        Labeled("Deck", _deck),
                        Labeled("Note type", _model),
                        new TextBlock { Text = "Fields", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) },
                        _fields,
                        Labeled("Tags", _tags)
                    }
                }
            };
        }

        public Control Root { get; }

        public async Task Populate(string[] decks, string[] models, AnkiConfig? existing, bool ankiReachable)
        {
            _populating = true;
            _existing = existing;
            _ankiReachable = ankiReachable;

            _deck.ItemsSource = WithExisting(decks, existing?.DeckName);
            _deck.SelectedItem = existing?.DeckName;
            _model.ItemsSource = WithExisting(models, existing?.ModelName);
            _model.SelectedItem = existing?.ModelName;
            _tags.Text = existing?.Tags is null ? "" : string.Join(' ', existing.Tags);
            _populating = false;

            if (existing is not null)
            {
                await LoadFields(existing.ModelName).ConfigureAwait(true);
            }
            else
            {
                _fields.Children.Clear();
                _fieldBoxes.Clear();
            }
        }

        private async Task LoadFields(string modelName)
        {
            string[]? fieldNames = null;
            if (_ankiReachable)
            {
                try
                {
                    fieldNames = await AnkiConnectUtils.GetFieldNames(modelName, CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    LoggerManager.Logger.Warning(ex, "Couldn't get field names for {Model}", modelName);
                }
            }

            // Offline: fall back to the fields saved last time for this note type.
            if (fieldNames is null && _existing is not null && _existing.ModelName == modelName)
            {
                fieldNames = _existing.Fields.Keys.ToArray();
            }

            _fields.Children.Clear();
            _fieldBoxes.Clear();
            if (fieldNames is null)
            {
                _status.Text = $"Couldn't get the fields of \"{modelName}\".";
                return;
            }

            ComboBoxItem[] MakeItems() => _allowedFields
                .Select(static f => new ComboBoxItem { Content = f.GetDescription(), Tag = f })
                .ToArray();

            foreach (string fieldName in fieldNames)
            {
                ComboBoxItem[] items = MakeItems();
                JLField current = _existing is not null && _existing.ModelName == modelName && _existing.Fields.TryGetValue(fieldName, out JLField saved)
                    ? saved
                    : JLField.Nothing;

                ComboBox box = new()
                {
                    ItemsSource = items,
                    SelectedItem = items.FirstOrDefault(i => i.Tag is JLField f && f == current) ?? items.FirstOrDefault(),
                    MinWidth = 320
                };

                _fieldBoxes[fieldName] = box;
                _fields.Children.Add(Labeled(fieldName, box));
            }
        }

        public AnkiConfig? BuildConfig()
        {
            if (_deck.SelectedItem is not string deck || _model.SelectedItem is not string model || _fieldBoxes.Count is 0)
            {
                return null;
            }

            OrderedDictionary<string, JLField> fields = new(StringComparer.Ordinal);
            foreach ((string fieldName, ComboBox box) in _fieldBoxes)
            {
                fields[fieldName] = (box.SelectedItem as ComboBoxItem)?.Tag is JLField f ? f : JLField.Nothing;
            }

            string[] tags = (_tags.Text ?? "").Split(new[] { ' ', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new AnkiConfig(deck, model, fields, tags.Length > 0 ? tags : null);
        }

        private static string[] WithExisting(string[] names, string? existing)
        {
            return existing is not null && !names.Contains(existing, StringComparer.Ordinal)
                ? [.. names, existing]
                : names;
        }

        private static Control Labeled(string label, Control control)
        {
            TextBlock labelBlock = new() { Text = label, Width = 200, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            DockPanel row = new();
            DockPanel.SetDock(labelBlock, Dock.Left);
            row.Children.Add(labelBlock);
            row.Children.Add(control);
            return row;
        }
    }
}
