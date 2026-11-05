using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace WheelOfThoughts;

static class Program
{
    [STAThread]
    static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var wheel = new WheelWindow();

        MouseHook.DragStarted += wheel.ShowAtCursor;
        MouseHook.ButtonReleased += wheel.HideSoon;
        MouseHook.Start();

        var tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Wheel of Thoughts: select a file, then Shift + drag it",
            Visible = true,
            ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip()
        };
        tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => app.Shutdown());

        app.Exit += (_, _) =>
        {
            MouseHook.Stop();
            tray.Visible = false;
            tray.Dispose();
        };
        app.Run();
    }
}

/// <summary>
/// Global low-level mouse hook. Fires DragStarted when the left button is pressed
/// with Shift held over Explorer or the desktop and then moves a few pixels.
/// </summary>
static class MouseHook
{
    public static event Action? DragStarted;
    public static event Action? ButtonReleased;

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    static readonly HookProc Proc = Callback; // keep a reference so it isn't garbage collected
    static IntPtr _hook;
    static bool _armed;
    static int _startX, _startY;

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }

    public static void Start()
    {
        using var p = Process.GetCurrentProcess();
        _hook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, Proc, GetModuleHandle(p.MainModule!.ModuleName), 0);
    }

    public static void Stop()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    static IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var m = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            switch ((int)wParam)
            {
                case 0x0201: // WM_LBUTTONDOWN
                    // Shift is checked once the mouse moves, so it doesn't matter whether the
                    // file was already selected or which order the button and Shift went down.
                    _armed = OverExplorer(m.pt);
                    _startX = m.pt.X; _startY = m.pt.Y;
                    Log.Write($"down over '{_lastClass}' armed={_armed} shift={ShiftDown()}");
                    break;

                case 0x0200 when _armed: // WM_MOUSEMOVE
                    if (Math.Abs(m.pt.X - _startX) + Math.Abs(m.pt.Y - _startY) > 12)
                    {
                        _armed = false;
                        bool shift = ShiftDown();
                        Log.Write($"moved, shift={shift}");
                        if (shift) DragStarted?.Invoke();
                    }
                    break;

                case 0x0202: // WM_LBUTTONUP
                    _armed = false;
                    Log.Write("button released");
                    ButtonReleased?.Invoke();
                    break;
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    static string _lastClass = "";

    static bool ShiftDown() => (GetAsyncKeyState(0x10) & 0x8000) != 0;

    static bool OverExplorer(POINT pt)
    {
        var root = GetAncestor(WindowFromPoint(pt), 2 /* GA_ROOT */);
        var name = new StringBuilder(64);
        GetClassName(root, name, name.Capacity);
        _lastClass = name.ToString();
        return _lastClass is "CabinetWClass" or "Progman" or "WorkerW";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
}

/// <summary>Tiny diagnostic log in %TEMP%\WheelOfThoughts.log (delete it any time).</summary>
static class Log
{
    static readonly string File_ = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WheelOfThoughts.log");

    public static void Write(string message)
    {
        try { System.IO.File.AppendAllText(File_, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}"); }
        catch { /* logging must never break the app */ }
    }
}
