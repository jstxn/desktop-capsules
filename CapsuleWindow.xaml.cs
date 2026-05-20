using DesktopCapsules.Interop;
using DesktopCapsules.Models;
using DesktopCapsules.Services;
using DesktopCapsules.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DesktopCapsules;

public partial class CapsuleWindow : Window, INotifyPropertyChanged
{
    private const double DefaultCornerRadius = 18;
    private const string DefaultBorderColor = "#55FFFFFF";
    private const string DefaultBackgroundColor = "#182028";
    private const double DefaultBackgroundOpacity = 0.88;
    private const string DefaultHeaderColor = "#CC25313B";
    private const double DefaultHeaderOpacity = 1.0;
    private const double MinimumAppearanceOpacity = 0.10;
    private const double CollapsedHeight = 40;
    private const double ExpandedMinWidth = 188;
    private const double CollapsedMinWidth = 104;

    private readonly Action _save;
    private readonly CapsuleState _state;
    private readonly CapsuleStorage _storage;
    private Brush _itemSelectionBorderBrush = Brushes.Transparent;
    private bool _isDragging;
    private System.Windows.Point _dragStartScreen;
    private double _dragStartLeft;
    private double _dragStartTop;
    private CapsuleItemViewModel? _selectedItem;
    private CapsuleItemViewModel? _pressedItem;
    private System.Windows.Point _pressedItemPosition;
    private bool _isItemDragInProgress;

    public CapsuleWindow(CapsuleState state, CapsuleStorage storage, IconCache iconCache, Action save)
    {
        _state = state;
        _storage = storage;
        _save = save;

        Items = new ObservableCollection<CapsuleItemViewModel>(
            state.Items.Select(item => new CapsuleItemViewModel(item, iconCache)));
        Items.CollectionChanged += (_, _) => RefreshItemState();

        InitializeComponent();

        DataContext = this;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = state.Left;
        Top = state.Top;
        MinWidth = state.IsCollapsed ? CollapsedMinWidth : ExpandedMinWidth;
        Width = Math.Max(MinWidth, state.Width);
        Height = Math.Max(MinHeight, state.Height);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? DeleteRequested;

    public ObservableCollection<CapsuleItemViewModel> Items { get; }

    public string CapsuleTitle => _state.Title;

    public string ItemCountText => Items.Count == 1 ? "1 item" : $"{Items.Count} items";

    public string CollapseGlyph => _state.IsCollapsed ? "v" : "^";

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyAppearance();
        ApplyCollapsedState();
        ClampToCurrentWorkArea();
        RefreshItemState();
    }

    private void Window_OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        DesktopHost.PrepareAsDesktopToolWindow(hwnd);
    }

    private void Window_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (TitleEditor.Visibility != Visibility.Visible)
        {
            return;
        }

        if (IsDescendantOf(e.OriginalSource as DependencyObject, TitleEditor))
        {
            return;
        }

        CommitTitleEdit();
    }

    private void Window_OnDeactivated(object? sender, EventArgs e)
    {
        if (TitleEditor.Visibility == Visibility.Visible)
        {
            CommitTitleEdit();
        }
    }

    public void Reveal()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleCollapsed();
            e.Handled = true;
            return;
        }

        _isDragging = true;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _dragStartLeft = Left;
        _dragStartTop = Top;
        Header.CaptureMouse();
        e.Handled = true;
    }

    private void Header_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = PointToScreen(e.GetPosition(this));
        var delta = ToDeviceIndependentVector(current - _dragStartScreen);
        Left = _dragStartLeft + delta.X;
        Top = _dragStartTop + delta.Y;
        ClampToCurrentWorkArea();
    }

    private void Header_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        Header.ReleaseMouseCapture();
        SaveWindowBounds();
        e.Handled = true;
    }

    private void RightResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeRight(e.HorizontalChange);
    }

    private void LeftResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeLeft(e.HorizontalChange);
    }

    private void BottomResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_state.IsCollapsed)
        {
            ResizeBottom(e.VerticalChange);
        }
    }

    private void CornerResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeRight(e.HorizontalChange);

        if (!_state.IsCollapsed)
        {
            ResizeBottom(e.VerticalChange);
        }
    }

    private void ResizeThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        SaveWindowBounds();
    }

    private void TitleText_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginTitleEdit();
        e.Handled = true;
    }

    private void TitleEditor_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTitleEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelTitleEdit();
            e.Handled = true;
        }
    }

    private void TitleEditor_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (TitleEditor.Visibility == Visibility.Visible)
        {
            CommitTitleEdit();
        }
    }

    private void OptionsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private void BackgroundColor_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryPickColor(_state.BackgroundColor, out var color))
        {
            _state.BackgroundColor = color;
            ApplyAppearance();
            _save();
        }
    }

    private void DefaultBackgroundColor_OnClick(object sender, RoutedEventArgs e)
    {
        _state.BackgroundColor = DefaultBackgroundColor;
        ApplyAppearance();
        _save();
    }

    private void BorderColor_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryPickColor(_state.BorderColor, out var color))
        {
            _state.BorderColor = color;
            ApplyAppearance();
            _save();
        }
    }

    private void DefaultBorderColor_OnClick(object sender, RoutedEventArgs e)
    {
        _state.BorderColor = DefaultBorderColor;
        ApplyAppearance();
        _save();
    }

    private void HeaderColor_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryPickColor(_state.HeaderColor, out var color))
        {
            _state.HeaderColor = color;
            ApplyAppearance();
            _save();
        }
    }

    private void DefaultHeaderColor_OnClick(object sender, RoutedEventArgs e)
    {
        _state.HeaderColor = DefaultHeaderColor;
        ApplyAppearance();
        _save();
    }

    private void Roundness_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } || !double.TryParse(value, out var radius))
        {
            return;
        }

        _state.CornerRadius = radius;
        ApplyAppearance();
        _save();
    }

    private void BackgroundOpacity_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } || !double.TryParse(value, out var opacity))
        {
            return;
        }

        _state.BackgroundOpacity = Math.Clamp(opacity, MinimumAppearanceOpacity, 1);
        ApplyAppearance();
        _save();
    }

    private void HeaderOpacity_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } || !double.TryParse(value, out var opacity))
        {
            return;
        }

        _state.HeaderOpacity = Math.Clamp(opacity, MinimumAppearanceOpacity, 1);
        ApplyAppearance();
        _save();
    }

    private void SortNameAscending_OnClick(object sender, RoutedEventArgs e)
    {
        SortItems(Items
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase));
    }

    private void SortNameDescending_OnClick(object sender, RoutedEventArgs e)
    {
        SortItems(Items
            .OrderByDescending(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase));
    }

    private void SortFileType_OnClick(object sender, RoutedEventArgs e)
    {
        SortItems(Items
            .OrderBy(item => GetSortExtension(item), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase));
    }

    private void SortDateInstalled_OnClick(object sender, RoutedEventArgs e)
    {
        SortItems(Items
            .OrderByDescending(GetInstalledSortDate)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase));
    }

    private void SortShortcutLocation_OnClick(object sender, RoutedEventArgs e)
    {
        SortItems(Items
            .OrderBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase));
    }

    private void ResetAppearance_OnClick(object sender, RoutedEventArgs e)
    {
        _state.CornerRadius = DefaultCornerRadius;
        _state.BorderColor = DefaultBorderColor;
        _state.BackgroundColor = DefaultBackgroundColor;
        _state.BackgroundOpacity = DefaultBackgroundOpacity;
        _state.HeaderColor = DefaultHeaderColor;
        _state.HeaderOpacity = DefaultHeaderOpacity;
        ApplyAppearance();
        _save();
    }

    private void CollapseButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleCollapsed();
    }

    private void DeleteCapsule_OnClick(object sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Window_OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        ApplyDropEffect(e);
    }

    private void Window_OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        ApplyDropEffect(e);
    }

    private void Window_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!TryGetDroppedFiles(e, out var files))
        {
            return;
        }

        var failures = new List<string>();
        var importedItems = new List<CapsuleItemViewModel>();
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (ContainsPath(file))
                {
                    continue;
                }

                var item = _storage.Import(_state.Id, file);
                var viewModel = new CapsuleItemViewModel(item, ((App)System.Windows.Application.Current).Icons);
                viewModel.SetSelectionBorderBrush(_itemSelectionBorderBrush);
                _state.Items.Add(item);
                Items.Add(viewModel);
                importedItems.Add(viewModel);
            }
            catch (Exception exception)
            {
                failures.Add($"{Path.GetFileName(file)}: {exception.Message}");
            }
        }

        try
        {
            SaveWindowBounds();
        }
        catch (Exception exception)
        {
            foreach (var item in importedItems)
            {
                RemoveItemModel(item);
                TryUndoImport(item.Model);
            }

            failures.Add($"Saving layout failed: {exception.Message}");
        }

        RefreshItemState();

        if (failures.Count > 0)
        {
            MessageBox.Show(
                string.Join(Environment.NewLine, failures),
                "Some items could not be added",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Item_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (GetItemFromSender(sender) is not { } item)
        {
            return;
        }

        if (File.Exists(item.Path))
        {
            _pressedItem = item;
            _pressedItemPosition = e.GetPosition(this);
            e.Handled = true;
        }
    }

    private void Item_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_pressedItem is null || _isItemDragInProgress || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPosition = e.GetPosition(this);
        if (Math.Abs(currentPosition.X - _pressedItemPosition.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - _pressedItemPosition.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        StartItemDrag(_pressedItem);
        e.Handled = true;
    }

    private void Item_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressedItem is { } pressedItem &&
            ReferenceEquals(pressedItem, GetItemFromSender(sender)))
        {
            if (ReferenceEquals(_selectedItem, pressedItem))
            {
                OpenItem(pressedItem);
            }
            else
            {
                SelectItem(pressedItem);
            }

            e.Handled = true;
        }

        ClearPendingItemDrag();
    }

    private void OpenItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetItemFromSender(sender) is { } item)
        {
            OpenItem(item);
        }
    }

    private void OpenLocation_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetItemFromSender(sender) is { } item)
        {
            OpenItemLocation(item);
        }
    }

    private void RestoreItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetItemFromSender(sender) is not { } item)
        {
            return;
        }

        try
        {
            var originalPath = item.Path;
            var restoredPath = _storage.RestoreToDesktop(item.Model);
            RemoveItemAndSaveWithRollback(
                item,
                () => CapsuleStorage.TryMoveFileBack(restoredPath, originalPath),
                "Could not restore shortcut");
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Could not restore shortcut", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoveItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetItemFromSender(sender) is not { } item)
        {
            return;
        }

        try
        {
            var originalPath = item.Path;
            var parkedPath = _storage.ParkManagedShortcut(item.Model);
            RemoveItemAndSaveWithRollback(
                item,
                () => parkedPath is null || CapsuleStorage.TryMoveFileBack(parkedPath, originalPath),
                "Could not remove shortcut");
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Could not remove shortcut", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenItem(CapsuleItemViewModel item)
    {
        var launchPath = GetBestExistingPath(item);
        if (string.IsNullOrWhiteSpace(launchPath))
        {
            MessageBox.Show("The shortcut target no longer exists.", "Missing item", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(launchPath)
        {
            UseShellExecute = true
        });
    }

    private static void OpenItemLocation(CapsuleItemViewModel item)
    {
        var path = GetBestExistingPath(item);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", arguments)
        {
            UseShellExecute = true
        });
    }

    private void RemoveItemModel(CapsuleItemViewModel item)
    {
        if (ReferenceEquals(_selectedItem, item))
        {
            SelectItem(null);
        }

        _state.Items.Remove(item.Model);
        Items.Remove(item);
        RefreshItemState();
    }

    private bool RemoveItemAndSaveWithRollback(CapsuleItemViewModel item, Func<bool> rollbackDiskMove, string errorTitle)
    {
        var modelIndex = _state.Items.IndexOf(item.Model);
        var itemIndex = Items.IndexOf(item);

        RemoveItemModel(item);

        try
        {
            _save();
            return true;
        }
        catch (Exception exception)
        {
            var rolledBack = false;
            try
            {
                rolledBack = rollbackDiskMove();
            }
            catch
            {
                rolledBack = false;
            }

            ReinsertItemModel(item, modelIndex, itemIndex);

            var message = rolledBack
                ? exception.Message
                : $"{exception.Message}{Environment.NewLine}{Environment.NewLine}The shortcut file move could not be rolled back automatically.";
            MessageBox.Show(message, errorTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void ReinsertItemModel(CapsuleItemViewModel item, int modelIndex, int itemIndex)
    {
        if (!_state.Items.Contains(item.Model))
        {
            if (modelIndex >= 0 && modelIndex <= _state.Items.Count)
            {
                _state.Items.Insert(modelIndex, item.Model);
            }
            else
            {
                _state.Items.Add(item.Model);
            }
        }

        if (!Items.Contains(item))
        {
            if (itemIndex >= 0 && itemIndex <= Items.Count)
            {
                Items.Insert(itemIndex, item);
            }
            else
            {
                Items.Add(item);
            }
        }

        item.Refresh();
        RefreshItemState();
    }

    private void SortItems(IEnumerable<CapsuleItemViewModel> sortedItems)
    {
        var orderedItems = sortedItems.ToList();

        Items.Clear();
        _state.Items.Clear();

        foreach (var item in orderedItems)
        {
            Items.Add(item);
            _state.Items.Add(item.Model);
        }

        RefreshItemState();
        _save();
    }

    private void StartItemDrag(CapsuleItemViewModel item)
    {
        ClearPendingItemDrag();

        if (!File.Exists(item.Path))
        {
            return;
        }

        _isItemDragInProgress = true;

        try
        {
            var originalPath = item.Path;
            var desktopSnapshot = GetDesktopShortcutSnapshot();
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { originalPath });
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Move)));

            var effect = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
            var restoredDesktopPath = FindNewDesktopShortcut(originalPath, desktopSnapshot);
            if (restoredDesktopPath is not null || effect == DragDropEffects.Move && !File.Exists(originalPath))
            {
                var parkedPath = restoredDesktopPath is not null && File.Exists(originalPath)
                    ? _storage.ParkManagedShortcut(item.Model)
                    : null;

                RemoveItemAndSaveWithRollback(
                    item,
                    () => RollBackDragOut(originalPath, restoredDesktopPath, parkedPath),
                    "Could not finish moving shortcut out of the capsule");
            }
        }
        finally
        {
            _isItemDragInProgress = false;
        }
    }

    private void ClearPendingItemDrag()
    {
        _pressedItem = null;
    }

    private void SelectItem(CapsuleItemViewModel? item)
    {
        if (ReferenceEquals(_selectedItem, item))
        {
            return;
        }

        if (_selectedItem is not null)
        {
            _selectedItem.IsSelected = false;
        }

        _selectedItem = item;

        if (_selectedItem is not null)
        {
            _selectedItem.IsSelected = true;
        }
    }

    private void TryUndoImport(CapsuleItem item)
    {
        try
        {
            _storage.UndoImport(item);
        }
        catch
        {
            // The user-facing failure already reports the save/import problem.
        }
    }

    private static bool RollBackDragOut(string originalPath, string? restoredDesktopPath, string? parkedPath)
    {
        if (restoredDesktopPath is not null && File.Exists(restoredDesktopPath))
        {
            return CapsuleStorage.TryMoveFileBack(restoredDesktopPath, originalPath);
        }

        if (parkedPath is not null && File.Exists(parkedPath))
        {
            return CapsuleStorage.TryMoveFileBack(parkedPath, originalPath);
        }

        return File.Exists(originalPath);
    }

    private void ToggleCollapsed()
    {
        if (!_state.IsCollapsed)
        {
            SaveWindowBounds();
        }

        _state.IsCollapsed = !_state.IsCollapsed;
        ApplyCollapsedState();
        SaveWindowBounds();
        OnPropertyChanged(nameof(CollapseGlyph));
    }

    private void BeginTitleEdit()
    {
        TitleEditor.Text = _state.Title;
        TitleText.Visibility = Visibility.Collapsed;
        TitleEditor.Visibility = Visibility.Visible;
        TitleEditor.Focus();
        TitleEditor.SelectAll();
    }

    private void CommitTitleEdit()
    {
        var newTitle = TitleEditor.Text.Trim();
        if (string.IsNullOrWhiteSpace(newTitle))
        {
            newTitle = "Capsule";
        }

        if (!string.Equals(_state.Title, newTitle, StringComparison.Ordinal))
        {
            _state.Title = newTitle;
            OnPropertyChanged(nameof(CapsuleTitle));
            Title = newTitle;
            _save();
        }

        EndTitleEdit();
    }

    private void CancelTitleEdit()
    {
        EndTitleEdit();
    }

    private void EndTitleEdit()
    {
        TitleEditor.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
    }

    private void ApplyCollapsedState()
    {
        if (_state.IsCollapsed)
        {
            ContentHost.Visibility = Visibility.Collapsed;
            RightResizeThumb.Visibility = Visibility.Visible;
            LeftResizeThumb.Visibility = Visibility.Visible;
            BottomResizeThumb.Visibility = Visibility.Collapsed;
            CornerResizeThumb.Visibility = Visibility.Collapsed;
            ContentRow.Height = new GridLength(0);
            Panel.SetZIndex(LeftResizeThumb, 4);
            Panel.SetZIndex(RightResizeThumb, 4);
            MinWidth = CollapsedMinWidth;
            Width = Math.Max(CollapsedMinWidth, Width);
            Height = CollapsedHeight;
        }
        else
        {
            ContentHost.Visibility = Visibility.Visible;
            RightResizeThumb.Visibility = Visibility.Visible;
            LeftResizeThumb.Visibility = Visibility.Visible;
            BottomResizeThumb.Visibility = Visibility.Visible;
            CornerResizeThumb.Visibility = Visibility.Visible;
            ContentRow.Height = new GridLength(1, GridUnitType.Star);
            Panel.SetZIndex(LeftResizeThumb, 2);
            Panel.SetZIndex(RightResizeThumb, 2);
            MinWidth = ExpandedMinWidth;
            Width = Math.Max(ExpandedMinWidth, Width);
            Height = Math.Max(112, _state.Height);
        }

        ApplyAppearance();
        OnPropertyChanged(nameof(CollapseGlyph));
    }

    private void ApplyAppearance()
    {
        var radius = Math.Clamp(_state.CornerRadius, 0, 48);
        var backgroundColor = ParseMediaColor(_state.BackgroundColor, Color.FromRgb(24, 32, 40));
        var headerColor = ParseMediaColor(_state.HeaderColor, Color.FromArgb(204, 37, 49, 59));
        var borderColor = ParseMediaColor(_state.BorderColor, Color.FromArgb(85, 255, 255, 255));

        var backgroundBrush = CreateHitTestableBrush(backgroundColor, _state.BackgroundOpacity);
        var headerBrush = CreateHitTestableBrush(headerColor, _state.HeaderOpacity);

        var borderBrush = new SolidColorBrush(borderColor);
        borderBrush.Freeze();

        RootBorder.Background = backgroundBrush;
        RootBorder.BorderBrush = borderBrush;
        RootBorder.CornerRadius = new CornerRadius(radius);
        Header.Background = headerBrush;
        Header.CornerRadius = _state.IsCollapsed
            ? new CornerRadius(radius)
            : new CornerRadius(radius, radius, 0, 0);

        ApplyToolbarButtonAppearance(headerBrush, borderBrush);
    }

    private void ApplyToolbarButtonAppearance(Brush backgroundBrush, Brush borderBrush)
    {
        _itemSelectionBorderBrush = borderBrush;

        var buttons = new[] { OptionsButton, CollapseButton, DeleteButton };
        foreach (var button in buttons)
        {
            button.Background = backgroundBrush;
            button.BorderBrush = borderBrush;
        }

        foreach (var item in Items)
        {
            item.SetSelectionBorderBrush(borderBrush);
        }
    }

    private void SaveWindowBounds()
    {
        ClampToCurrentWorkArea();

        _state.Left = Left;
        _state.Top = Top;
        _state.Width = Width;

        if (!_state.IsCollapsed)
        {
            _state.Height = Height;
        }

        _save();
    }

    private void ResizeRight(double horizontalChange)
    {
        var workArea = GetCurrentWorkArea();
        var minWidth = _state.IsCollapsed ? CollapsedMinWidth : MinWidth;
        Width = Math.Clamp(Width + horizontalChange, minWidth, Math.Max(minWidth, workArea.Right - Left));

        if (_state.IsCollapsed)
        {
            Height = CollapsedHeight;
        }
    }

    private void ResizeLeft(double horizontalChange)
    {
        var workArea = GetCurrentWorkArea();
        var minWidth = _state.IsCollapsed ? CollapsedMinWidth : MinWidth;
        var maxRight = Left + Width;
        var desiredLeft = Math.Clamp(Left + horizontalChange, workArea.Left, maxRight - minWidth);
        var delta = desiredLeft - Left;

        Left = desiredLeft;
        Width -= delta;

        if (_state.IsCollapsed)
        {
            Height = CollapsedHeight;
        }
    }

    private void ResizeBottom(double verticalChange)
    {
        var workArea = GetCurrentWorkArea();
        Height = Math.Clamp(Height + verticalChange, 112, Math.Max(112, workArea.Bottom - Top));
    }

    private void ClampToCurrentWorkArea()
    {
        var workArea = GetCurrentWorkArea();

        if (Width > workArea.Width)
        {
            Width = Math.Max(MinWidth, workArea.Width);
        }

        if (!_state.IsCollapsed && Height > workArea.Height)
        {
            Height = Math.Max(112, workArea.Height);
        }

        Left = Math.Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
        Top = Math.Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
    }

    private Rect GetCurrentWorkArea()
    {
        var source = PresentationSource.FromVisual(this);
        var transformToDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var transformFromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        var topLeft = transformToDevice.Transform(new Point(Left, Top));
        var size = transformToDevice.Transform(new Vector(Width, Height));
        var screenBounds = new Drawing.Rectangle(
            (int)Math.Round(topLeft.X),
            (int)Math.Round(topLeft.Y),
            Math.Max(1, (int)Math.Round(Math.Abs(size.X))),
            Math.Max(1, (int)Math.Round(Math.Abs(size.Y))));

        var workingArea = Forms.Screen.FromRectangle(screenBounds).WorkingArea;
        var dipTopLeft = transformFromDevice.Transform(new Point(workingArea.Left, workingArea.Top));
        var dipBottomRight = transformFromDevice.Transform(new Point(workingArea.Right, workingArea.Bottom));
        return new Rect(dipTopLeft, dipBottomRight);
    }

    private bool ContainsPath(string path)
    {
        return _state.Items.Any(item =>
            string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.SourcePath, path, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> GetDesktopShortcutSnapshot()
    {
        return EnumerateDesktopShortcuts().ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? FindNewDesktopShortcut(string originalPath, HashSet<string> beforeDrop)
    {
        var originalName = Path.GetFileNameWithoutExtension(originalPath);
        return EnumerateDesktopShortcuts()
            .Where(path => !beforeDrop.Contains(path) && IsLikelySameShortcutName(originalName, path))
            .OrderByDescending(GetSafeCreationTimeUtc)
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateDesktopShortcuts()
    {
        var desktopPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        foreach (var desktopPath in desktopPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(desktopPath) || !Directory.Exists(desktopPath))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(desktopPath, "*", SearchOption.TopDirectoryOnly).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool IsLikelySameShortcutName(string originalName, string path)
    {
        var candidateName = Path.GetFileNameWithoutExtension(path);
        return string.Equals(candidateName, originalName, StringComparison.OrdinalIgnoreCase) ||
               candidateName.StartsWith(originalName + " (", StringComparison.OrdinalIgnoreCase) ||
               candidateName.StartsWith(originalName + " - ", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime GetSafeCreationTimeUtc(string path)
    {
        try
        {
            return File.GetCreationTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private void RefreshItemState()
    {
        OnPropertyChanged(nameof(ItemCountText));

        if (EmptyHint is not null)
        {
            EmptyHint.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private Vector ToDeviceIndependentVector(Vector deviceVector)
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformFromDevice.Transform(deviceVector) ?? deviceVector;
    }

    private static void ApplyDropEffect(System.Windows.DragEventArgs e)
    {
        e.Effects = TryGetDroppedFiles(e, out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private static bool TryGetDroppedFiles(System.Windows.DragEventArgs e, out string[] files)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] droppedFiles &&
            droppedFiles.Length > 0)
        {
            files = droppedFiles;
            return true;
        }

        files = [];
        return false;
    }

    private static string? GetBestExistingPath(CapsuleItemViewModel item)
    {
        if (File.Exists(item.Path) || Directory.Exists(item.Path))
        {
            return item.Path;
        }

        if (File.Exists(item.SourcePath) || Directory.Exists(item.SourcePath))
        {
            return item.SourcePath;
        }

        return null;
    }

    private static CapsuleItemViewModel? GetItemFromSender(object sender)
    {
        if (sender is FrameworkElement { DataContext: CapsuleItemViewModel directItem })
        {
            return directItem;
        }

        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: CapsuleItemViewModel menuItem } } })
        {
            return menuItem;
        }

        return null;
    }

    private static string GetSortExtension(CapsuleItemViewModel item)
    {
        var path = !string.IsNullOrWhiteSpace(item.SourcePath) ? item.SourcePath : item.Path;
        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(extension) ? "folder" : extension;
    }

    private static DateTime GetInstalledSortDate(CapsuleItemViewModel item)
    {
        foreach (var candidatePath in GetInstalledDateCandidatePaths(item))
        {
            if (InstalledDateResolver.TryGetInstalledDateUtc(candidatePath, out var installedDate))
            {
                return installedDate;
            }
        }

        foreach (var candidatePath in GetInstalledDateCandidatePaths(item))
        {
            if (TryGetCreationTimeUtc(candidatePath, out var creationTime))
            {
                return creationTime;
            }
        }

        return DateTime.MinValue;
    }

    private static IEnumerable<string> GetInstalledDateCandidatePaths(CapsuleItemViewModel item)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidatePaths = new[]
        {
            TryResolveShortcutTarget(item.Path),
            TryResolveShortcutTarget(item.SourcePath),
            item.SourcePath,
            item.Path
        };

        foreach (var candidatePath in candidatePaths)
        {
            if (!string.IsNullOrWhiteSpace(candidatePath) && seen.Add(candidatePath))
            {
                yield return candidatePath;
            }
        }
    }

    private static string? TryResolveShortcutTarget(string path)
    {
        if (ShellLinkService.TryReadShortcut(path, out var shortcut) &&
            !string.IsNullOrWhiteSpace(shortcut.TargetPath))
        {
            return ExpandPath(shortcut.TargetPath, shortcut.WorkingDirectory);
        }

        return null;
    }

    private static bool TryGetCreationTimeUtc(string path, out DateTime creationTimeUtc)
    {
        creationTimeUtc = DateTime.MinValue;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (File.Exists(path))
            {
                creationTimeUtc = File.GetCreationTimeUtc(path);
                return true;
            }

            if (Directory.Exists(path))
            {
                creationTimeUtc = Directory.GetCreationTimeUtc(path);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string ExpandPath(string path, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (!Path.IsPathRooted(path) && !string.IsNullOrWhiteSpace(baseDirectory))
        {
            path = Path.Combine(baseDirectory, path);
        }

        return path;
    }

    private static bool TryPickColor(string currentColor, out string selectedColor)
    {
        selectedColor = "";
        var mediaColor = ParseMediaColor(currentColor, Color.FromRgb(24, 32, 40));

        using var dialog = new Forms.ColorDialog
        {
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true,
            Color = Drawing.Color.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B)
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return false;
        }

        selectedColor = ToHex(dialog.Color);
        return true;
    }

    private static Color ParseMediaColor(string color, Color fallback)
    {
        try
        {
            return ColorConverter.ConvertFromString(color) is Color parsed ? parsed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string ToHex(Drawing.Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static Brush CreateHitTestableBrush(Color color, double opacity)
    {
        opacity = Math.Clamp(opacity, MinimumAppearanceOpacity, 1);
        var brush = new SolidColorBrush(color)
        {
            Opacity = opacity
        };
        brush.Freeze();
        return brush;
    }

    private static bool IsDescendantOf(DependencyObject? candidate, DependencyObject parent)
    {
        while (candidate is not null)
        {
            if (ReferenceEquals(candidate, parent))
            {
                return true;
            }

            candidate = VisualTreeHelper.GetParent(candidate);
        }

        return false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
