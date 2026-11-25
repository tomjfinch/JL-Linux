using System.Collections.Frozen;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using JL.Core;
using JL.Core.Dicts;
using JL.Core.Dicts.Interfaces;
using JL.Core.Dicts.Options;
using JL.Core.Frontend;
using JL.Core.Utilities;
using JL.Core.Utilities.Database;
using JL.Core.Utilities.ObjectPool;

namespace JL.Linux.GUI;

/// <summary>
/// Enable/disable, reorder, add and remove dictionaries. Mirrors the core logic of
/// JL.Windows' ManageDictionariesWindow and AddDictionaryWindow.
/// </summary>
internal sealed class DictionariesWindow : Window
{
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly ComboBox _typeComboBox = new() { MinWidth = 260 };
    private readonly TextBox _nameTextBox = new() { Watermark = "Name, e.g. Jitendex" };
    private readonly TextBox _pathTextBox = new() { Watermark = "Folder of an unzipped Yomitan dictionary, or a Nazeka .json file" };
    private readonly TextBlock _addError = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };

    public DictionariesWindow()
    {
        Title = "JL Dictionaries";
        Width = 820;
        Height = 640;
        MinWidth = 600;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        TextBlock intro = new()
        {
            Text = "Higher in the list = shown first. Changes take effect when this window closes. "
                 + "Yomitan/Yomichan dictionaries must be unzipped into a folder first.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Margin = new Thickness(12, 10, 12, 6)
        };

        Button browse = new() { Content = "Browse…" };
        browse.Click += async (_, _) => await BrowseAsync().ConfigureAwait(true);

        Button add = new() { Content = "Add dictionary" };
        add.Click += (_, _) => AddDictionary();

        DockPanel pathRow = new();
        DockPanel.SetDock(browse, Dock.Right);
        browse.Margin = new Thickness(6, 0, 0, 0);
        pathRow.Children.Add(browse);
        pathRow.Children.Add(_pathTextBox);

        StackPanel addPanel = new()
        {
            Spacing = 6,
            Margin = new Thickness(12),
            Children =
            {
                new TextBlock { Text = "Add a dictionary", FontWeight = FontWeight.SemiBold },
                _typeComboBox,
                _nameTextBox,
                pathRow,
                _addError,
                add
            }
        };

        Border addBorder = new()
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = addPanel
        };

        DockPanel root = new();
        DockPanel.SetDock(intro, Dock.Top);
        DockPanel.SetDock(addBorder, Dock.Bottom);
        root.Children.Add(intro);
        root.Children.Add(addBorder);
        root.Children.Add(new ScrollViewer { Content = _list, Margin = new Thickness(6, 0, 6, 0) });
        Content = root;

        Refresh();
        Closed += OnClosed;
    }

    private void Refresh()
    {
        _list.Children.Clear();
        Dict[] dicts = DictUtils.Dicts.Values.OrderBy(static d => d.Priority).ToArray();

        for (int i = 0; i < dicts.Length; i++)
        {
            _list.Children.Add(BuildRow(dicts, i));
        }

        // Types that can be added: Yomichan/Nazeka formats, minus single-instance types already present.
        IEnumerable<DictType> loaded = DictUtils.Dicts.Values.Select(static d => d.Type);
        DictType[] addable = DictUtils.YomichanDictTypes.Concat(DictUtils.NazekaDictTypes)
            .Except(loaded.Except(DictUtils.NonspecificDictTypes))
            .ToArray();

        _typeComboBox.ItemsSource = addable.Select(static t => new ComboBoxItem { Content = t.GetDescription(), Tag = t }).ToArray();
        _typeComboBox.SelectedIndex = addable.Length > 0 ? 0 : -1;
    }

    private Control BuildRow(Dict[] sortedDicts, int index)
    {
        Dict dict = sortedDicts[index];

        CheckBox active = new() { IsChecked = dict.Active, VerticalAlignment = VerticalAlignment.Center };
        active.IsCheckedChanged += (_, _) => dict.Active = active.IsChecked is true;

        string fullPath = Path.GetFullPath(dict.Path, AppInfo.ApplicationPath);
        string status = dict.Updating ? "updating…"
            : !Path.Exists(fullPath) ? "file not found"
            : dict.Ready ? "ready"
            : dict.Active ? "loading…" : "off";

        StackPanel info = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = dict.Name, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = $"{dict.Type.GetDescription()} · {status} · {dict.Path}", FontSize = 12, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis }
            }
        };

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

        Button up = new() { Content = "↑", IsEnabled = index > 0 };
        up.Click += (_, _) => Swap(sortedDicts[index], sortedDicts[index - 1]);
        Button down = new() { Content = "↓", IsEnabled = index + 1 < sortedDicts.Length };
        down.Click += (_, _) => Swap(sortedDicts[index], sortedDicts[index + 1]);
        buttons.Children.Add(up);
        buttons.Children.Add(down);

        bool builtIn = DictUtils.BuiltInDicts.Values.Any(d => d.Type == dict.Type);
        if (!builtIn)
        {
            Button remove = new() { Content = "Remove" };
            remove.Click += async (_, _) =>
            {
                if (await MessageDialog.ShowYesNo(this, "Remove dictionary", $"Remove \"{dict.Name}\"?").ConfigureAwait(true))
                {
                    RemoveDictionary(dict);
                }
            };
            buttons.Children.Add(remove);
        }

        DockPanel row = new() { Margin = new Thickness(6, 4, 6, 4) };
        DockPanel.SetDock(active, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(active);
        row.Children.Add(buttons);
        row.Children.Add(info);
        return row;
    }

    private void Swap(Dict a, Dict b)
    {
        (a.Priority, b.Priority) = (b.Priority, a.Priority);
        Refresh();
    }

    private void RemoveDictionary(Dict dict)
    {
        dict.Contents = FrozenDictionary<string, IList<IDictRecord>>.Empty;
        _ = DictUtils.Dicts.Remove(dict.Name);

        if (File.Exists(dict.DBPath))
        {
            DBUtils.DeleteDB(dict.DBPath);
        }

        if (dict.Type is DictType.PitchAccentYomichan)
        {
            _ = DictUtils.SingleDictTypeDicts.Remove(DictType.PitchAccentYomichan);
        }

        foreach (Dict d in DictUtils.Dicts.Values)
        {
            if (d.Priority > dict.Priority)
            {
                d.Priority -= 1;
            }
        }

        Refresh();
    }

    private async Task BrowseAsync()
    {
        if ((_typeComboBox.SelectedItem as ComboBoxItem)?.Tag is not DictType type)
        {
            return;
        }

        string? path;
        if (DictUtils.YomichanDictTypes.Contains(type))
        {
            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose the unzipped dictionary folder",
                AllowMultiple = false
            }).ConfigureAwait(true);

            path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }
        else
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose the Nazeka dictionary file",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Nazeka dictionary") { Patterns = ["*.json"] }]
            }).ConfigureAwait(true);

            path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        if (path is null)
        {
            return;
        }

        _pathTextBox.Text = PathUtils.GetPortablePath(path);
        if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
        {
            _nameTextBox.Text = Path.GetFileName(path.TrimEnd('/'));
        }
    }

    private void AddDictionary()
    {
        _addError.Text = "";
        if ((_typeComboBox.SelectedItem as ComboBoxItem)?.Tag is not DictType type)
        {
            _addError.Text = "Choose a dictionary type.";
            return;
        }

        string path = (_pathTextBox.Text ?? "").Trim();
        string fullPath = Path.GetFullPath(path.Length > 0 ? path : ".", AppInfo.ApplicationPath);
        if (path.Length is 0 || !Path.Exists(fullPath))
        {
            _addError.Text = "That path doesn't exist.";
            return;
        }

        path = PathUtils.GetPortablePath(fullPath);
        if (DictUtils.Dicts.Values.Any(d => d.Path == path))
        {
            _addError.Text = "A dictionary with this path is already added.";
            return;
        }

        string name = (_nameTextBox.Text ?? "").Trim();
        if (name.Length is 0 || name.Length > 128 || name.IndexOfAny(PathUtils.InvalidFileNameChars) >= 0)
        {
            _addError.Text = "Enter a valid name (no slashes, up to 128 characters).";
            return;
        }

        if (DictUtils.Dicts.ContainsKey(name))
        {
            _addError.Text = "A dictionary with this name already exists.";
            return;
        }

        if (DictUtils.YomichanDictTypes.Contains(type))
        {
            string pattern = type is DictType.NonspecificKanjiYomichan
                ? "kanji_bank_*.json"
                : type is DictType.PitchAccentYomichan
                    ? "term_meta_bank_*.json"
                    : "term_bank_*.json";

            if (!Directory.Exists(fullPath) || !Directory.EnumerateFiles(fullPath, pattern, SearchOption.TopDirectoryOnly).Any())
            {
                _addError.Text = $"No {pattern} files in that folder. Pick the unzipped dictionary folder, and check the type.";
                return;
            }
        }

        int maxSearchKeyLength = DictUtils.KanjiDictTypes.Contains(type) ? 1 : 0;
        Dict dict = new(type, name, path, true, DictUtils.Dicts.Count + 1, 0, DefaultOptions(type), maxSearchKeyLength);
        DictUtils.Dicts.Add(name, dict);

        if (type is DictType.PitchAccentYomichan)
        {
            DictUtils.SingleDictTypeDicts[DictType.PitchAccentYomichan] = dict;
        }

        _nameTextBox.Text = "";
        _pathTextBox.Text = "";
        Refresh();
    }

    /// <summary>The same defaults JL.Core fills in for these types (DictUtils, deserialization).</summary>
    private static DictOptions DefaultOptions(DictType type)
    {
        return new DictOptions(
            new UseDBOption(UseDBOption.ValidDictTypes.Contains(type)),
            new NoAllOption(false),
            newlineBetweenDefinitions: NewlineBetweenDefinitionsOption.ValidDictTypes.Contains(type) ? new NewlineBetweenDefinitionsOption(true) : null,
            pitchAccentMarkerColor: PitchAccentMarkerColorOption.ValidDictTypes.Contains(type) ? new PitchAccentMarkerColorOption("#FF00BFFF") : null,
            showPitchAccentWithDottedLines: ShowPitchAccentWithDottedLinesOption.ValidDictTypes.Contains(type) ? new ShowPitchAccentWithDottedLinesOption(true) : null,
            autoUpdateAfterNDays: AutoUpdateAfterNDaysOption.ValidDictTypes.Contains(type) ? new AutoUpdateAfterNDaysOption(0) : null,
            showImages: ShowImagesOption.ValidDictTypes.Contains(type) ? new ShowImagesOption(true) : null,
            showImageAtBottom: ShowImageAtBottomOption.ValidDictTypes.Contains(type) ? new ShowImageAtBottomOption(true) : null,
            maxImageWidth: MaxImageWidthOption.ValidDictTypes.Contains(type) ? new MaxImageWidthOption(0) : null,
            maxImageHeight: MaxImageHeightOption.ValidDictTypes.Contains(type) ? new MaxImageHeightOption(0) : null,
            generateMazegakiVariants: GenerateMazegakiVariantsOption.ValidDictTypes.Contains(type) ? new GenerateMazegakiVariantsOption(false) : null,
            generateFusejiVariants: GenerateFusejiVariantsOption.ValidDictTypes.Contains(type) ? new GenerateFusejiVariantsOption(false) : null,
            maxSearchKeyLengthForFusejiGeneration: MaxSearchKeyLengthForFusejiGenerationOption.ValidDictTypes.Contains(type) ? new MaxSearchKeyLengthForFusejiGenerationOption(9) : null,
            maxTotalFusejiCount: MaxTotalFusejiCountOption.ValidDictTypes.Contains(type) ? new MaxTotalFusejiCountOption(1) : null);
    }

    private static async void OnClosed(object? sender, EventArgs e)
    {
        try
        {
            await Task.Run(static async () =>
            {
                await DictUtils.SerializeDicts().ConfigureAwait(false);
                await DictUtils.LoadDictionaries().ConfigureAwait(false);
                await DictUtils.SerializeDicts().ConfigureAwait(false);
                ObjectPoolManager.ClearStringPoolIfDictsAreReady();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LoggerManager.Logger.Error(ex, "Reloading dictionaries failed");
            FrontendManager.Frontend.Notify(NotificationLevel.Error, $"Reloading dictionaries failed: {ex.Message}");
        }
    }
}
