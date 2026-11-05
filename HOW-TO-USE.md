# How to Use Wheel of Thoughts

## Start the app

Run the app from the repository root:

```powershell
dotnet run --project .\WheelOfThoughts\WheelOfThoughts.csproj
```

Alternatively, build it with `dotnet build` and launch `WheelOfThoughts.exe` from `WheelOfThoughts\bin\Debug\net8.0-windows`.

When it starts, Wheel of Thoughts runs in the notification area (system tray). It does not open a main window.

## Open the wheel

1. In Windows File Explorer or on the desktop, select the file or files you want to act on.
2. Press and hold **Shift**.
3. Press and hold the left mouse button over the selected item, then move the pointer a short distance while continuing to hold both.
4. The wheel appears centered under the pointer. Keep holding the mouse button and move over the action you want.
5. Release the mouse button over that segment to perform the action.

Shift can be pressed before or after the mouse button, as long as it is held when you begin moving. Releasing without dropping on an action closes the wheel without running an action. The wheel only recognizes drags that start in Windows File Explorer or on the desktop.

## Actions

| Segment | What it does |
| --- | --- |
| **Open** | Opens each dropped file using its Windows-associated application. |
| **Rename** | Prompts for a new name and renames the first dropped item. For predictable results, drop one item. |
| **Delete** | Deletes each dropped item to the Recycle Bin and shows Windows' confirmation dialog(s). |
| **Properties** | Opens the Windows Properties dialog for the first dropped item. |

Hover over a segment to highlight it. The center of the wheel displays the dropped item's name, or the number of items when multiple files are dragged.

## Exit the app

Open the Wheel of Thoughts icon's context menu in the system tray and choose **Exit**. If the icon is hidden, open the tray's overflow area.

## Troubleshooting

- **The wheel does not appear:** Make sure the app is running in the system tray, start the drag in File Explorer or on the desktop, and hold Shift while moving the mouse.
- **The wheel disappears without an action:** Release the mouse button over a segment, not outside the wheel or in its center.
- **An action fails:** The app displays a warning with the Windows error. The diagnostic log, if needed, is at `%TEMP%\WheelOfThoughts.log`.
