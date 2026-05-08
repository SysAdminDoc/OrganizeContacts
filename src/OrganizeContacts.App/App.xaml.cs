using System.IO;
using System.Windows;
using OrganizeContacts.Core;

namespace OrganizeContacts.App;

public partial class App : Application
{
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
        var settings = AppSettings.LoadOrDefault(Path.Combine(dataDir, "settings.json"));
        ApplyTheme(settings.Theme);
    }
}
