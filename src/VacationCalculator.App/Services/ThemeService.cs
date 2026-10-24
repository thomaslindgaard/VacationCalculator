using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace VacationCalculator.App.Services;

/// <summary>The user's theme choice. <see cref="System"/> follows the Windows app theme.</summary>
public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Holds the Light/Dark/System choice, swaps the palette dictionary in the application resources,
/// keeps window title bars in step and remembers the choice in a small preferences file.
/// The preferences file is separate from the vacation data and failures here never touch that data.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const string PreferencesFileName = "vacation-calculator-preferences.json";

    private readonly string? _preferencesPath;
    private readonly Func<bool> _systemPrefersDark;
    private readonly List<Window> _windows = [];
    private ThemeMode _mode;
    private bool _disposed;

    /// <param name="preferencesPath">Where to remember the choice; null keeps it in memory only.</param>
    /// <param name="systemPrefersDark">Whether Windows prefers dark apps; defaults to reading the registry.</param>
    public ThemeService(string? preferencesPath, Func<bool>? systemPrefersDark = null)
    {
        _preferencesPath = preferencesPath;
        _systemPrefersDark = systemPrefersDark ?? WindowsPrefersDark;
        _mode = preferencesPath is null ? ThemeMode.System : LoadMode(preferencesPath);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// The preferences file that sits next to the given data file, or null if that would be the data file itself
    /// (preferences must never be read from or written over the vacation data).
    /// </summary>
    public static string? PreferencesPathFor(string dataPath)
    {
        var fullData = Path.GetFullPath(dataPath);
        var path = Path.Combine(Path.GetDirectoryName(fullData) ?? "", PreferencesFileName);
        return string.Equals(path, fullData, StringComparison.OrdinalIgnoreCase) ? null : path;
    }

    public event EventHandler? Changed;

    public ThemeMode Mode
    {
        get => _mode;
        set
        {
            if (value == _mode)
                return;

            _mode = value;
            SaveMode();
            Apply();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether the dark palette is in use right now (resolves System against the Windows setting).</summary>
    public bool IsDark => _mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => _systemPrefersDark(),
    };

    /// <summary>Applies the current palette to the application and to every attached window.</summary>
    public void Apply()
    {
        var app = Application.Current;
        if (app is null)
            return;

        var source = new Uri(IsDark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", UriKind.Relative);
        var merged = app.Resources.MergedDictionaries;
        var existing = merged.FirstOrDefault(d => d.Source is { } s && s.OriginalString.EndsWith("Theme.xaml", StringComparison.Ordinal));
        var palette = new ResourceDictionary { Source = source };
        if (existing is null)
            merged.Insert(0, palette);
        else
            merged[merged.IndexOf(existing)] = palette;

        foreach (var window in _windows)
            ApplyTitleBar(window);
    }

    /// <summary>Makes the window's title bar follow the theme, now and whenever the theme changes.</summary>
    public void Attach(Window window)
    {
        _windows.Add(window);
        window.SourceInitialized += (_, _) => ApplyTitleBar(window);
        window.Closed += (_, _) => _windows.Remove(window);
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            ApplyTitleBar(window);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Raised on a background thread; only System mode cares, and only about General (AppsUseLightTheme).
        if (e.Category != UserPreferenceCategory.General || _mode != ThemeMode.System || Application.Current is not { } app)
            return;

        app.Dispatcher.BeginInvoke(() =>
        {
            Apply();
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private static bool WindowsPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ThemeMode LoadMode(string path)
    {
        // A missing or unreadable file means System.
        try
        {
            if (!File.Exists(path))
                return ThemeMode.System;

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("theme", out var theme)
                && theme.ValueKind == JsonValueKind.String
                && Enum.TryParse<ThemeMode>(theme.GetString(), ignoreCase: true, out var mode)
                && Enum.IsDefined(mode))
                return mode;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
        return ThemeMode.System;
    }

    private void SaveMode()
    {
        if (_preferencesPath is null)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_preferencesPath)!);
            File.WriteAllText(_preferencesPath, JsonSerializer.Serialize(new { theme = _mode.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the choice is not worth interrupting the user.
        }
    }

    private void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var dark = IsDark ? 1 : 0;
        // Attribute 20 on Windows 10 2004 and later; 19 on earlier Windows 10 builds.
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));

        // Windows 11 can colour the caption; match the sidebar. Silently ignored elsewhere.
        if (Application.Current?.Resources["SidebarBrush"] is SolidColorBrush back
            && Application.Current.Resources["TextBrush"] is SolidColorBrush text)
        {
            var caption = ToColorRef(back.Color);
            var captionText = ToColorRef(text.Color);
            DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(handle, 36, ref captionText, sizeof(int));
        }
    }

    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

