using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Desktop_Frames.NativeDesktop
{
    public sealed class NativeFrameRegionGeometry
    {
        public int WidthPx { get; init; }
        public int HeightPx { get; init; }
        public int HeaderHeightPx { get; init; }
        public int ResizeBorderPx { get; init; }
        public int ContentLeftPx { get; init; }
        public int ContentTopPx { get; init; }
        public int ContentRightPx { get; init; }
        public int ContentBottomPx { get; init; }
    }

    /// <summary>
    /// Builds a single HWND region containing the header and resize border while
    /// excluding the central content rectangle. The caller transfers ownership
    /// of a successful region to SetWindowRgn.
    /// </summary>
    internal static class NativeFrameRegionBuilder
    {
        private const int RgnDiff = 4;
        private const int RegionError = 0;

        public static NativeFrameRegionGeometry Calculate(
            int widthPx, int heightPx, double dpiScaleX, double dpiScaleY,
            double headerHeightDip, double resizeBorderDip)
        {
            if (widthPx <= 0) throw new ArgumentOutOfRangeException(nameof(widthPx));
            if (heightPx <= 0) throw new ArgumentOutOfRangeException(nameof(heightPx));

            int headerPx = Math.Clamp(
                (int)Math.Ceiling(headerHeightDip * Math.Max(dpiScaleY, 0.1)), 1, heightPx);
            int horizontalBorderPx = Math.Clamp(
                (int)Math.Ceiling(resizeBorderDip * Math.Max(dpiScaleX, 0.1)), 1, Math.Max(1, widthPx / 2));
            int verticalBorderPx = Math.Clamp(
                (int)Math.Ceiling(resizeBorderDip * Math.Max(dpiScaleY, 0.1)), 1, Math.Max(1, heightPx / 2));

            return new NativeFrameRegionGeometry
            {
                WidthPx = widthPx,
                HeightPx = heightPx,
                HeaderHeightPx = headerPx,
                ResizeBorderPx = Math.Max(horizontalBorderPx, verticalBorderPx),
                ContentLeftPx = horizontalBorderPx,
                ContentTopPx = headerPx,
                ContentRightPx = Math.Max(horizontalBorderPx, widthPx - horizontalBorderPx),
                ContentBottomPx = Math.Max(headerPx, heightPx - verticalBorderPx)
            };
        }

        public static IntPtr Create(NativeFrameRegionGeometry geometry)
        {
            ArgumentNullException.ThrowIfNull(geometry);
            IntPtr outer = CreateRectRgn(0, 0, geometry.WidthPx, geometry.HeightPx);
            if (outer == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRectRgn failed for the Frame outer region.");

            IntPtr contentHole = CreateRectRgn(
                geometry.ContentLeftPx,
                geometry.ContentTopPx,
                geometry.ContentRightPx,
                geometry.ContentBottomPx);
            if (contentHole == IntPtr.Zero)
            {
                DeleteObject(outer);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRectRgn failed for the Frame content hole.");
            }

            try
            {
                if (CombineRgn(outer, outer, contentHole, RgnDiff) == RegionError)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CombineRgn failed for the hollow Frame region.");
                return outer;
            }
            catch
            {
                DeleteObject(outer);
                throw;
            }
            finally
            {
                DeleteObject(contentHole);
            }
        }

        public static void DeleteUnowned(IntPtr region)
        {
            if (region != IntPtr.Zero) DeleteObject(region);
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int mode);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr value);
    }
}
