using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Desktop_Frames.Localization;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// Physical pixels versus device-independent units for the desktop chrome.
    ///
    /// Every layout coordinate comes from the Desktop ListView, which answers in
    /// physical pixels. A WPF window is placed in DIPs. At 100% scaling the two
    /// are the same number, which is how the chrome got away with mixing them;
    /// at 150% a header told to sit at x=115 lands at 172, and the right rail
    /// ends up beyond the edge of the screen.
    /// </summary>
    internal static class DesktopChromeDpi
    {
        private const int DpiAwarenessPerMonitor = 2;

        /// <summary>Physical pixels per DIP for a window, or for this process before the window has a handle.</summary>
        public static double ScaleOf(Visual? visual)
        {
            if (visual != null && PresentationSource.FromVisual(visual) != null)
            {
                double scale = VisualTreeHelper.GetDpi(visual).DpiScaleX;
                if (scale > 0) return scale;
            }
            return ProcessDpi / 96.0;
        }

        /// <summary>
        /// The DPI this process renders at. DesktopHub is system-DPI aware, so
        /// the value is fixed when the process starts and does not follow a
        /// later scaling change.
        /// </summary>
        public static int ProcessDpi
        {
            get
            {
                try
                {
                    uint dpi = GetDpiForSystem();
                    return dpi > 0 ? (int)dpi : 96;
                }
                catch (EntryPointNotFoundException)
                {
                    return 96;
                }
            }
        }

        /// <summary>DPI of a window, as its owner renders it.</summary>
        public static int WindowDpi(IntPtr hwnd)
        {
            try
            {
                uint dpi = GetDpiForWindow(hwnd);
                return dpi > 0 ? (int)dpi : 96;
            }
            catch (EntryPointNotFoundException)
            {
                return 96;
            }
        }

        /// <summary>
        /// True when the display scaling changed after this process started.
        /// Windows then rescales every coordinate this process reads into its
        /// old DPI, while Explorer keeps answering in real pixels. No layout
        /// computed from that mixture is safe to apply; only a restart fixes it.
        /// </summary>
        public static bool IsProcessDpiStale(int desktopDpi)
        {
            if (desktopDpi <= 0) return false;
            try
            {
                if (GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) == DpiAwarenessPerMonitor)
                    return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            return desktopDpi != ProcessDpi;
        }

        public static void ThrowIfProcessDpiStale(int desktopDpi)
        {
            if (IsProcessDpiStale(desktopDpi))
                throw new InvalidOperationException(
                    $"DPI_CONTEXT_STALE ProcessDpi={ProcessDpi}; DesktopDpi={desktopDpi}. {Strings.DesktopLayoutRestartRequired}");
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);
    }
}
