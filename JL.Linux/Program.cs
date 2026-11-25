using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using JL.Core.Utilities;
using JL.Linux.Config;

namespace JL.Linux;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // JL.Core resolves Resources/, Config/ and Profiles/ relative to the app folder.
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        string instanceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppContext.BaseDirectory)))[..16];
        using Mutex singleInstanceMutex = new(true, $"JL.Linux.{instanceKey}", out bool createdNew);
        if (!createdNew)
        {
            Console.Error.WriteLine("JL is already running from this folder.");
            return 1;
        }

        AppDomain.CurrentDomain.UnhandledException += static (_, e) =>
            LoggerManager.Logger.Fatal((Exception)e.ExceptionObject, "Unhandled exception");

        TaskScheduler.UnobservedTaskException += static (_, e) =>
        {
            LoggerManager.Logger.Fatal(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        WindowsConfigMigration.Run();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
    }

    // Also used by the Avalonia previewer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
