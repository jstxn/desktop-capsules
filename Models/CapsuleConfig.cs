namespace DesktopCapsules.Models;

public sealed class CapsuleConfig
{
    public int Version { get; set; } = 1;

    public List<CapsuleState> Capsules { get; set; } = [];
}
