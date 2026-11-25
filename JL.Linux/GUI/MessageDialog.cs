using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace JL.Linux.GUI;

internal sealed class MessageDialog : Window
{
    private bool _result;

    private MessageDialog(string caption, string text, string yesText, string? noText)
    {
        Title = caption;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 560;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;

        Button yes = new() { Content = yesText, MinWidth = 80, IsDefault = true };
        yes.Click += (_, _) =>
        {
            _result = true;
            Close();
        };

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttons.Children.Add(yes);

        if (noText is not null)
        {
            Button no = new() { Content = noText, MinWidth = 80, IsCancel = true };
            no.Click += (_, _) =>
            {
                _result = false;
                Close();
            };
            buttons.Children.Add(no);
        }

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 },
                buttons
            }
        };
    }

    public static Task<bool> ShowYesNo(Window? owner, string caption, string text)
    {
        return ShowCore(owner, new MessageDialog(caption, text, "Yes", "No"));
    }

    public static Task ShowInfo(Window? owner, string caption, string text)
    {
        return ShowCore(owner, new MessageDialog(caption, text, "OK", null));
    }

    private static Task<bool> ShowCore(Window? owner, MessageDialog dialog)
    {
        TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Closed += (_, _) => tcs.TrySetResult(dialog._result);

        if (owner is { IsVisible: true })
        {
            _ = dialog.ShowDialog(owner);
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Show();
        }

        return tcs.Task;
    }
}
