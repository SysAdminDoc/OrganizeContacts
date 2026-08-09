using System.IO;
using System.Windows;
using System.Windows.Threading;
using OrganizeContacts.Core;
using OrganizeContacts.Core.Diagnostics;

namespace OrganizeContacts.App;

public partial class App : Application
{
    public static LocalDiagnosticLog? Diagnostics { get; private set; }

    /// <summary>Swap the active theme resource dictionary. Locates the existing theme by
    /// matching on the `Themes/Catppuccin*.xaml` path so the swap is correct even after
    /// another `MergedDictionaries` entry has been inserted ahead of it (e.g. a future
    /// shared-styles dict).</summary>
    public static void ApplyTheme(string theme)
    {
        var path = string.Equals(theme, "Latte", System.StringComparison.OrdinalIgnoreCase)
            ? "Themes/CatppuccinLatte.xaml"
            : "Themes/CatppuccinMocha.xaml";
        var rd = new ResourceDictionary { Source = new System.Uri(path, System.UriKind.Relative) };
        var dictionaries = Current.Resources.MergedDictionaries;
        for (int i = 0; i < dictionaries.Count; i++)
        {
            var existing = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (existing.IndexOf("themes/catppuccin", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                dictionaries[i] = rd;
                return;
            }
        }
        // No existing theme dict found — append rather than overwrite a stranger.
        dictionaries.Add(rd);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataDir = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "OrganizeContacts");
        Diagnostics = new LocalDiagnosticLog(Path.Combine(dataDir, "diagnostics.log"));
        Diagnostics.Information("app.start", $"version={typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown"}");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var settings = AppSettings.LoadOrDefault(Path.Combine(dataDir, "settings.json"));
        ApplyTheme(settings.Theme);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        => Diagnostics?.Error("dispatcher.unhandled", e.Exception);

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            Diagnostics?.Error("appdomain.unhandled", exception);
        else
            Diagnostics?.Information("appdomain.unhandled", e.ExceptionObject?.ToString() ?? "unknown exception");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Diagnostics?.Error("task.unobserved", e.Exception);
        e.SetObserved();
    }
}
