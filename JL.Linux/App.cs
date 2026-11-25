using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using JL.Linux.GUI;

namespace JL.Linux;

internal sealed class App : Application
{
    public override void Initialize()
    {
        // No XAML: the whole UI is built in code so there is nothing to precompile.
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // MainWindow registers itself as JL.Core's frontend in its constructor,
            // before it loads settings (which may already raise notifications).
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
