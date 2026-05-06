namespace DesktopCapsules.Models;

public sealed class CapsuleState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "Capsule";

    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; } = 260;

    public double Height { get; set; } = 184;

    public bool IsCollapsed { get; set; }

    public double CornerRadius { get; set; } = 18;

    public string BorderColor { get; set; } = "#55FFFFFF";

    public string BackgroundColor { get; set; } = "#182028";

    public double BackgroundOpacity { get; set; } = 0.88;

    public string HeaderColor { get; set; } = "#CC25313B";

    public double HeaderOpacity { get; set; } = 1.0;

    public List<CapsuleItem> Items { get; set; } = [];
}
