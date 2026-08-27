using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames.ReferenceGrouping
{
    /// <summary>
    /// The visual owner is explicit and persisted per reference. Existing
    /// references deliberately default to FrameRendered for compatibility.
    /// </summary>
    internal static class VisualOwnershipModes
    {
        public const string FrameRendered = "FrameRendered";
        public const string NativeDesktopIcon = "NativeDesktopIcon";

        public static bool NativeVisualGroupingEnabled { get; set; } = true;

        public static string Normalize(string? mode) =>
            string.Equals(mode, NativeDesktopIcon, StringComparison.OrdinalIgnoreCase)
                ? NativeDesktopIcon
                : FrameRendered;

        public static bool IsNativeFrame(dynamic frame)
        {
            if (!NativeVisualGroupingEnabled || frame == null) return false;
            try
            {
                if (frame is not JObject nativeFrameObject) return false;
                if (nativeFrameObject["NativeVisualGroupingEnabled"]?.Type == JTokenType.Boolean &&
                    nativeFrameObject["NativeVisualGroupingEnabled"]!.Value<bool>() == false)
                    return false;
                return ContainsOnlyNativeItems(nativeFrameObject["Items"] as JArray);
            }
            catch { return false; }
        }

        private static bool ContainsOnlyNativeItems(JArray? items)
        {
            var references = items?.OfType<JObject>().Where(ReferenceFrameItem.IsReference).ToList();
            return references != null && references.Count > 0 && references.All(ReferenceFrameItem.IsNativeDesktopIcon);
        }
    }
}
