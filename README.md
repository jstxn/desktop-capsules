# DesktopCapsules

DesktopCapsules is a Windows desktop prototype for grouping shortcuts into small draggable boxes.

## Run

```powershell
dotnet run --project .\DesktopCapsules.csproj
```

The built executable is here:

```text
.\bin\Debug\net9.0-windows\DesktopCapsules.exe
```

## Current Behavior

- Creates one default capsule named `Apps`.
- Drag the capsule by its header.
- Click the capsule name to rename it; press Enter or click away to save, Esc to cancel.
- Resize from the lower-right handle.
- Use the `...` toolbar button to customize background color, border color, header color, roundness, background opacity, and header opacity.
- Use `... > Sort items` to sort by name, file type, best-effort date installed, or shortcut location.
- Drop files, folders, `.lnk` shortcuts, or `.url` shortcuts into the capsule.
- Desktop `.lnk` files are moved into app-managed storage so the desktop is cleaned up.
- Non-desktop `.lnk` files are copied into app-managed storage.
- `.url` shortcuts are managed directly so Steam/game icons can be read from their `IconFile`.
- Files and folders are added by creating managed `.lnk` shortcuts to them.
- Drag an item out of a capsule onto the desktop to move its managed shortcut back and remove it from the capsule.
- Click a capsule item once to select it; click the selected item again to launch it.
- Right-click an item to open, open location, restore to desktop, or remove it.
- Right-click the tray icon to create another capsule, show existing capsules, open the data folder, toggle Windows startup, or exit.
- Double-click the tray icon to show existing capsules.

## Data

Layout and managed shortcuts are stored under:

```text
%APPDATA%\DesktopCapsules
```

## Performance Notes

The prototype uses one small WPF window per capsule rather than one fullscreen transparent overlay. That keeps repaint areas small and makes dragging cheap. Shell icons are cached after first extraction, and the app does no idle polling.

The first build experimented with parenting capsules directly to Explorer's desktop window. That can hide WPF layered windows behind Explorer's internal desktop layers on some Windows builds, so this prototype currently uses visible top-level tool windows instead.
