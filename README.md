# Wheel of Thoughts

Wheel of Thoughts is a small Windows desktop utility that opens a radial menu when you Shift+drag files from File Explorer or the desktop. Drop the files on a menu segment to open, rename, delete, or view properties.

The app runs in the system tray and does not have a conventional main window.

## Requirements

- Windows
- .NET 8 SDK

## Run from source

From the repository root, run:

```powershell
dotnet run --project .\WheelOfThoughts\WheelOfThoughts.csproj
```

To build the app:

```powershell
dotnet build .\WheelOfThoughts\WheelOfThoughts.csproj
```

For step-by-step instructions, see [HOW-TO-USE.md](./HOW-TO-USE.md).

## Exit

Find the Wheel of Thoughts icon in the notification area (system tray), open its context menu, and choose **Exit**.

## Notes

- The gesture is supported in Windows File Explorer and on the desktop.
- Delete sends files to the Recycle Bin and uses Windows' confirmation dialogs.
- The diagnostic log is written to `%TEMP%\WheelOfThoughts.log`.
