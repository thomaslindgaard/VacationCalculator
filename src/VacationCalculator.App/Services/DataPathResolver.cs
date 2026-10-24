using System.IO;

namespace VacationCalculator.App.Services;

public static class DataPathResolver
{
    /// <summary>
    /// Validates the value of the --data option and returns the full path, or null if it is not a usable file path
    /// (empty, an existing directory, or containing characters Windows doesn't allow).
    /// </summary>
    public static string? Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // A colon is only allowed right after a drive letter at the start (also rules out alternate data streams).
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == ':' && !(i == 1 && char.IsAsciiLetter(value[0])))
                return null;
        }

        if (value.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || value.IndexOfAny(['*', '?']) >= 0)
            return null;

        try
        {
            var full = Path.GetFullPath(value);
            var fileName = Path.GetFileName(full);
            if (fileName.Length == 0 || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return null;

            var directory = Path.GetDirectoryName(full);
            if (directory is not null && directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return null;

            return Directory.Exists(full) ? null : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
