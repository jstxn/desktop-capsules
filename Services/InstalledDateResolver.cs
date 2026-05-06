using Microsoft.Win32;
using System.Globalization;

namespace DesktopCapsules.Services;

public static class InstalledDateResolver
{
    private static readonly Lazy<IReadOnlyList<InstalledApplication>> InstalledApplications = new(LoadInstalledApplications);

    public static bool TryGetInstalledDateUtc(string path, out DateTime installedDateUtc)
    {
        installedDateUtc = DateTime.MinValue;

        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        foreach (var application in InstalledApplications.Value)
        {
            if (IsSamePath(application.DisplayIconPath, normalizedPath) ||
                IsChildPath(application.InstallLocation, normalizedPath))
            {
                installedDateUtc = application.InstalledDateUtc;
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<InstalledApplication> LoadInstalledApplications()
    {
        var applications = new List<InstalledApplication>();
        var hives = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
        var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };

        foreach (var hive in hives)
        {
            foreach (var view in views)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstallKey is null)
                    {
                        continue;
                    }

                    foreach (var subkeyName in uninstallKey.GetSubKeyNames())
                    {
                        using var subkey = uninstallKey.OpenSubKey(subkeyName);
                        if (subkey is null)
                        {
                            continue;
                        }

                        var installLocation = NormalizePath(subkey.GetValue("InstallLocation") as string);
                        var displayIconPath = NormalizePath(ParseDisplayIconPath(subkey.GetValue("DisplayIcon") as string));
                        var installedDateUtc = ParseInstallDateUtc(subkey.GetValue("InstallDate") as string);

                        if (installedDateUtc == DateTime.MinValue &&
                            !string.IsNullOrWhiteSpace(installLocation) &&
                            Directory.Exists(installLocation))
                        {
                            installedDateUtc = Directory.GetCreationTimeUtc(installLocation);
                        }

                        if (installedDateUtc != DateTime.MinValue &&
                            (!string.IsNullOrWhiteSpace(installLocation) || !string.IsNullOrWhiteSpace(displayIconPath)))
                        {
                            applications.Add(new InstalledApplication(installLocation, displayIconPath, installedDateUtc));
                        }
                    }
                }
                catch
                {
                    // Registry access varies by hive/view; unavailable views are simply skipped.
                }
            }
        }

        return applications;
    }

    private static DateTime ParseInstallDateUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.MinValue;
        }

        return DateTime.TryParseExact(
            value.Trim(),
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var localDate)
            ? localDate.ToUniversalTime()
            : DateTime.MinValue;
    }

    private static string ParseDisplayIconPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        value = Environment.ExpandEnvironmentVariables(value.Trim());
        if (value.StartsWith('"'))
        {
            var endQuoteIndex = value.IndexOf('"', 1);
            return endQuoteIndex > 1 ? value[1..endQuoteIndex] : value.Trim('"');
        }

        var commaIndex = value.LastIndexOf(',');
        if (commaIndex > 0 && int.TryParse(value[(commaIndex + 1)..], out _))
        {
            value = value[..commaIndex];
        }

        return value.Trim().Trim('"');
    }

    private static bool IsSamePath(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChildPath(string parent, string child)
    {
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(child))
        {
            return false;
        }

        return string.Equals(parent, child, StringComparison.OrdinalIgnoreCase) ||
               child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')))
                .TrimEnd(Path.DirectorySeparatorChar);
        }
        catch
        {
            return "";
        }
    }

    private sealed record InstalledApplication(string InstallLocation, string DisplayIconPath, DateTime InstalledDateUtc);
}
