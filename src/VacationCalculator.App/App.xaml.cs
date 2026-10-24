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

        // "--data <path>" uses that data file (and the preferences file next to it) instead of the default.
        var dataPath = DataStore.DefaultPath;
        var dataArg = Array.IndexOf(e.Args, "--data");
        if (dataArg >= 0)
        {
            var resolved = dataArg + 1 < e.Args.Length ? DataPathResolver.Resolve(e.Args[dataArg + 1]) : null;
            if (resolved is null)
            {
                MessageBox.Show(
                    "The --data option needs the path to a data file, for example:\n\n--data \"C:\\Temp\\vacation-data.json\"\n\nThe path is missing or not valid.",
                    "Vacation Calculator",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(2);
                return;
            }
            dataPath = resolved;
        }

        _theme = new ThemeService(ThemeService.PreferencesPathFor(dataPath));
        _theme.Apply();

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
                $"Could not read {store.Path}:\n\n{ex.Message}\n\nFix or move the file and start the app again.",
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
