using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DesktopCapsules.Services;

public sealed class TrayController : IDisposable
{
    private readonly Drawing.Icon _icon;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _startWithWindowsItem;

    public TrayController(Action createCapsule, Action showCapsules, Action openDataFolder, Action exit)
    {
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("New capsule", null, (_, _) => createCapsule());
        _menu.Items.Add("Show capsules", null, (_, _) => showCapsules());
        _menu.Items.Add("Open data folder", null, (_, _) => openDataFolder());
        _startWithWindowsItem = new Forms.ToolStripMenuItem("Start with Windows")
        {
            Checked = StartupService.IsEnabled(),
            Enabled = StartupService.CanConfigure()
        };
        _startWithWindowsItem.Click += (_, _) => ToggleStartWithWindows();
        _menu.Items.Add(_startWithWindowsItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = LoadApplicationIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Desktop Capsules",
            ContextMenuStrip = _menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => showCapsules();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }

    private static Drawing.Icon LoadApplicationIcon()
    {
        var trayIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DesktopCapsules-tray.ico");
        if (File.Exists(trayIconPath))
        {
            using var trayIcon = new Drawing.Icon(trayIconPath);
            return (Drawing.Icon)trayIcon.Clone();
        }

        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
        {
            using var icon = Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon is not null)
            {
                return (Drawing.Icon)icon.Clone();
            }
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    private void ToggleStartWithWindows()
    {
        try
        {
            var shouldEnable = !StartupService.IsEnabled();
            StartupService.SetEnabled(shouldEnable);
            _startWithWindowsItem.Checked = StartupService.IsEnabled();
        }
        catch (Exception exception)
        {
            _startWithWindowsItem.Checked = StartupService.IsEnabled();
            Forms.MessageBox.Show(
                exception.Message,
                "Could not update startup setting",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Warning);
        }
    }
}
