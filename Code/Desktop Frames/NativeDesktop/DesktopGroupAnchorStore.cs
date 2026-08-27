using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// A grid position the user chose by dragging a group header. Stored in
    /// grid slots, not pixels, so it survives DPI and resolution changes.
    /// </summary>
    public sealed class DesktopGroupAnchor
    {
        /// <summary>Grid column of the group's first icon column.</summary>
        public int Column { get; set; }

        /// <summary>Grid row of the group's first icon row.</summary>
        public int Row { get; set; }

        /// <summary>Rail widening the group had when the user pinned it, so its shape survives.</summary>
        public int ExtraColumns { get; set; }

        /// <summary>When true the header refuses to drag, guarding against a stray click.</summary>
        public bool Locked { get; set; }

        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    internal sealed class DesktopGroupAnchorDocument
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, DesktopGroupAnchor> Groups { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Persists manual group positions. A manual anchor outranks the automatic
    /// Dual-Rail planner on every later refresh and restart.
    /// </summary>
    internal static class DesktopGroupAnchorStore
    {
        private const string FileName = "native-desktop-group-anchors.json";
        private static readonly object SyncRoot = new();

        private static string Path => System.IO.Path.Combine(ProfileManager.CurrentProfileDir, FileName);

        public static IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupAnchor> Load()
        {
            var result = new Dictionary<NativeDesktopLayoutGroupId, DesktopGroupAnchor>();
            try
            {
                lock (SyncRoot)
                {
                    string path = Path;
                    if (!File.Exists(path)) return result;
                    DesktopGroupAnchorDocument? document =
                        JsonConvert.DeserializeObject<DesktopGroupAnchorDocument>(File.ReadAllText(path, Encoding.UTF8));
                    foreach (KeyValuePair<string, DesktopGroupAnchor> pair in document?.Groups ?? new())
                        if (Enum.TryParse(pair.Key, true, out NativeDesktopLayoutGroupId group) && pair.Value != null)
                            result[group] = pair.Value;
                }
            }
            catch
            {
                // A malformed anchor file must never block planning; the
                // automatic Dual-Rail position is always a safe fallback.
            }
            return result;
        }

        public static void Save(NativeDesktopLayoutGroupId group, int column, int row, bool locked, int extraColumns)
        {
            lock (SyncRoot)
            {
                DesktopGroupAnchorDocument document = Read();
                document.Groups[group.ToString()] = new DesktopGroupAnchor
                {
                    Column = column,
                    Row = row,
                    ExtraColumns = extraColumns,
                    Locked = locked,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
                Write(document);
            }
        }

        public static void SetLocked(NativeDesktopLayoutGroupId group, bool locked)
        {
            lock (SyncRoot)
            {
                DesktopGroupAnchorDocument document = Read();
                if (!document.Groups.TryGetValue(group.ToString(), out DesktopGroupAnchor? anchor) || anchor == null)
                    return;
                anchor.Locked = locked;
                anchor.UpdatedUtc = DateTimeOffset.UtcNow;
                Write(document);
            }
        }

        public static bool IsLocked(NativeDesktopLayoutGroupId group) =>
            Load().TryGetValue(group, out DesktopGroupAnchor? anchor) && anchor.Locked;

        /// <summary>Drops the manual anchor so the group returns to its planned Dual-Rail position.</summary>
        public static void Clear(NativeDesktopLayoutGroupId group)
        {
            lock (SyncRoot)
            {
                DesktopGroupAnchorDocument document = Read();
                if (!document.Groups.Remove(group.ToString())) return;
                Write(document);
            }
        }

        private static DesktopGroupAnchorDocument Read()
        {
            try
            {
                string path = Path;
                if (!File.Exists(path)) return new DesktopGroupAnchorDocument();
                return JsonConvert.DeserializeObject<DesktopGroupAnchorDocument>(File.ReadAllText(path, Encoding.UTF8))
                    ?? new DesktopGroupAnchorDocument();
            }
            catch
            {
                return new DesktopGroupAnchorDocument();
            }
        }

        private static void Write(DesktopGroupAnchorDocument document)
        {
            string path = Path;
            string? directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonConvert.SerializeObject(document, Formatting.Indented), new UTF8Encoding(false));
        }
    }
}
