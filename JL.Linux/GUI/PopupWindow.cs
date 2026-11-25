using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using JL.Core;
using JL.Core.Audio;
using JL.Core.Dicts;
using JL.Core.External.AnkiConnect;
using JL.Core.Frontend;
using JL.Core.Lookup;
using JL.Core.Mining;
using JL.Core.Utilities;
using JL.Linux.Config;

namespace JL.Linux.GUI;

/// <summary>
/// The dictionary popup. Unlocked, it follows the mouse over the main text.
/// Locked ("mining mode": click the text, or move the mouse into the popup), it stays
/// put, shows every result, and its definitions can be hovered for a nested popup.
/// </summary>
internal sealed class PopupWindow : Window
{
    private const int MaxNestingLevel = 10;
    private const int MaxRenderedResults = 100;

    private readonly int _level;
    private readonly ScrollViewer _scrollViewer;
    private readonly StackPanel _resultsPanel;
    private readonly Border _rootBorder;
    private readonly DispatcherTimer _hideTimer;
    private readonly Dictionary<int, SelectableTextBlock> _definitionBlocks = [];

    private PopupWindow? _child;
    private LookupResult[] _results = [];
    private string _sourceText = "";
    private int _charPosition;
    private int _renderedCount;
    private PixelPoint _anchor;
    private bool _keepPosition;
    private readonly bool _constructed;

    public PopupWindow(int level)
    {
        _level = level;

        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Title = "JL Popup";

        _resultsPanel = new StackPanel { Orientation = Orientation.Vertical };
        _scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _resultsPanel
        };

        _rootBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = _scrollViewer
        };

        Content = _rootBorder;
        ApplyAppearance();

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _hideTimer.Tick += (_, _) =>
        {
            if (!IsVisible)
            {
                _hideTimer.Stop();
                return;
            }

            if (!IsPointerOverChain())
            {
                HideAll();
            }
        };

        PointerEntered += (_, _) =>
        {
            _hideTimer.Stop();
            if (IsVisible && !IsLocked)
            {
                Lock();
            }
        };

        // A locked popup closes once the pointer has left it (and any nested popup).
        PointerExited += (_, _) =>
        {
            if (IsLocked)
            {
                _hideTimer.Start();
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape)
            {
                HideAll();
                e.Handled = true;
            }
        };

        _constructed = true;
    }

    public bool IsLocked { get; private set; }

    public string? LastLookedUpText { get; private set; }

    public PopupWindow? Child => _child;

    public void ApplyAppearance()
    {
        UiConfig config = UiConfig.Instance;
        MaxWidth = config.PopupMaxWidth;
        MaxHeight = config.PopupMaxHeight;
        FontFamily = new FontFamily(config.PopupFont);
        _rootBorder.Background = Palette.Brush(config.PopupBackgroundColor, config.PopupOpacity);
        _child?.ApplyAppearance();
    }

    public void ShowResults(LookupResult[] results, string sourceText, int charPosition, PixelPoint anchor, string? lookedUpText)
    {
        _child?.HideAll();
        _results = results;
        _sourceText = sourceText;
        _charPosition = charPosition;
        _anchor = anchor;
        _keepPosition = false;
        IsLocked = false;
        _hideTimer.Stop();

        bool sameAsBefore = IsVisible && lookedUpText == LastLookedUpText;
        LastLookedUpText = lookedUpText;

        int limit = UiConfig.Instance.MaxNumResultsNotInMiningMode;
        Render(limit > 0 ? limit : MaxRenderedResults);

        // Place the window before mapping it, so it doesn't flash at the screen corner.
        Reposition();
        if (!IsVisible)
        {
            Show();
            Reposition();
        }

        if (!sameAsBefore && UiConfig.Instance.AutoPlayAudio && results.Length > 0)
        {
            PlayAudio(0);
        }
    }

    /// <summary>Mining mode: stop following the mouse and show everything.</summary>
    public void Lock()
    {
        if (IsLocked || !IsVisible)
        {
            return;
        }

        IsLocked = true;
        _keepPosition = true;
        if (_renderedCount < Math.Min(_results.Length, MaxRenderedResults))
        {
            Render(MaxRenderedResults);
        }
    }

    public void HideAll()
    {
        _child?.HideAll();
        _hideTimer.Stop();
        IsLocked = false;
        LastLookedUpText = null;
        if (IsVisible)
        {
            Hide();
        }
    }

    public bool IsPointerOverChain()
    {
        return (IsVisible && IsPointerOver) || (_child is not null && _child.IsVisible && _child.IsPointerOverChain());
    }

    /// <summary>The deepest visible popup in this chain (used for keyboard shortcuts).</summary>
    public PopupWindow? DeepestVisible()
    {
        if (!IsVisible)
        {
            return null;
        }

        return _child?.DeepestVisible() ?? this;
    }

    public void MineResult(int index)
    {
        if (index >= 0 && index < _results.Length)
        {
            _ = MineAsync(index, null);
        }
    }

    public void PlayAudio(int index)
    {
        if (index < 0 || index >= _results.Length)
        {
            return;
        }

        LookupResult result = _results[index];
        string spelling = result.PrimarySpelling;
        string? reading = result.Readings is { Length: > 0 } ? result.Readings[0] : null;
        Task.Run(() => AudioUtils.GetAndPlayAudio(spelling, reading)).SafeFireAndForget("Unexpected error while playing audio");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty)
        {
            Reposition();
        }
    }

    private void Reposition()
    {
        // ClientSize can change while the base Window is still being constructed.
        if (!_constructed)
        {
            return;
        }

        Screen? screen = Screens.ScreenFromPoint(_anchor) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        UiConfig config = UiConfig.Instance;
        double scale = screen.Scaling;
        PixelRect workArea = screen.WorkingArea;
        int width = (int)Math.Ceiling(ClientSize.Width * scale);
        int height = (int)Math.Ceiling(ClientSize.Height * scale);
        int offsetX = (int)(config.PopupXOffset * scale);
        int offsetY = (int)(config.PopupYOffset * scale);

        int x;
        int y;
        if (_keepPosition)
        {
            x = Position.X;
            y = Position.Y;
        }
        else
        {
            x = _anchor.X + offsetX;
            if (x + width > workArea.Right)
            {
                x = _anchor.X - offsetX - width;
            }

            y = _anchor.Y + offsetY;
            if (y + height > workArea.Bottom)
            {
                y = _anchor.Y - offsetY - height;
            }
        }

        x = Math.Clamp(x, workArea.X, Math.Max(workArea.X, workArea.Right - width));
        y = Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - height));

        PixelPoint position = new(x, y);
        if (position != Position)
        {
            Position = position;
        }
    }

    private void Render(int limit)
    {
        _resultsPanel.Children.Clear();
        _definitionBlocks.Clear();

        int count = Math.Min(_results.Length, Math.Min(limit, MaxRenderedResults));
        for (int i = 0; i < count; i++)
        {
            _resultsPanel.Children.Add(BuildResult(_results[i], i, i + 1 < count));
        }

        if (count < _results.Length && !IsLocked)
        {
            _resultsPanel.Children.Add(new TextBlock
            {
                Text = $"+{_results.Length - count} more — click the text or move into this popup to see all",
                FontSize = UiConfig.Instance.DictTypeFontSize,
                Foreground = Palette.Brush(UiConfig.Instance.DeconjugationInfoColor),
                Margin = new Thickness(10, 2, 10, 6)
            });
        }

        _renderedCount = count;
        _scrollViewer.Offset = default;
    }

    private Control BuildResult(LookupResult result, int index, bool separator)
    {
        UiConfig config = UiConfig.Instance;
        StackPanel item = new() { Spacing = 2, Margin = new Thickness(10, 6, 10, 6) };

        // Header: spelling, readings, alternative spellings, frequency, dictionary
        WrapPanel header = new() { Orientation = Orientation.Horizontal };
        header.Children.Add(MakeText(result.PrimarySpelling, config.PrimarySpellingFontSize, config.PrimarySpellingColor, new Thickness(0, 0, 8, 0)));

        string? readings = ReadingsText(result);
        if (readings is not null)
        {
            header.Children.Add(MakeText(readings, config.ReadingsFontSize, config.ReadingsColor, new Thickness(0, 0, 8, 0), VerticalAlignment.Bottom));
        }

        if (result.AlternativeSpellings is { Length: > 0 })
        {
            header.Children.Add(MakeText($"({string.Join("、", result.AlternativeSpellings)})", config.ReadingsFontSize * 0.85, config.ReadingsColor, new Thickness(0, 0, 8, 0), VerticalAlignment.Bottom));
        }

        if (result.Frequencies is { Count: > 0 })
        {
            string? freqText = LookupResultUtils.ValidFrequenciesToText(CollectionsMarshal.AsSpan(result.Frequencies), result.Frequencies.Count is 1);
            if (freqText is not null)
            {
                header.Children.Add(MakeText(freqText, config.DictTypeFontSize + 1, config.FrequencyColor, new Thickness(0, 0, 8, 0), VerticalAlignment.Center));
            }
        }

        header.Children.Add(new Border
        {
            BorderBrush = Palette.Brush(config.DictTypeColor, 0.6),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = MakeText(result.Dict.Name, config.DictTypeFontSize, config.DictTypeColor, default)
        });

        // Actions
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
        Button mineButton = MakeButton("＋ Mine", "Add this entry to Anki (or the mining file)");
        mineButton.Click += (_, _) => _ = MineAsync(index, mineButton);
        Button audioButton = MakeButton("♪", "Play audio");
        audioButton.Click += (_, _) => PlayAudio(index);
        actions.Children.Add(audioButton);
        actions.Children.Add(mineButton);

        DockPanel top = new();
        DockPanel.SetDock(actions, Dock.Right);
        top.Children.Add(actions);
        top.Children.Add(header);
        item.Children.Add(top);

        if (!string.IsNullOrEmpty(result.DeconjugationProcess))
        {
            item.Children.Add(MakeText(result.DeconjugationProcess, config.DefinitionsFontSize * 0.85, config.DeconjugationInfoColor, default));
        }

        if (result.KanjiLookupResult is not null)
        {
            string kanjiInfo = KanjiText(result.KanjiLookupResult);
            if (kanjiInfo.Length > 0)
            {
                SelectableTextBlock kanjiBlock = MakeSelectable(kanjiInfo, config.DefinitionsFontSize, config.DefinitionsColor);
                AttachNestedLookup(kanjiBlock);
                item.Children.Add(kanjiBlock);
            }
        }

        bool showImages = result.Dict.Options.ShowImages?.Value ?? true;
        bool imagesAtBottom = result.Dict.Options.ShowImageAtBottom?.Value ?? false;
        if (showImages && !imagesAtBottom)
        {
            AddImages(item, result);
        }

        if (!string.IsNullOrEmpty(result.FormattedDefinitions))
        {
            SelectableTextBlock definitions = MakeSelectable(result.FormattedDefinitions, config.DefinitionsFontSize, config.DefinitionsColor);
            AttachNestedLookup(definitions);
            _definitionBlocks[index] = definitions;
            item.Children.Add(definitions);
        }

        if (showImages && imagesAtBottom)
        {
            AddImages(item, result);
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = separator ? new Thickness(0, 0, 0, 1) : default,
            Child = item
        };
    }

    private static string? ReadingsText(LookupResult result)
    {
        string[]? readings = result.Readings;
        if (readings is null || readings.Length is 0)
        {
            return null;
        }

        byte[]? pitch = result.PitchPositions;
        if (pitch is null)
        {
            return string.Join("、", readings);
        }

        // Pitch accent as the conventional downstep number, e.g. はし[2]
        StringBuilder sb = new();
        for (int i = 0; i < readings.Length; i++)
        {
            if (i > 0)
            {
                _ = sb.Append('、');
            }

            _ = sb.Append(readings[i]);
            if (i < pitch.Length && pitch[i] is not byte.MaxValue)
            {
                _ = sb.Append('[').Append(pitch[i]).Append(']');
            }
        }

        return sb.ToString();
    }

    private static string KanjiText(KanjiLookupResult kanji)
    {
        StringBuilder sb = new();
        AppendLine(sb, "On", kanji.OnReadings);
        AppendLine(sb, "Kun", kanji.KunReadings);
        AppendLine(sb, "Nanori", kanji.NanoriReadings);
        AppendLine(sb, "Radicals", kanji.RadicalNames);
        AppendLine(sb, "Composition", kanji.KanjiComposition);

        List<string> facts = [];
        if (kanji.StrokeCount > 0)
        {
            facts.Add($"Strokes: {kanji.StrokeCount}");
        }

        if (kanji.KanjiGrade is not byte.MaxValue)
        {
            facts.Add($"Grade: {LookupResultUtils.GradeToText(kanji.KanjiGrade)}");
        }

        if (facts.Count > 0)
        {
            _ = sb.AppendLine(string.Join("   ", facts));
        }

        if (!string.IsNullOrEmpty(kanji.KanjiStats))
        {
            _ = sb.AppendLine(kanji.KanjiStats);
        }

        return sb.ToString().TrimEnd();

        static void AppendLine(StringBuilder sb, string label, string[]? values)
        {
            if (values is { Length: > 0 })
            {
                _ = sb.Append(label).Append(": ").AppendLine(string.Join("、", values));
            }
        }
    }

    private static void AddImages(StackPanel item, LookupResult result)
    {
        ImageInfo[]? images = result.ImageInfos;
        if (images is null)
        {
            return;
        }

        UiConfig config = UiConfig.Instance;
        int maxImageWidth = result.Dict.Options.MaxImageWidth?.Value ?? 0;
        int maxImageHeight = result.Dict.Options.MaxImageHeight?.Value ?? 0;

        foreach (ImageInfo info in images)
        {
            try
            {
                // Dictionaries imported by Windows JL store image paths with backslashes.
                string fullPath = Path.GetFullPath(info.Path.Replace('\\', '/'), AppInfo.ApplicationPath);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                double maxWidth = Math.Min(info.Width > 0 ? info.Width : double.PositiveInfinity, config.PopupMaxWidth - 40);
                if (maxImageWidth > 0)
                {
                    maxWidth = Math.Min(maxWidth, maxImageWidth);
                }

                double maxHeight = info.Height > 0 ? info.Height : double.PositiveInfinity;
                if (maxImageHeight > 0)
                {
                    maxHeight = Math.Min(maxHeight, maxImageHeight);
                }

                item.Children.Add(new Image
                {
                    Source = new Bitmap(fullPath),
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    MaxWidth = maxWidth,
                    MaxHeight = maxHeight,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 4, 0, 4)
                });
            }
            catch (Exception ex)
            {
                LoggerManager.Logger.Debug(ex, "Couldn't load image {Path}", info.Path);
            }
        }
    }

    private void AttachNestedLookup(SelectableTextBlock block)
    {
        block.AddHandler(PointerMovedEvent, (_, e) => OnNestedPointerMoved(block, e, false), RoutingStrategies.Tunnel);
        block.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (UiConfig.Instance.LookupTrigger is LookupTrigger.LeftClick && e.GetCurrentPoint(block).Properties.IsLeftButtonPressed)
            {
                OnNestedPointerMoved(block, e, true);
            }
        }, RoutingStrategies.Tunnel);
    }

    private void OnNestedPointerMoved(SelectableTextBlock block, PointerEventArgs e, bool clicked)
    {
        if (!IsLocked || _level + 1 >= MaxNestingLevel)
        {
            return;
        }

        if (!clicked && !LookupEngine.TriggerSatisfied(e))
        {
            return;
        }

        if (_child is { IsVisible: true, IsLocked: true })
        {
            return;
        }

        string? text = block.Text;
        int charIndex = LookupEngine.GetCharIndexAtPointer(block, e);
        if (text is null || charIndex < 0)
        {
            _child?.HideAll();
            return;
        }

        LookupResult[]? results = LookupEngine.Lookup(text, charIndex, out string? lookedUpText);
        if (results is null)
        {
            _child?.HideAll();
            return;
        }

        _child ??= new PopupWindow(_level + 1);
        if (_child.IsVisible && _child.LastLookedUpText == lookedUpText && _child._sourceText == text && _child._charPosition == charIndex)
        {
            return;
        }

        PixelPoint anchor = block.PointToScreen(e.GetPosition(block));
        _child.ShowResults(results, text, charIndex, anchor, lookedUpText);

        if (UiConfig.Instance.HighlightLongestMatch)
        {
            block.SelectionStart = charIndex;
            block.SelectionEnd = Math.Min(text.Length, charIndex + results[0].MatchedText.Length);
        }

        if (clicked)
        {
            _child.Lock();
        }
    }

    private async Task MineAsync(int index, Button? button)
    {
        LookupResult[] results = _results;
        if (index >= results.Length)
        {
            return;
        }

        LookupResult result = results[index];
        string? selectedDefinitions = null;
        if (_definitionBlocks.TryGetValue(index, out SelectableTextBlock? definitionsBlock))
        {
            string? selected = definitionsBlock.SelectedText;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                selectedDefinitions = selected;
            }
        }

        string sourceText = _sourceText;
        int charPosition = _charPosition;
        string? formattedDefinitions = result.FormattedDefinitions;
        string spelling = result.PrimarySpelling;
        bool mineToFile = UiConfig.Instance.MineToFileInsteadOfAnki;

        if (button is not null)
        {
            button.IsEnabled = false;
        }

        try
        {
            await Task.Run(() => mineToFile
                ? MiningUtils.MineToFile(results, index, sourceText, formattedDefinitions, selectedDefinitions, charPosition, spelling)
                : AnkiConnectUtils.Mine(results, index, sourceText, formattedDefinitions, selectedDefinitions, charPosition, spelling)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LoggerManager.Logger.Error(ex, "Mining failed");
            FrontendManager.Frontend.Notify(NotificationLevel.Error, $"Mining failed: {ex.Message}");
        }
        finally
        {
            if (button is not null)
            {
                button.IsEnabled = true;
            }
        }
    }

    private static TextBlock MakeText(string text, double fontSize, string color, Thickness margin, VerticalAlignment verticalAlignment = VerticalAlignment.Center)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = Palette.Brush(color),
            Margin = margin,
            VerticalAlignment = verticalAlignment,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static SelectableTextBlock MakeSelectable(string text, double fontSize, string color)
    {
        return new SelectableTextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = Palette.Brush(color),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
            SelectionBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x33, 0x99, 0xFF))
        };
    }

    private static Button MakeButton(string text, string tooltip)
    {
        Button button = new()
        {
            Content = text,
            FontSize = 12,
            Padding = new Thickness(6, 1, 6, 1),
            MinHeight = 0,
            Focusable = false
        };
        ToolTip.SetTip(button, tooltip);
        return button;
    }
}
