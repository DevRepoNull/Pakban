using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Pakban.Services;
using Pakban.ViewModels;

namespace Pakban.Views;

public partial class MainWindow : Window
{
    private TaskCompletionSource<bool>? _dialogTcs;
    private bool _dialogIsDanger;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        UiDialogs.Host = ShowDialogAsync;
    }

    // ---------------------------------------------------------------- //
    //  Window chrome                                                    //
    // ---------------------------------------------------------------- //

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // Hook Win32 messages to enforce desktop working area on maximize (prevents taskbar overlap)
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WindowProc);

        // Dark title bar + rounded corners on Windows 11 (silently ignored elsewhere).
        try
        {
            int dark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        }
        catch { }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        MaxButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

        // Compensate for Windows 10/11 invisible 8px resize border frame when maximized
        if (RootGrid != null)
        {
            RootGrid.Margin = WindowState == WindowState.Maximized
                ? new Thickness(8)
                : new Thickness(0);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        const int MONITOR_DEFAULTTONEAREST = 0x00000002;
        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            GetMonitorInfo(monitor, ref monitorInfo);

            RECT rcWorkArea = monitorInfo.rcWork;
            RECT rcMonitorArea = monitorInfo.rcMonitor;

            mmi.ptMaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
            mmi.ptMaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);
            mmi.ptMaxSize.X = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
            mmi.ptMaxSize.Y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    // ---------------------------------------------------------------- //
    //  Modal dialog overlay                                             //
    // ---------------------------------------------------------------- //

    private Task<bool> ShowDialogAsync(string title, string message, bool danger)
    {
        _dialogTcs?.TrySetResult(false);
        _dialogTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dialogIsDanger = danger;

        DialogTitle.Text = title;
        DialogMessage.Text = message;

        var accent = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        var dangerBrush = (SolidColorBrush)Application.Current.Resources["DangerBrush"];

        if (danger)
        {
            DialogIconBox.Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xF8, 0x71, 0x71));
            DialogIcon.Foreground = dangerBrush;
            DialogIcon.Text = "\uE7BA";
            DialogOk.Background = dangerBrush;
        }
        else
        {
            DialogIconBox.Background = new SolidColorBrush(Color.FromArgb(0x2E, accent.Color.R, accent.Color.G, accent.Color.B));
            DialogIcon.Foreground = accent;
            DialogIcon.Text = "\uE73E";
            DialogOk.ClearValue(BackgroundProperty);
        }

        DialogHost.Visibility = Visibility.Visible;
        DialogHost.Opacity = 0;
        DialogHost.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

        return _dialogTcs.Task;
    }

    private void CloseDialog(bool result)
    {
        DialogHost.Visibility = Visibility.Collapsed;
        _dialogTcs?.TrySetResult(result);
        _dialogTcs = null;
    }

    private void OnDialogOk(object sender, RoutedEventArgs e) => CloseDialog(true);
    private void OnDialogCancel(object sender, RoutedEventArgs e) => CloseDialog(false);

    private void OnDialogBackdropClick(object sender, MouseButtonEventArgs e) => CloseDialog(false);

    // ---------------------------------------------------------------- //
    #region Win32 P/Invoke & Interop

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("dwmapi.dll")]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    #endregion
}
