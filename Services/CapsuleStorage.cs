using DesktopCapsules.Models;

namespace DesktopCapsules.Services;

public sealed class CapsuleStorage
{
    private readonly CapsuleStore _store;
    private readonly string _commonDesktop;
    private readonly string _userDesktop;

    public CapsuleStorage(CapsuleStore store)
    {
        _store = store;
        _userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    }

    public CapsuleItem Import(string capsuleId, string droppedPath)
    {
        if (!File.Exists(droppedPath) && !Directory.Exists(droppedPath))
        {
            throw new FileNotFoundException("Dropped item no longer exists.", droppedPath);
        }

        var groupPath = _store.GetGroupPath(capsuleId);
        var isShortcut = IsManagedShortcutFile(droppedPath);
        var displayName = Path.GetFileNameWithoutExtension(droppedPath);

        if (isShortcut)
        {
            var destination = GetUniquePath(groupPath, Path.GetFileName(droppedPath));

            if (IsDesktopPath(droppedPath))
            {
                File.Move(droppedPath, destination);
            }
            else
            {
                CopyPreserveTimestamps(droppedPath, destination);
            }

            return new CapsuleItem
            {
                DisplayName = displayName,
                Path = destination,
                SourcePath = droppedPath,
                IsManagedShortcut = true
            };
        }

        var shortcutPath = GetUniquePath(groupPath, displayName + ".lnk");
        ShellLinkService.CreateShortcut(shortcutPath, droppedPath, $"DesktopCapsules shortcut to {droppedPath}");

        return new CapsuleItem
        {
            DisplayName = displayName,
            Path = shortcutPath,
            SourcePath = droppedPath,
            IsManagedShortcut = true
        };
    }

    public bool NormalizeImportedItems(CapsuleState capsule)
    {
        var changed = false;

        foreach (var item in capsule.Items)
        {
            if (!ShouldNormalizeUrlWrapper(item))
            {
                continue;
            }

            try
            {
                var groupPath = _store.GetGroupPath(capsule.Id);
                var destination = GetUniquePath(groupPath, Path.GetFileName(item.SourcePath));

                if (IsDesktopPath(item.SourcePath))
                {
                    File.Move(item.SourcePath, destination);
                }
                else
                {
                    CopyPreserveTimestamps(item.SourcePath, destination);
                }

                if (File.Exists(item.Path))
                {
                    Directory.CreateDirectory(_store.RemovedPath);
                    File.Move(item.Path, GetUniquePath(_store.RemovedPath, Path.GetFileName(item.Path)));
                }

                item.Path = destination;
                item.IsManagedShortcut = true;
                changed = true;
            }
            catch
            {
                // Keep the user's existing shortcut if migration cannot be completed.
            }
        }

        return changed;
    }

    public string RestoreToDesktop(CapsuleItem item)
    {
        if (!item.IsManagedShortcut || !File.Exists(item.Path))
        {
            throw new InvalidOperationException("Only managed shortcut files can be restored to the desktop.");
        }

        var destination = GetUniquePath(_userDesktop, Path.GetFileName(item.Path));
        File.Move(item.Path, destination);
        return destination;
    }

    public string? ParkManagedShortcut(CapsuleItem item)
    {
        if (!item.IsManagedShortcut || !File.Exists(item.Path))
        {
            return null;
        }

        Directory.CreateDirectory(_store.RemovedPath);
        var destination = GetUniquePath(_store.RemovedPath, Path.GetFileName(item.Path));
        File.Move(item.Path, destination);
        return destination;
    }

    public void UndoImport(CapsuleItem item)
    {
        if (!item.IsManagedShortcut || !File.Exists(item.Path))
        {
            return;
        }

        if (IsDesktopPath(item.SourcePath) &&
            !File.Exists(item.SourcePath) &&
            !Directory.Exists(item.SourcePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(item.SourcePath)!);
            File.Move(item.Path, item.SourcePath);
            return;
        }

        Directory.CreateDirectory(_store.RemovedPath);
        File.Move(item.Path, GetUniquePath(_store.RemovedPath, Path.GetFileName(item.Path)));
    }

    public static bool TryMoveFileBack(string sourcePath, string destinationPath)
    {
        try
        {
            if (!File.Exists(sourcePath) || File.Exists(destinationPath) || Directory.Exists(destinationPath))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Move(sourcePath, destinationPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsDesktopPath(string path)
    {
        return IsChildPath(_userDesktop, path) || IsChildPath(_commonDesktop, path);
    }

    private static bool ShouldNormalizeUrlWrapper(CapsuleItem item)
    {
        return item.IsManagedShortcut &&
               File.Exists(item.Path) &&
               File.Exists(item.SourcePath) &&
               string.Equals(Path.GetExtension(item.Path), ".lnk", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Path.GetExtension(item.SourcePath), ".url", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsManagedShortcutFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChildPath(string parent, string child)
    {
        if (string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        var parentFull = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var childFull = Path.GetFullPath(child);
        return childFull.StartsWith(parentFull, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUniquePath(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);

        var candidate = Path.Combine(directory, SanitizeFileName(fileName));
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var index = 2; ; index++)
        {
            candidate = Path.Combine(directory, $"{SanitizeFileName(name)} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '-');
        }

        return string.IsNullOrWhiteSpace(fileName) ? "Shortcut.lnk" : fileName;
    }

    private static void CopyPreserveTimestamps(string sourcePath, string destinationPath)
    {
        File.Copy(sourcePath, destinationPath);
        File.SetCreationTimeUtc(destinationPath, File.GetCreationTimeUtc(sourcePath));
        File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
        File.SetLastAccessTimeUtc(destinationPath, File.GetLastAccessTimeUtc(sourcePath));
    }
}
