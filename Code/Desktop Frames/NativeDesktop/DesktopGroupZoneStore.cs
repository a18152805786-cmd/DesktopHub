using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// What the user has decided by hand about one group's zone: where it sits
    /// and how big it is.
    ///
    /// Both are stored in GRID SLOTS, never pixels. Explorer's icon cell is
    /// around 75x100 on this desktop but that is a runtime value, so a stored
    /// "Width=376" would silently become wrong the moment the DPI, the icon
    /// size or the resolution changed. Columns and rows survive all of them.
    /// </summary>
    public sealed class DesktopGroupZone
    {
        /// <summary>Grid column of the group's first icon column.</summary>
        public int Column { get; set; }

        /// <summary>Grid row of the group's first icon row.</summary>
        public int Row { get; set; }

        /// <summary>True when the user placed this group; the planner must not move it.</summary>
        public bool IsManualPosition { get; set; }

        /// <summary>Zone width in grid columns, when the user has chosen one.</summary>
        public int ManualColumns { get; set; }

        /// <summary>Zone height in grid rows, when the user has chosen one.</summary>
        public int ManualRows { get; set; }

        /// <summary>True when the user resized this group; automatic sizing must not override it.</summary>
        public bool IsManualSize { get; set; }

        /// <summary>Rail widening the group had when it was pinned, so an auto-sized shape survives.</summary>
        public int ExtraColumns { get; set; }

        /// <summary>Locked zones refuse to drag and show no resize handles.</summary>
        public bool Locked { get; set; }

        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Derived, so it is never written to the file.</summary>
        [JsonIgnore]
        public bool HasAnything => IsManualPosition || IsManualSize || Locked;
    }

    internal sealed class DesktopGroupZoneDocument
    {
        public int Version { get; set; } = 2;
        public Dictionary<string, DesktopGroupZone> Groups { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Persists manual zone position and size. Anything stored here outranks the
    /// automatic Dual-Rail planner on every later refresh and restart, which is
    /// the whole point: a refresh must never undo something the user did on
    /// purpose.
    /// </summary>
    internal static class DesktopGroupZoneStore
    {
        private const string FileName = "native-desktop-group-anchors.json";
        private static readonly object SyncRoot = new();

        private static string Path => System.IO.Path.Combine(ProfileManager.CurrentProfileDir, FileName);

        public static IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> Load()
        {
            var result = new Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone>();
            try
            {
                lock (SyncRoot)
                {
                    foreach (KeyValuePair<string, DesktopGroupZone> pair in Read().Groups)
                        if (Enum.TryParse(pair.Key, true, out NativeDesktopLayoutGroupId group) && pair.Value != null)
                            result[group] = pair.Value;
                }
            }
            catch
            {
                // A malformed file must never block planning; the automatic
                // Dual-Rail position is always a safe fallback.
            }
            return result;
        }

        public static DesktopGroupZone Get(NativeDesktopLayoutGroupId group) =>
            Load().TryGetValue(group, out DesktopGroupZone? zone) && zone != null
                ? zone
                : new DesktopGroupZone();

        /// <summary>Records the position the user dragged a group to.</summary>
        public static void SavePosition(NativeDesktopLayoutGroupId group, int column, int row, int extraColumns)
        {
            Update(group, zone =>
            {
                zone.Column = column;
                zone.Row = row;
                zone.ExtraColumns = extraColumns;
                zone.IsManualPosition = true;
            });
        }

        /// <summary>Records the size the user resized a group to.</summary>
        public static void SaveSize(NativeDesktopLayoutGroupId group, int columns, int rows)
        {
            Update(group, zone =>
            {
                zone.ManualColumns = Math.Max(1, columns);
                zone.ManualRows = Math.Max(1, rows);
                zone.IsManualSize = true;
            });
        }

        public static void SetLocked(NativeDesktopLayoutGroupId group, bool locked) =>
            Update(group, zone => zone.Locked = locked);

        public static bool IsLocked(NativeDesktopLayoutGroupId group) => Get(group).Locked;

        /// <summary>
        /// Captures the exact file before a resize commit, including absence.
        /// Bind the path now so a rollback can never target a different profile.
        /// </summary>
        internal static Action CaptureRollback()
        {
            lock (SyncRoot)
            {
                string path = Path;
                byte[]? contents = File.Exists(path) ? File.ReadAllBytes(path) : null;
                return () =>
                {
                    lock (SyncRoot)
                    {
                        if (contents == null) File.Delete(path);
                        else if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(contents))
                            File.WriteAllBytes(path, contents);
                    }
                };
            }
        }

        /// <summary>Hands the group's position back to the automatic planner.</summary>
        public static void ClearPosition(NativeDesktopLayoutGroupId group) =>
            Update(group, zone =>
            {
                zone.IsManualPosition = false;
                zone.Column = 0;
                zone.Row = 0;
                zone.ExtraColumns = 0;
            });

        /// <summary>Hands the group's size back to automatic sizing.</summary>
        public static void ClearSize(NativeDesktopLayoutGroupId group) =>
            Update(group, zone =>
            {
                zone.IsManualSize = false;
                zone.ManualColumns = 0;
                zone.ManualRows = 0;
            });

        private static void Update(NativeDesktopLayoutGroupId group, Action<DesktopGroupZone> mutate)
        {
            lock (SyncRoot)
            {
                DesktopGroupZoneDocument document = Read();
                string key = group.ToString();
                if (!document.Groups.TryGetValue(key, out DesktopGroupZone? zone) || zone == null)
                {
                    zone = new DesktopGroupZone();
                    document.Groups[key] = zone;
                }
                mutate(zone);
                zone.UpdatedUtc = DateTimeOffset.UtcNow;
                // An entry that no longer says anything is removed rather than
                // left behind as an empty record.
                if (!zone.HasAnything) document.Groups.Remove(key);
                Write(document);
            }
        }

        private static DesktopGroupZoneDocument Read()
        {
            try
            {
                string path = Path;
                if (!File.Exists(path)) return new DesktopGroupZoneDocument();
                DesktopGroupZoneDocument document =
                    JsonConvert.DeserializeObject<DesktopGroupZoneDocument>(File.ReadAllText(path, Encoding.UTF8))
                    ?? new DesktopGroupZoneDocument();
                Migrate(document);
                return document;
            }
            catch
            {
                return new DesktopGroupZoneDocument();
            }
        }

        /// <summary>
        /// Version 1 stored a position and nothing else, so an entry existing at
        /// all meant the user had dragged that group. Read it that way rather
        /// than losing the placement.
        /// </summary>
        private static void Migrate(DesktopGroupZoneDocument document)
        {
            if (document.Version >= 2) return;
            foreach (DesktopGroupZone zone in document.Groups.Values)
                zone.IsManualPosition = true;
            document.Version = 2;
        }

        private static void Write(DesktopGroupZoneDocument document)
        {
            string path = Path;
            string? directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonConvert.SerializeObject(document, Formatting.Indented), new UTF8Encoding(false));
        }
    }
}
