using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // WPF bindings format dates and numbers as en-US unless told otherwise; use the user's regional settings.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        var store = new DataStore(DataStore.DefaultPath);
        MainViewModel viewModel;
        try
        {
            viewModel = new MainViewModel(store);
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

        new MainWindow { DataContext = viewModel }.Show();
    }
}
