using Desktop_Frames.ReferenceGrouping;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// Event-driven native visual ownership for a Frame. It only changes
    /// Explorer ListView coordinates after a deliberate frame move/resize or
    /// explicit apply; it never watches, forwards input, or changes files.
    /// </summary>
    internal static class NativeDesktopVisualGroupingService
    {
        private const int ContentPaddingPx = 8;

        public static bool NativeVisualGroupingEnabled
        {
            get => VisualOwnershipModes.NativeVisualGroupingEnabled;
            set => VisualOwnershipModes.NativeVisualGroupingEnabled = value;
        }

        public static bool IsNativeFrame(dynamic frame) => VisualOwnershipModes.IsNativeFrame(frame);

        public static NativeDesktopVisualLayoutResult LayoutReferencesInsideFrame(
            NonActivatingWindow window, dynamic frame)
        {
            var result = new NativeDesktopVisualLayoutResult();
            if (!NativeVisualGroupingEnabled)
            {
                result.Reason = "NATIVE_VISUAL_GROUPING_DISABLED";
                return result;
            }

            if (!IsNativeFrame(frame))
            {
                result.Reason = "NO_NATIVE_REFERENCES";
                return result;
            }

            NativeFrameRegionGeometry? geometry = window.NativeSurfacePrototypeGeometry;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (geometry == null || hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out NativeRect frameRect))
            {
                result.Reason = "NATIVE_FRAME_SURFACE_UNAVAILABLE";
                return result;
            }

            Drawing.Rectangle primary = Forms.Screen.PrimaryScreen?.Bounds ?? Forms.Screen.AllScreens.First().Bounds;
            Drawing.Rectangle frameBounds = Drawing.Rectangle.FromLTRB(frameRect.Left, frameRect.Top, frameRect.Right, frameRect.Bottom);
            if (!primary.Contains(frameBounds))
            {
                result.Reason = "PRIMARY_MONITOR_ONLY";
                return result;
            }

            List<string> paths = GetNativeReferencePaths(frame, result);
            if (paths.Count == 0)
            {
                if (string.IsNullOrEmpty(result.Reason)) result.Reason = "NO_VALID_NATIVE_REFERENCES";
                return result;
            }

            var layout = new NativeDesktopIconLayoutService();
            NativeDesktopListViewState listView = layout.GetListViewState();
            result.AutoArrangeEnabled = listView.AutoArrangeEnabled;
            result.AlignToGridEnabled = listView.AlignToGridEnabled;
            if (listView.AutoArrangeEnabled)
            {
                result.Reason = "AUTO_ARRANGE_ENABLED";
                return result;
            }

            int left = frameRect.Left + geometry.ContentLeftPx + ContentPaddingPx;
            int top = frameRect.Top + geometry.ContentTopPx + ContentPaddingPx;
            int right = frameRect.Left + geometry.ContentRightPx - ContentPaddingPx;
            int bottom = frameRect.Top + geometry.ContentBottomPx - ContentPaddingPx;
            int gridX = Math.Max(listView.GridSpacingX, 1);
            int gridY = Math.Max(listView.GridSpacingY, 1);
            IReadOnlyList<NativeDesktopIconPlacement> placements = BuildPlacements(paths, left, top, right, bottom, gridX, gridY);
            result.RequiredCapacity = paths.Count;
            result.AvailableSlots = CountSlots(left, top, right, bottom, gridX, gridY);
            if (placements.Count != paths.Count)
            {
                result.Reason = "FRAME_CAPACITY_INSUFFICIENT";
                return result;
            }

            // Preflight all identities before changing a single icon so an
            // ambiguous/missing item cannot leave a partially moved group.
            var available = layout.EnumerateDesktopIcons()
                .Where(icon => icon.IdentityStatus.IsResolved())
                .GroupBy(icon => NormalizePath(icon.OriginalPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (NativeDesktopIconPlacement placement in placements)
            {
                if (!available.TryGetValue(NormalizePath(placement.OriginalPath), out int matches) || matches != 1)
                {
                    result.Reason = matches == 0 ? "ICON_NOT_FOUND" : "AMBIGUOUS_ICON_IDENTITY";
                    return result;
                }
            }

            foreach (NativeDesktopIconPlacement placement in placements)
            {
                if (!layout.TrySetPosition(placement.OriginalPath, placement.X, placement.Y, out _, out string reason))
                {
                    result.Reason = reason;
                    return result;
                }
                result.Positioned++;
            }

            result.Applied = true;
            result.Reason = "APPLIED";
            return result;
        }

        private static List<string> GetNativeReferencePaths(dynamic frame, NativeDesktopVisualLayoutResult result)
        {
            JArray? items = null;
            try { items = frame is JObject objectFrame ? objectFrame["Items"] as JArray : frame.Items as JArray; }
            catch { }

            var paths = new List<string>();
            foreach (JObject item in items?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                if (!ReferenceFrameItem.IsNativeDesktopIcon(item)) continue;
                string path = ReferenceFrameItem.GetOriginalPath(item);
                if (!ReferenceFrameItem.IsSupportedPath(path))
                {
                    result.SkippedInvalidPath++;
                    continue;
                }
                paths.Add(NormalizePath(path));
            }
            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IReadOnlyList<NativeDesktopIconPlacement> BuildPlacements(
            IReadOnlyList<string> paths, int left, int top, int right, int bottom, int gridX, int gridY)
        {
            var placements = new List<NativeDesktopIconPlacement>();
            int firstX = RoundUp(left, gridX);
            int firstY = RoundUp(top, gridY);
            for (int y = firstY; y + gridY <= bottom && placements.Count < paths.Count; y += gridY)
            {
                for (int x = firstX; x + gridX <= right && placements.Count < paths.Count; x += gridX)
                {
                    placements.Add(new NativeDesktopIconPlacement(paths[placements.Count], x, y));
                }
            }
            return placements;
        }

        private static int CountSlots(int left, int top, int right, int bottom, int gridX, int gridY)
        {
            int horizontal = Math.Max(0, ((right - RoundUp(left, gridX)) / gridX));
            int vertical = Math.Max(0, ((bottom - RoundUp(top, gridY)) / gridY));
            return horizontal * vertical;
        }

        private static int RoundUp(int value, int grid) => ((value + grid - 1) / grid) * grid;

        private static string NormalizePath(string path)
        {
            try { return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar); }
            catch { return path?.Trim() ?? string.Empty; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rectangle);
    }

    internal sealed record NativeDesktopIconPlacement(string OriginalPath, int X, int Y);

    internal sealed class NativeDesktopVisualLayoutResult
    {
        public bool Applied { get; set; }
        public string Reason { get; set; } = string.Empty;
        public int Positioned { get; set; }
        public int SkippedInvalidPath { get; set; }
        public int RequiredCapacity { get; set; }
        public int AvailableSlots { get; set; }
        public bool AutoArrangeEnabled { get; set; }
        public bool AlignToGridEnabled { get; set; }
    }
}
