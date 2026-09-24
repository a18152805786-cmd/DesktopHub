using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

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
    ///
    /// DesktopHub is system-DPI aware, so its DPI is fixed when the process
    /// starts. Moving between a laptop at 150% and a monitor at 100% leaves it
    /// running at the old DPI, and Windows then rescales every coordinate the
    /// process reads or writes by the ratio of the two - while Explorer keeps
    /// answering in real pixels. Everything here therefore works in physical
    /// pixels and converts only at the edge, using the desktop's real DPI.
    /// </summary>
    internal static class DesktopChromeDpi
    {
        private static readonly IntPtr PerMonitorAwareV2 = new(-4);
        private const uint MonitorDefaultToPrimary = 1;

        /// <summary>
        /// Physical pixels per DIP for chrome drawn over a desktop at
        /// <paramref name="desktopDpi"/>.
        ///
        /// A window's position in DIPs is multiplied by the process DPI and then,
        /// if the desktop's DPI differs, rescaled by Windows by desktop/process.
        /// The process DPI cancels out, so the desktop's DPI alone decides where
        /// the window lands, whether or not the process outlived a scaling change.
        /// </summary>
        public static double ScaleFor(int desktopDpi) => (desktopDpi > 0 ? desktopDpi : ProcessDpi) / 96.0;

        /// <summary>
        /// The DPI this process renders at. It is fixed when the process starts
        /// and does not follow a later scaling change.
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
        /// Runs <paramref name="read"/> with this thread briefly per-monitor aware,
        /// so window rectangles, monitor areas and the cursor come back in the
        /// physical pixels Explorer uses, not rescaled to a stale process DPI.
        /// </summary>
        public static T InPhysicalPixels<T>(Func<T> read)
        {
            IntPtr previous;
            try
            {
                previous = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
            }
            catch (EntryPointNotFoundException)
            {
                return read();
            }
            try
            {
                return read();
            }
            finally
            {
                if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
            }
        }

        /// <summary>Work area of the primary display, in physical pixels.</summary>
        public static Rectangle PrimaryWorkArea() => InPhysicalPixels(() =>
        {
            IntPtr monitor = MonitorFromPoint(new NativePoint(), MonitorDefaultToPrimary);
            var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                return Rectangle.FromLTRB(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
            return Forms.Screen.PrimaryScreen?.WorkingArea ?? Forms.Screen.AllScreens[0].WorkingArea;
        });

        /// <summary>Pointer position in physical pixels, the unit the desktop grid spacing is measured in.</summary>
        public static System.Windows.Point CursorPosition() => InPhysicalPixels(() =>
        {
            GetCursorPos(out NativePoint point);
            return new System.Windows.Point(point.X, point.Y);
        });

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);
    }
}
