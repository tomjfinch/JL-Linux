using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using JL.Core.Config;
using JL.Core.Statistics;

namespace JL.Linux.GUI;

internal sealed class StatsWindow : Window
{
    public StatsWindow()
    {
        Title = "JL Statistics";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        StatsUtils.UpdateTimeStats(restartStopWatch: true);

        StackPanel columns = new() { Orientation = Orientation.Horizontal, Spacing = 28, Margin = new Thickness(20) };
        columns.Children.Add(Column("This session", StatsUtils.SessionStats));
        columns.Children.Add(Column($"Profile ({ProfileUtils.CurrentProfileName})", StatsUtils.ProfileLifetimeStats));
        columns.Children.Add(Column("All time", StatsUtils.LifetimeStats));
        Content = columns;
    }

    private static Control Column(string title, Stats stats)
    {
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                new SelectableTextBlock { Text = stats.ToString().Replace("???", "Imoutos", StringComparison.Ordinal) }
            }
        };
    }
}
