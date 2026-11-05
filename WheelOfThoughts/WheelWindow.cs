using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;

namespace WheelOfThoughts;

/// <summary>
/// A round, always-on-top window that appears under the cursor mid-drag.
/// It acts as a drop target: hovering highlights a wedge, dropping runs that action.
/// </summary>
sealed class WheelWindow : Window
{
    const double Dim = 360, Inner = 58, Outer = 170;

    static readonly Brush Base = new SolidColorBrush(Color.FromArgb(0xE8, 0x1A, 0x1D, 0x26));
    static readonly Brush Light = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF1));
    static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x1B));
    static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    // Label, icon glyph, hover colour, action. Delete glows coral so it reads as the risky one.
    static readonly (string Label, string Glyph, Color Hover, Action<string[]> Run)[] Items =
    {
        ("Open",       "\uE8A7", Color.FromRgb(0x3D, 0xD6, 0xC6), Actions.Open),
        ("Rename",     "\uE8AC", Color.FromRgb(0x3D, 0xD6, 0xC6), Actions.Rename),
        ("Delete",     "\uE74D", Color.FromRgb(0xFF, 0x6B, 0x6B), Actions.Delete),
        ("Properties", "\uE946", Color.FromRgb(0x3D, 0xD6, 0xC6), Actions.Properties),
    };

    readonly System.Windows.Shapes.Path[] _wedges = new System.Windows.Shapes.Path[Items.Length];
    readonly TextBlock[] _glyphs = new TextBlock[Items.Length];
    readonly TextBlock[] _labels = new TextBlock[Items.Length];
    readonly TextBlock _center = new();
    readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    int _hot = -2;

    public WheelWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = Height = Dim;
        AllowDrop = true;

        var canvas = new Canvas();

        // A faint disc behind the wedges: it makes the wheel read as one object and gives
        // the whole round area something solid to receive drag events.
        var disc = new System.Windows.Shapes.Ellipse
        {
            Width = (Outer + 8) * 2, Height = (Outer + 8) * 2,
            Fill = new SolidColorBrush(Color.FromArgb(0x38, 0x0A, 0x0C, 0x12))
        };
        Canvas.SetLeft(disc, Dim / 2 - (Outer + 8));
        Canvas.SetTop(disc, Dim / 2 - (Outer + 8));
        canvas.Children.Add(disc);
        double sweep = 360.0 / Items.Length;

        for (int i = 0; i < Items.Length; i++)
        {
            _wedges[i] = new System.Windows.Shapes.Path
            {
                Data = Wedge(i * sweep + 1.2, (i + 1) * sweep - 1.2),
                Fill = Base
            };
            canvas.Children.Add(_wedges[i]);

            _glyphs[i] = new TextBlock
            {
                Text = Items[i].Glyph, FontFamily = Icons, FontSize = 22,
                Foreground = Light, HorizontalAlignment = HorizontalAlignment.Center
            };
            _labels[i] = new TextBlock
            {
                Text = Items[i].Label, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 13, Foreground = Light, HorizontalAlignment = HorizontalAlignment.Center
            };
            var stack = new StackPanel { Width = 90, Height = 48, IsHitTestVisible = false };
            stack.Children.Add(_glyphs[i]);
            stack.Children.Add(_labels[i]);

            var mid = P((Inner + Outer) / 2, (i + 0.5) * sweep);
            Canvas.SetLeft(stack, mid.X - 45);
            Canvas.SetTop(stack, mid.Y - 24);
            canvas.Children.Add(stack);
        }

        var hub = new System.Windows.Shapes.Ellipse { Width = (Inner - 6) * 2, Height = (Inner - 6) * 2, Fill = Base };
        Canvas.SetLeft(hub, Dim / 2 - (Inner - 6));
        Canvas.SetTop(hub, Dim / 2 - (Inner - 6));
        canvas.Children.Add(hub);

        _center.Width = 84;
        _center.TextAlignment = TextAlignment.Center;
        _center.TextWrapping = TextWrapping.Wrap;
        _center.TextTrimming = TextTrimming.CharacterEllipsis;
        _center.MaxHeight = 40;
        _center.FontSize = 11;
        _center.Foreground = Light;
        _center.IsHitTestVisible = false;
        Canvas.SetLeft(_center, Dim / 2 - 42);
        Canvas.SetTop(_center, Dim / 2 - 10);
        canvas.Children.Add(_center);

        Content = canvas;

        DragEnter += (s, e) => { Log.Write("drag entered the wheel"); OnDrag(s, e); };
        DragOver += OnDrag;
        // Leaving the wheel only clears the highlight. The wheel stays until the button is released.
        DragLeave += (_, _) => { SetHot(-1); _center.Text = ""; Log.Write("drag left the wheel"); };
        Drop += OnDrop;
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); Log.Write("wheel hidden"); };
    }

    // ---- showing / hiding -------------------------------------------------

    public void ShowAtCursor() => Dispatcher.InvokeAsync(() =>
    {
        _hideTimer.Stop();
        GetCursorPos(out var pt);
        _center.Text = "";
        _hot = -2;
        SetHot(-1);
        Opacity = 0;   // avoids a one-frame flash in the wrong place
        Show();

        // Place the wheel in raw screen pixels, the same space the cursor position came from,
        // so it stays centred under the cursor at any Windows display scaling.
        var h = new WindowInteropHelper(this).Handle;
        GetWindowRect(h, out var r);
        SetWindowPos(h, new IntPtr(-1), pt.X - (r.Right - r.Left) / 2, pt.Y - (r.Bottom - r.Top) / 2,
                     0, 0, 0x0001 | 0x0010);   // SWP_NOSIZE | SWP_NOACTIVATE
        Opacity = 1;
        Log.Write($"wheel shown at {pt.X},{pt.Y}");
    });

    // If the mouse is released and no drop reached us, close the wheel shortly after.
    public void HideSoon() => Dispatcher.InvokeAsync(() =>
    {
        if (!IsVisible) return;
        _hideTimer.Stop();
        _hideTimer.Start();
    });

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: never steal focus, stay out of Alt+Tab.
        var h = new WindowInteropHelper(this).Handle;
        SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x00000080);
    }

    // ---- drag and drop ----------------------------------------------------

    void OnDrag(object sender, DragEventArgs e)
    {
        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        // "Link" keeps Explorer from moving or copying the file; we only run our action.
        e.Effects = files.Length > 0 ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;

        _center.Text = files.Length switch
        {
            0 => "",
            1 => System.IO.Path.GetFileName(files[0].TrimEnd('\\')) is { Length: > 0 } n ? n : files[0],
            _ => $"{files.Length} items"
        };
        SetHot(HitTest(e.GetPosition(this)));
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        int i = HitTest(e.GetPosition(this));
        Log.Write($"dropped on wedge {i}, {files.Length} file(s)");
        e.Effects = DragDropEffects.Link;
        e.Handled = true;
        Hide();

        // Run after Drop returns so dialogs don't block Explorer's drag loop.
        if (i >= 0 && files.Length > 0)
            Dispatcher.InvokeAsync(() => Items[i].Run(files));
    }

    int HitTest(Point p)
    {
        double dx = p.X - Dim / 2, dy = p.Y - Dim / 2, r = Math.Sqrt(dx * dx + dy * dy);
        if (r < Inner || r > Outer) return -1;
        double angle = Math.Atan2(dx, -dy) * 180 / Math.PI;   // 0 at the top, clockwise
        if (angle < 0) angle += 360;
        return Math.Min((int)(angle / (360.0 / Items.Length)), Items.Length - 1);
    }

    void SetHot(int index)
    {
        if (index == _hot) return;
        _hot = index;
        for (int k = 0; k < Items.Length; k++)
        {
            bool on = k == index;
            _wedges[k].Fill = on ? new SolidColorBrush(Items[k].Hover) : Base;
            _glyphs[k].Foreground = _labels[k].Foreground = on ? Ink : Light;
        }
    }

    // ---- geometry ---------------------------------------------------------

    static Point P(double radius, double degrees)
    {
        double a = degrees * Math.PI / 180;
        return new Point(Dim / 2 + radius * Math.Sin(a), Dim / 2 - radius * Math.Cos(a));
    }

    static Geometry Wedge(double a0, double a1)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(P(Inner, a0), true, true);
            c.LineTo(P(Outer, a0), true, false);
            c.ArcTo(P(Outer, a1), new Size(Outer, Outer), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(P(Inner, a1), true, false);
            c.ArcTo(P(Inner, a0), new Size(Inner, Inner), 0, false, SweepDirection.Counterclockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out PT pt);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}

/// <summary>The actions behind each wedge. Add new ones here and in WheelWindow.Items.</summary>
static class Actions
{
    public static void Open(string[] files)
    {
        foreach (var f in files)
        {
            try { Process.Start(new ProcessStartInfo(f) { UseShellExecute = true }); }
            catch (Exception ex) { Error(ex); }
        }
    }

    public static void Rename(string[] files)
    {
        var path = files[0].TrimEnd('\\');
        var current = System.IO.Path.GetFileName(path);
        var name = Ask("Rename", current)?.Trim();
        if (string.IsNullOrEmpty(name) || name == current) return;

        var target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, name);
        try
        {
            if (Directory.Exists(path)) Directory.Move(path, target);
            else File.Move(path, target);
        }
        catch (Exception ex) { Error(ex); }
    }

    public static void Delete(string[] files)
    {
        foreach (var f in files)
        {
            try
            {
                // Goes to the Recycle Bin and asks for confirmation.
                if (Directory.Exists(f))
                    FileSystem.DeleteDirectory(f, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
                else
                    FileSystem.DeleteFile(f, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Error(ex); }
        }
    }

    public static void Properties(string[] files)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = 0x0000000C, // SEE_MASK_INVOKEIDLIST
            lpVerb = "properties",
            lpFile = files[0],
            nShow = 5
        };
        ShellExecuteEx(ref info);
    }

    static string? Ask(string title, string initial)
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(14, 14, 14, 10), MinWidth = 320, FontSize = 14 };
        var ok = new Button
        {
            Content = "Rename", IsDefault = true, Padding = new Thickness(18, 5, 18, 5),
            Margin = new Thickness(14, 0, 14, 14), HorizontalAlignment = HorizontalAlignment.Right
        };
        var panel = new StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(ok);

        var win = new Window
        {
            Title = title, Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize, Topmost = true
        };
        ok.Click += (_, _) => win.DialogResult = true;
        win.Loaded += (_, _) =>
        {
            win.Activate();
            box.Focus();
            // Select the name without the extension, like Explorer does.
            var dot = initial.LastIndexOf('.');
            box.Select(0, dot > 0 ? dot : initial.Length);
        };
        return win.ShowDialog() == true ? box.Text : null;
    }

    static void Error(Exception ex) =>
        MessageBox.Show(ex.Message, "Wheel of Thoughts", MessageBoxButton.OK, MessageBoxImage.Warning);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb, lpFile, lpParameters, lpDirectory;
        public int nShow;
        public IntPtr hInstApp, lpIDList;
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon, hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);
}
