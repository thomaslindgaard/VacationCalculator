using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using VacationCalculator.App.Services;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App;

public partial class App : Application
{
    private ThemeService? _theme;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // WPF bindings format dates and numbers as en-US unless told otherwise; use the user's regional settings.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        // Until the preferences are read, text follows the Windows display language.
        Loc.SetLanguage(Languages.Resolve(LanguageMode.System, CultureInfo.CurrentUICulture));

        // "--data <path>" uses that data file (and the preferences file next to it) instead of the default.
        var dataPath = DataStore.DefaultPath;
        var dataArg = Array.IndexOf(e.Args, "--data");
        if (dataArg >= 0)
        {
            var resolved = dataArg + 1 < e.Args.Length ? DataPathResolver.Resolve(e.Args[dataArg + 1]) : null;
            if (resolved is null)
            {
                MessageBox.Show(
                    Loc.Get("Startup.DataArgMissing"),
                    "Vacation Calculator",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(2);
                return;
            }
            dataPath = resolved;
        }

        // The display language is what the System language choice follows.
        _theme = new ThemeService(ThemeService.PreferencesPathFor(dataPath), uiCulture: () => CultureInfo.CurrentUICulture);
        _theme.Apply();
        Loc.SetLanguage(_theme.Language);

        var store = new DataStore(dataPath);
        MainViewModel viewModel;
        try
        {
            viewModel = new MainViewModel(store, theme: _theme);
        }
        catch (JsonException ex)
        {
            // Don't start with empty data, as the next save would overwrite the user's file.
            MessageBox.Show(
                Loc.Format("Startup.ReadFailed", store.Path, ex.Message),
                "Vacation Calculator",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow { DataContext = viewModel };
        _theme.Attach(window);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _theme?.Dispose();
        base.OnExit(e);
    }
}
