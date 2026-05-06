using DesktopCapsules.Models;
using DesktopCapsules.Services;
using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace DesktopCapsules;

public partial class App : System.Windows.Application
{
    private readonly List<CapsuleWindow> _windows = [];
    private CapsuleConfig _config = new();
    private CapsuleStore _store = null!;
    private CapsuleStorage _storage = null!;
    private TrayController? _tray;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    internal IconCache Icons { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "Local\\DesktopCapsules.SingleInstance", out _ownsSingleInstanceMutex);
        if (!_ownsSingleInstanceMutex)
        {
            MessageBox.Show(
                "Desktop Capsules is already running.",
                "Desktop Capsules",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _store = new CapsuleStore();
        _storage = new CapsuleStorage(_store);
        _config = _store.Load();

        if (_config.Capsules.Count == 0)
        {
            _config.Capsules.Add(CreateDefaultCapsuleState("Apps", 0));
            SaveConfig();
        }

        var normalizedItems = false;
        foreach (var capsule in _config.Capsules)
        {
            normalizedItems |= _storage.NormalizeImportedItems(capsule);
        }

        if (normalizedItems)
        {
            SaveConfig();
        }

        foreach (var capsule in _config.Capsules)
        {
            ShowCapsule(capsule);
        }

        _tray = new TrayController(CreateCapsule, ShowAllCapsules, OpenDataFolder, ExitApp);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        Icons.Dispose();

        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private CapsuleState CreateDefaultCapsuleState(string title, int offset)
    {
        var workArea = SystemParameters.WorkArea;
        return new CapsuleState
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Left = workArea.Left + 36 + offset * 28,
            Top = workArea.Top + 36 + offset * 28,
            Width = 260,
            Height = 184
        };
    }

    private void CreateCapsule()
    {
        var capsule = CreateDefaultCapsuleState($"Capsule {_config.Capsules.Count + 1}", _config.Capsules.Count);
        _config.Capsules.Add(capsule);
        SaveConfig();
        ShowCapsule(capsule);
    }

    private void ShowAllCapsules()
    {
        foreach (var window in _windows.ToArray())
        {
            window.Reveal();
        }
    }

    private void ShowCapsule(CapsuleState state)
    {
        var window = new CapsuleWindow(state, _storage, Icons, SaveConfig);
        window.DeleteRequested += (_, _) => DeleteCapsule(window, state);
        window.Closed += (_, _) => _windows.Remove(window);
        _windows.Add(window);
        window.Show();
    }

    private void DeleteCapsule(CapsuleWindow window, CapsuleState state)
    {
        var result = MessageBox.Show(
            $"Delete '{state.Title}' from the desktop?\n\nManaged shortcuts stay in the app data folder unless you restore them first.",
            "Delete Capsule",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _config.Capsules.Remove(state);
        SaveConfig();
        window.Close();
    }

    private void SaveConfig()
    {
        _store.Save(_config);
    }

    private void OpenDataFolder()
    {
        Process.Start(new ProcessStartInfo(_store.RootPath)
        {
            UseShellExecute = true
        });
    }

    private void ExitApp()
    {
        SaveConfig();
        Shutdown();
    }
}
