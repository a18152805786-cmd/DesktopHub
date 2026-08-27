using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// TEST-only Plan A prototype. It makes the central Frame content area
    /// absent from the HWND region while preserving the header and resize rim.
    /// It performs no input forwarding and has no background polling.
    /// </summary>
    public sealed class NativeFrameSurfaceManager : IDisposable
    {
        private const double DefaultResizeBorderDip = 8.0;
        private readonly NonActivatingWindow _window;
        private readonly DispatcherTimer _deferredUpdate;
        private readonly DispatcherTimer _layoutDebounce;
        private bool _attached;
        private bool _disposed;

        public NativeFrameRegionGeometry? LastGeometry { get; private set; }
        public bool RegionApplied { get; private set; }
        public event EventHandler? LayoutRefreshRequested;

        public NativeFrameSurfaceManager(NonActivatingWindow window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _deferredUpdate = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _deferredUpdate.Tick += DeferredUpdateOnTick;
            _layoutDebounce = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _layoutDebounce.Tick += LayoutDebounceOnTick;
        }

        public void Attach()
        {
            ThrowIfDisposed();
            if (_attached) return;

            _attached = true;
            _window.SizeChanged += WindowOnSizeChanged;
            _window.LocationChanged += WindowOnLocationChanged;
            _window.DpiChanged += WindowOnDpiChanged;
            _window.Closed += WindowOnClosed;
            _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyNow));
        }

        public void ApplyNow()
        {
            // A Loaded-priority callback can remain queued when a test or
            // profile reload closes the window. Treat that stale callback as
            // a no-op instead of surfacing an unhandled Dispatcher exception.
            if (_disposed) return;
            IntPtr hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Frame HWND is not initialized.");
            if (!GetWindowRect(hwnd, out NativeRect windowRect))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetWindowRect failed for the prototype Frame.");

            int widthPx = windowRect.Right - windowRect.Left;
            int heightPx = windowRect.Bottom - windowRect.Top;
            DpiScale dpi = VisualTreeHelper.GetDpi(_window);
            NativeFrameRegionGeometry geometry = NativeFrameRegionBuilder.Calculate(
                widthPx,
                heightPx,
                dpi.DpiScaleX,
                dpi.DpiScaleY,
                FrameVisualStyleHelper.HeaderHeight,
                DefaultResizeBorderDip);

            IntPtr region = NativeFrameRegionBuilder.Create(geometry);
            if (SetWindowRgn(hwnd, region, true) == 0)
            {
                NativeFrameRegionBuilder.DeleteUnowned(region);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowRgn failed for the prototype Frame.");
            }

            // After successful SetWindowRgn the operating system owns region.
            LastGeometry = geometry;
            RegionApplied = true;
        }

        public void ScheduleUpdate()
        {
            ThrowIfDisposed();
            _deferredUpdate.Stop();
            _layoutDebounce.Stop();
            _deferredUpdate.Start();
        }

        public void Clear()
        {
            if (_disposed) return;
            _deferredUpdate.Stop();
            IntPtr hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd != IntPtr.Zero && SetWindowRgn(hwnd, IntPtr.Zero, true) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Clearing the prototype Frame region failed.");
            RegionApplied = false;
            LastGeometry = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _deferredUpdate.Stop();
            _layoutDebounce.Stop();
            _deferredUpdate.Tick -= DeferredUpdateOnTick;
            _layoutDebounce.Tick -= LayoutDebounceOnTick;
            if (_attached)
            {
                _window.SizeChanged -= WindowOnSizeChanged;
                _window.LocationChanged -= WindowOnLocationChanged;
                _window.DpiChanged -= WindowOnDpiChanged;
                _window.Closed -= WindowOnClosed;
                _attached = false;
            }
        }

        private void WindowOnSizeChanged(object sender, SizeChangedEventArgs args)
        {
            ScheduleUpdate();
            ScheduleLayoutRefresh();
        }

        private void WindowOnLocationChanged(object? sender, EventArgs args) => ScheduleLayoutRefresh();

        private void WindowOnDpiChanged(object sender, DpiChangedEventArgs args) => ScheduleUpdate();

        private void WindowOnClosed(object? sender, EventArgs args) => Dispose();

        private void DeferredUpdateOnTick(object? sender, EventArgs args)
        {
            _deferredUpdate.Stop();
            ApplyNow();
        }

        private void ScheduleLayoutRefresh()
        {
            if (_disposed) return;
            _layoutDebounce.Stop();
            _layoutDebounce.Start();
        }

        private void LayoutDebounceOnTick(object? sender, EventArgs args)
        {
            _layoutDebounce.Stop();
            LayoutRefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    }
}
