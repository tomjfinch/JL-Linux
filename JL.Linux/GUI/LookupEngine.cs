using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using JL.Core.Config;
using JL.Core.Dicts;
using JL.Core.Japanese;
using JL.Core.Lookup;
using JL.Core.Statistics;
using JL.Core.Utilities;
using JL.Linux.Config;

namespace JL.Linux.GUI;

/// <summary>
/// The bits of Windows JL's PopupWindow.LookupOnMouseMove that are not WPF-specific:
/// map a pointer position to a character, cut the search span, and ask JL.Core.
/// </summary>
internal static class LookupEngine
{
    /// <summary>Character index under the pointer, or -1 if the pointer isn't over text.</summary>
    public static int GetCharIndexAtPointer(TextBlock textBlock, PointerEventArgs e)
    {
        string? text = textBlock.Text;
        if (string.IsNullOrEmpty(text))
        {
            return -1;
        }

        Point p = e.GetPosition(textBlock);
        Thickness padding = textBlock.Padding;
        Point layoutPoint = new(p.X - padding.Left, p.Y - padding.Top);

        TextLayout layout = textBlock.TextLayout;
        if (layoutPoint.X < 0 || layoutPoint.Y < 0)
        {
            return -1;
        }

        TextHitTestResult hit = layout.HitTestPoint(layoutPoint);
        if (!hit.IsInside)
        {
            return -1;
        }

        int index = hit.TextPosition;
        if (index < 0 || index >= text.Length)
        {
            return -1;
        }

        // Land on the start of a surrogate pair, never in the middle of it.
        if (char.IsLowSurrogate(text[index]) && index > 0 && char.IsHighSurrogate(text[index - 1]))
        {
            --index;
        }

        return index;
    }

    public static LookupResult[]? Lookup(string text, int charPosition, out string? lookedUpText)
    {
        lookedUpText = null;
        if (charPosition < 0 || charPosition >= text.Length)
        {
            return null;
        }

        UiConfig config = UiConfig.Instance;
        ReadOnlySpan<char> searchSpan = text;

        int maxSearchLength = config.MaxSearchLength > DictUtils.MaxSearchKeyLength && DictUtils.MaxSearchKeyLength > 0
            ? DictUtils.MaxSearchKeyLength
            : config.MaxSearchLength;

        if (text.Length - charPosition > maxSearchLength)
        {
            int newLength = charPosition + maxSearchLength;
            if (char.IsHighSurrogate(text[newLength - 1]))
            {
                --newLength;
            }

            searchSpan = searchSpan[..newLength];
        }

        int endPosition = JapaneseUtils.FindExpressionBoundary(searchSpan, charPosition);
        if (endPosition <= charPosition)
        {
            return null;
        }

        ReadOnlySpan<char> spanToLookup = searchSpan[charPosition..endPosition];
        if (spanToLookup.IsEmpty || TextUtils.StartsWithWhiteSpace(spanToLookup))
        {
            return null;
        }

        lookedUpText = new string(spanToLookup);
        LookupResult[]? results = LookupUtils.LookupText(lookedUpText);
        if (results is null || results.Length is 0)
        {
            return null;
        }

        StatsUtils.IncrementStat(StatType.NumberOfLookups);
        if (CoreConfigManager.Instance.TrackTermLookupCounts)
        {
            LookupResult first = results[0];
            StatsUtils.IncrementTermLookupCount(first.DeconjugatedMatchedText ?? first.MatchedText);
        }

        return results;
    }

    public static bool TriggerSatisfied(PointerEventArgs e)
    {
        KeyModifiers modifiers = e.KeyModifiers;
        return UiConfig.Instance.LookupTrigger switch
        {
            LookupTrigger.MouseMove => true,
            LookupTrigger.Shift => modifiers.HasFlag(KeyModifiers.Shift),
            LookupTrigger.Control => modifiers.HasFlag(KeyModifiers.Control),
            LookupTrigger.Alt => modifiers.HasFlag(KeyModifiers.Alt),
            LookupTrigger.LeftClick => false,
            _ => true
        };
    }
}
