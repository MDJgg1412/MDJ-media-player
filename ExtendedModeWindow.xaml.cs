using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace MDJMediaPlayer
{
    public partial class ExtendedModeWindow : Window
    {
        public Panel HostPanel => VideoHost;
        private bool _isFullscreen;
        private bool _suppressStateChanged;
        private Rect _restoreBounds;

        public ExtendedModeWindow()
        {
            InitializeComponent();
            StateChanged += ExtendedModeWindow_StateChanged;
            PreviewKeyDown += ExtendedModeWindow_PreviewKeyDown;
        }

        private void ExtendedModeWindow_StateChanged(object? sender, System.EventArgs e)
        {
            if (_suppressStateChanged)
            {
                return;
            }

            if (WindowState == WindowState.Maximized && !_isFullscreen)
            {
                EnterFullscreen();
            }
            else if (_isFullscreen && WindowState == WindowState.Normal)
            {
                ExitFullscreen();
            }
        }

        private void ExtendedModeWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isFullscreen)
            {
                ExitFullscreen();
                e.Handled = true;
            }
        }

        private void EnterFullscreen()
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            var bounds = GetMonitorBounds();

            _suppressStateChanged = true;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            _isFullscreen = true;
            _suppressStateChanged = false;
        }

        private void ExitFullscreen()
        {
            _suppressStateChanged = true;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            Topmost = false;
            WindowState = WindowState.Normal;
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            _isFullscreen = false;
            _suppressStateChanged = false;
        }

        private Rect GetMonitorBounds()
        {
            var handle = new WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
            if (monitor == System.IntPtr.Zero)
            {
                return new Rect(
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);
            }

            var monitorInfo = new MONITORINFO();
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return new Rect(
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);
            }

            return new Rect(
                monitorInfo.rcMonitor.Left,
                monitorInfo.rcMonitor.Top,
                monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left,
                monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top);
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;

            public MONITORINFO()
            {
                cbSize = Marshal.SizeOf<MONITORINFO>();
                rcMonitor = default;
                rcWork = default;
                dwFlags = 0;
            }
        }
    }
}
