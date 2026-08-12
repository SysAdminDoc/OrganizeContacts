using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using OrganizeContacts.Core;
using OrganizeContacts.Core.Diagnostics;

namespace OrganizeContacts.App;

public partial class App : Application
{
    private static string s_preferredTheme = "Mocha";

    public static LocalDiagnosticLog? Diagnostics { get; private set; }

    /// <summary>Remember the user's palette and apply it unless Windows High Contrast is active.</summary>
    public static void ApplyTheme(string theme)
    {
        s_preferredTheme = string.Equals(theme, "HighContrast", StringComparison.OrdinalIgnoreCase)
            ? "HighContrast"
            : string.Equals(theme, "Latte", StringComparison.OrdinalIgnoreCase)
                ? "Latte"
                : "Mocha";
        ApplyEffectiveTheme();
    }

    private static void ApplyEffectiveTheme()
    {
        var path = SystemParameters.HighContrast || s_preferredTheme == "HighContrast"
            ? "Themes/HighContrast.xaml"
            : string.Equals(s_preferredTheme, "Latte", StringComparison.OrdinalIgnoreCase)
                ? "Themes/CatppuccinLatte.xaml"
                : "Themes/CatppuccinMocha.xaml";
        var rd = new ResourceDictionary { Source = new System.Uri(path, System.UriKind.Relative) };
        var dictionaries = Current.Resources.MergedDictionaries;
        for (int i = 0; i < dictionaries.Count; i++)
        {
            var existing = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (existing.IndexOf("themes/catppuccin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                existing.IndexOf("themes/highcontrast", StringComparison.OrdinalIgnoreCase) >= 0)
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
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;

        var settings = AppSettings.LoadOrDefault(Path.Combine(dataDir, "settings.json"));
        ApplyTheme(settings.Theme);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        base.OnExit(e);
    }

    private static void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != nameof(SystemParameters.HighContrast))
            return;

        if (Current.Dispatcher.CheckAccess())
            ApplyEffectiveTheme();
        else
            _ = Current.Dispatcher.InvokeAsync(ApplyEffectiveTheme);
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
