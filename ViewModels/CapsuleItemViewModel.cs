using DesktopCapsules.Models;
using DesktopCapsules.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace DesktopCapsules.ViewModels;

public sealed class CapsuleItemViewModel : INotifyPropertyChanged
{
    private readonly IconCache _iconCache;
    private Brush _selectionBorderBrush = Brushes.Transparent;
    private ImageSource _icon;
    private int _iconLoadVersion;
    private bool _isSelected;

    public CapsuleItemViewModel(CapsuleItem model, IconCache iconCache)
    {
        Model = model;
        _iconCache = iconCache;
        _icon = iconCache.FallbackIcon;
        _ = LoadIconAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CapsuleItem Model { get; }

    public string DisplayName => Model.DisplayName;

    public string Path => Model.Path;

    public string SourcePath => Model.SourcePath;

    public bool IsManagedShortcut => Model.IsManagedShortcut;

    public ImageSource Icon => _icon;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionBorderBrush));
        }
    }

    public Brush SelectionBorderBrush => IsSelected ? _selectionBorderBrush : Brushes.Transparent;

    public void SetSelectionBorderBrush(Brush brush)
    {
        _selectionBorderBrush = brush;
        OnPropertyChanged(nameof(SelectionBorderBrush));
    }

    public void Refresh()
    {
        _icon = _iconCache.FallbackIcon;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(SourcePath));
        OnPropertyChanged(nameof(IsManagedShortcut));
        OnPropertyChanged(nameof(Icon));
        _ = LoadIconAsync();
    }

    private async Task LoadIconAsync()
    {
        try
        {
            var version = Interlocked.Increment(ref _iconLoadVersion);
            var path = Path;
            var icon = await _iconCache.GetIconAsync(path).ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (version != _iconLoadVersion)
                {
                    return;
                }

                _icon = icon;
                OnPropertyChanged(nameof(Icon));
            });
        }
        catch
        {
            // Keep the fallback icon if shell extraction fails or the app is shutting down.
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
