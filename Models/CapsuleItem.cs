namespace DesktopCapsules.Models;

public sealed class CapsuleItem
{
    public string DisplayName { get; set; } = "";

    public string Path { get; set; } = "";

    public string SourcePath { get; set; } = "";

    public bool IsManagedShortcut { get; set; }
}
