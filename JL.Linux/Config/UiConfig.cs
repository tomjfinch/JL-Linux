using System.Globalization;
using JL.Core.Config;
using Microsoft.Data.Sqlite;

namespace JL.Linux.Config;

internal enum LookupTrigger
{
    MouseMove,
    Shift,
    Control,
    Alt,
    LeftClick
}

/// <summary>
/// Front-end settings for the Linux UI. Stored in JL's own Configs.sqlite next to the
/// core settings. Where a setting means the same thing as in Windows JL, the same
/// name is used, so a copied-over Windows config carries across.
/// </summary>
internal sealed class UiConfig
{
    public static UiConfig Instance { get; private set; } = new();

    // Main window
    public bool AlwaysOnTop { get; set; } = true;
    public double MainWindowWidth { get; set; } = 800;
    public double MainWindowHeight { get; set; } = 200;
    public int MainWindowLeft { get; set; } = int.MinValue;
    public int MainWindowTop { get; set; } = int.MinValue;
    public double MainWindowFontSize { get; set; } = 28;
    public double MainWindowOpacity { get; set; } = 0.75;
    public string MainWindowFont { get; set; } = DefaultFont;
    public string MainWindowTextColor { get; set; } = "#FFFFFFFF";
    public string MainWindowBackgroundColor { get; set; } = "#FF101010";

    // Capture
    public bool OnlyCaptureTextWithJapaneseChars { get; set; } = true;
    public bool DiscardIdenticalText { get; set; } = true;
    public int MaxTextLengthToCapture { get; set; }
    public int MaxBacklogCapacity { get; set; } = 1000;

    // Lookup
    public LookupTrigger LookupTrigger { get; set; } = LookupTrigger.MouseMove;
    public int MaxSearchLength { get; set; } = 41;
    public bool HighlightLongestMatch { get; set; } = true;
    public int MaxNumResultsNotInMiningMode { get; set; } = 7;
    public bool AutoPlayAudio { get; set; }

    // Popup
    public double PopupMaxWidth { get; set; } = 700;
    public double PopupMaxHeight { get; set; } = 520;
    public double PopupXOffset { get; set; } = 10;
    public double PopupYOffset { get; set; } = 20;
    public double PopupOpacity { get; set; } = 0.95;
    public string PopupFont { get; set; } = DefaultFont;
    public double PrimarySpellingFontSize { get; set; } = 24;
    public double ReadingsFontSize { get; set; } = 18;
    public double DefinitionsFontSize { get; set; } = 16;
    public double DictTypeFontSize { get; set; } = 12;
    public string PrimarySpellingColor { get; set; } = "#FFFFA040";
    public string ReadingsColor { get; set; } = "#FFFFD27F";
    public string DefinitionsColor { get; set; } = "#FFFFFFFF";
    public string DeconjugationInfoColor { get; set; } = "#FFA9A9A9";
    public string FrequencyColor { get; set; } = "#FFFF6EB4";
    public string DictTypeColor { get; set; } = "#FF87CEEB";
    public string PopupBackgroundColor { get; set; } = "#FF181818";

    // Mining / audio
    public bool MineToFileInsteadOfAnki { get; set; }
    public string AudioPlayerCommand { get; set; } = "";

    public const string DefaultFont = "Noto Sans CJK JP, Noto Sans JP, Source Han Sans JP, IPAexGothic, IPAGothic, VL Gothic, Takao Gothic, sans-serif";

    private const string P = "Linux";

    public static void Load(SqliteConnection connection, Dictionary<string, string> configs)
    {
        UiConfig c = new();
        using SqliteTransaction transaction = connection.BeginTransaction();

        c.AlwaysOnTop = Get(connection, configs, c.AlwaysOnTop, nameof(AlwaysOnTop));
        c.MainWindowWidth = Get(connection, configs, c.MainWindowWidth, nameof(MainWindowWidth));
        c.MainWindowHeight = Get(connection, configs, c.MainWindowHeight, nameof(MainWindowHeight));
        c.MainWindowLeft = Get(connection, configs, c.MainWindowLeft, P + nameof(MainWindowLeft));
        c.MainWindowTop = Get(connection, configs, c.MainWindowTop, P + nameof(MainWindowTop));
        c.MainWindowFontSize = Get(connection, configs, c.MainWindowFontSize, P + nameof(MainWindowFontSize));
        c.MainWindowOpacity = Get(connection, configs, c.MainWindowOpacity, P + nameof(MainWindowOpacity));
        c.MainWindowFont = GetString(connection, configs, c.MainWindowFont, P + nameof(MainWindowFont));
        c.MainWindowTextColor = GetString(connection, configs, c.MainWindowTextColor, P + nameof(MainWindowTextColor));
        c.MainWindowBackgroundColor = GetString(connection, configs, c.MainWindowBackgroundColor, P + nameof(MainWindowBackgroundColor));

        c.OnlyCaptureTextWithJapaneseChars = Get(connection, configs, c.OnlyCaptureTextWithJapaneseChars, nameof(OnlyCaptureTextWithJapaneseChars));
        c.DiscardIdenticalText = Get(connection, configs, c.DiscardIdenticalText, nameof(DiscardIdenticalText));
        c.MaxTextLengthToCapture = Get(connection, configs, c.MaxTextLengthToCapture, nameof(MaxTextLengthToCapture));
        c.MaxBacklogCapacity = Get(connection, configs, c.MaxBacklogCapacity, P + nameof(MaxBacklogCapacity));

        c.LookupTrigger = ConfigDBManager.GetValueEnumValueFromConfig(connection, configs, c.LookupTrigger, P + nameof(LookupTrigger));
        c.MaxSearchLength = Get(connection, configs, c.MaxSearchLength, nameof(MaxSearchLength));
        c.HighlightLongestMatch = Get(connection, configs, c.HighlightLongestMatch, P + nameof(HighlightLongestMatch));
        c.MaxNumResultsNotInMiningMode = Get(connection, configs, c.MaxNumResultsNotInMiningMode, nameof(MaxNumResultsNotInMiningMode));
        c.AutoPlayAudio = Get(connection, configs, c.AutoPlayAudio, nameof(AutoPlayAudio));

        c.PopupMaxWidth = Get(connection, configs, c.PopupMaxWidth, nameof(PopupMaxWidth));
        c.PopupMaxHeight = Get(connection, configs, c.PopupMaxHeight, nameof(PopupMaxHeight));
        c.PopupXOffset = Get(connection, configs, c.PopupXOffset, nameof(PopupXOffset));
        c.PopupYOffset = Get(connection, configs, c.PopupYOffset, nameof(PopupYOffset));
        c.PopupOpacity = Get(connection, configs, c.PopupOpacity, P + nameof(PopupOpacity));
        c.PopupFont = GetString(connection, configs, c.PopupFont, P + nameof(PopupFont));
        c.PrimarySpellingFontSize = Get(connection, configs, c.PrimarySpellingFontSize, P + nameof(PrimarySpellingFontSize));
        c.ReadingsFontSize = Get(connection, configs, c.ReadingsFontSize, P + nameof(ReadingsFontSize));
        c.DefinitionsFontSize = Get(connection, configs, c.DefinitionsFontSize, P + nameof(DefinitionsFontSize));
        c.DictTypeFontSize = Get(connection, configs, c.DictTypeFontSize, P + nameof(DictTypeFontSize));
        c.PrimarySpellingColor = GetString(connection, configs, c.PrimarySpellingColor, P + nameof(PrimarySpellingColor));
        c.ReadingsColor = GetString(connection, configs, c.ReadingsColor, P + nameof(ReadingsColor));
        c.DefinitionsColor = GetString(connection, configs, c.DefinitionsColor, P + nameof(DefinitionsColor));
        c.DeconjugationInfoColor = GetString(connection, configs, c.DeconjugationInfoColor, P + nameof(DeconjugationInfoColor));
        c.FrequencyColor = GetString(connection, configs, c.FrequencyColor, P + nameof(FrequencyColor));
        c.DictTypeColor = GetString(connection, configs, c.DictTypeColor, P + nameof(DictTypeColor));
        c.PopupBackgroundColor = GetString(connection, configs, c.PopupBackgroundColor, P + nameof(PopupBackgroundColor));

        c.MineToFileInsteadOfAnki = Get(connection, configs, c.MineToFileInsteadOfAnki, nameof(MineToFileInsteadOfAnki));
        c.AudioPlayerCommand = GetString(connection, configs, c.AudioPlayerCommand, P + nameof(AudioPlayerCommand));

        transaction.Commit();
        Instance = c;
    }

    public static string KeyFor(string propertyName)
    {
        return propertyName switch
        {
            nameof(AlwaysOnTop) or nameof(MainWindowWidth) or nameof(MainWindowHeight)
                or nameof(OnlyCaptureTextWithJapaneseChars) or nameof(DiscardIdenticalText) or nameof(MaxTextLengthToCapture)
                or nameof(MaxSearchLength) or nameof(MaxNumResultsNotInMiningMode) or nameof(AutoPlayAudio)
                or nameof(PopupMaxWidth) or nameof(PopupMaxHeight) or nameof(PopupXOffset) or nameof(PopupYOffset)
                or nameof(MineToFileInsteadOfAnki) => propertyName,
            _ => P + propertyName
        };
    }

    /// <summary>Insert-or-update one setting for the current profile.</summary>
    public static void Save(SqliteConnection connection, Dictionary<string, string> configs, string settingName, string value)
    {
        if (configs.ContainsKey(settingName))
        {
            ConfigDBManager.UpdateSetting(connection, settingName, value);
        }
        else
        {
            ConfigDBManager.InsertSetting(connection, settingName, value);
        }

        configs[settingName] = value;
    }

    public static string Format(bool value) => value.ToString();
    public static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
    public static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    public void SaveWindowGeometry(SqliteConnection connection)
    {
        Dictionary<string, string> configs = ConfigDBManager.GetAllConfigs(connection);
        Save(connection, configs, KeyFor(nameof(MainWindowWidth)), Format(MainWindowWidth));
        Save(connection, configs, KeyFor(nameof(MainWindowHeight)), Format(MainWindowHeight));
        Save(connection, configs, KeyFor(nameof(MainWindowLeft)), Format(MainWindowLeft));
        Save(connection, configs, KeyFor(nameof(MainWindowTop)), Format(MainWindowTop));
        Save(connection, configs, KeyFor(nameof(AlwaysOnTop)), Format(AlwaysOnTop));
    }

    private static T Get<T>(SqliteConnection connection, Dictionary<string, string> configs, T defaultValue, string key)
        where T : struct, IConvertible, IParsable<T>
    {
        return ConfigDBManager.GetValueFromConfig(connection, configs, defaultValue, key);
    }

    private static string GetString(SqliteConnection connection, Dictionary<string, string> configs, string defaultValue, string key)
    {
        return ConfigDBManager.GetValueFromConfig(connection, configs, defaultValue, key);
    }
}
