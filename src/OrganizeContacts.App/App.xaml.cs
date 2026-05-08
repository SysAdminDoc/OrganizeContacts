using System.IO;
using System.Windows;
using OrganizeContacts.Core;

namespace OrganizeContacts.App;

public partial class App : Application
{
    /// <summary>Swap the active theme resource dictionary.</summary>
    public static void ApplyTheme(string theme)
    {
        var path = string.Equals(theme, "Latte", System.StringComparison.OrdinalIgnoreCase)
            ? "Themes/CatppuccinLatte.xaml"
            : "Themes/CatppuccinMocha.xaml";
        var rd = new ResourceDictionary { Source = new System.Uri(path, System.UriKind.Relative) };
        var dictionaries = Current.Resources.MergedDictionaries;
        if (dictionaries.Count > 0) dictionaries[0] = rd;
        else dictionaries.Add(rd);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataDir = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "OrganizeContacts");
        var settings = AppSettings.LoadOrDefault(Path.Combine(dataDir, "settings.json"));
        ApplyTheme(settings.Theme);
    }
}
