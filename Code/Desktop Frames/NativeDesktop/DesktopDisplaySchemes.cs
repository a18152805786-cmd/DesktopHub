using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// A display the desktop has been laid out on: the Desktop ListView's size in
    /// physical pixels and its DPI.
    /// </summary>
    internal readonly record struct DesktopDisplayKey(int Width, int Height, int Dpi)
    {
        private static readonly Regex Pattern = new(@"^(\d{3,5})x(\d{3,5})@(\d{2,3})$", RegexOptions.CultureInvariant);

        public int ScalePercent => (int)Math.Round(Dpi * 100 / 96.0, MidpointRounding.AwayFromZero);

        /// <summary>The key as it appears in file names, e.g. 2560x1440@96.</summary>
        public override string ToString() => $"{Width}x{Height}@{Dpi}";

        public static bool TryParse(string? text, out DesktopDisplayKey key)
        {
            key = default;
            Match match = Pattern.Match(text?.Trim() ?? string.Empty);
            if (!match.Success) return false;
            key = new DesktopDisplayKey(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
            return true;
        }

        public static DesktopDisplayKey Of(DesktopLayoutPlan plan) =>
            new(plan.ListViewClientWidth, plan.ListViewClientHeight, plan.Dpi);
    }

    internal sealed class DesktopDisplaySchemeSettings
    {
        /// <summary>Re-apply the matching layout on its own when the display changes.</summary>
        public bool AutoApplyOnDisplayChange { get; set; } = true;

        /// <summary>Names the user gave displays, by key.</summary>
        public Dictionary<string, string> Labels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One desktop layout scheme per display.
    ///
    /// Manual zone decisions are grid slots, and a slot only means something on
    /// the display it was chosen on: column 28 of the 33-column monitor does not
    /// exist on the laptop's 22 columns. Kept in one file, arranging the laptop
    /// overwrote the monitor's arrangement and the other way round. Each display
    /// now keeps its own zone file, and whichever display Explorer is showing
    /// decides which file is live.
    /// </summary>
    internal static class DesktopDisplaySchemes
    {
        private const string LegacyZoneFile = "native-desktop-group-anchors.json";
        private const string ZoneFilePrefix = "native-desktop-group-anchors.";
        private const string ZoneFileSuffix = ".json";
        private const string MigratedLegacyFile = "native-desktop-group-anchors.before-display-schemes.json";
        private const string SettingsFileName = "native-desktop-display-schemes.json";

        private static readonly object SyncRoot = new();
        private static DesktopDisplayKey? _current;

        /// <summary>The display the live scheme belongs to, once it has been read.</summary>
        public static DesktopDisplayKey? Current
        {
            get { lock (SyncRoot) return _current; }
        }

        /// <summary>Reads the display Explorer is showing and makes its scheme the live one.</summary>
        public static DesktopDisplayKey Refresh()
        {
            var layout = new NativeDesktopIconLayoutService();
            NativeDesktopListViewState state = layout.GetListViewState();
            NativeDesktopSafeGrid grid = layout.GetSafeDesktopGrid();
            var key = new DesktopDisplayKey(grid.ClientWidth, grid.ClientHeight, DesktopChromeDpi.WindowDpi(state.Handle));
            lock (SyncRoot)
            {
                MigrateLegacyFile(key);
                if (_current != key)
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        $"Desktop layout scheme: {key}");
                _current = key;
            }
            return key;
        }

        /// <summary>Zone file of the live scheme, reading the display first if it has not been read yet.</summary>
        public static string CurrentZoneFile()
        {
            DesktopDisplayKey key = Current ?? Refresh();
            lock (SyncRoot)
            {
                // A profile switch can bring a profile that still has the old single file.
                MigrateLegacyFile(key);
                return ZoneFile(key);
            }
        }

        public static string ZoneFile(DesktopDisplayKey key) =>
            Path.Combine(ProfileManager.CurrentProfileDir, ZoneFilePrefix + key + ZoneFileSuffix);

        /// <summary>
        /// Every display with a scheme in the current profile, plus the live one,
        /// the live one first.
        /// </summary>
        public static IReadOnlyList<DesktopDisplayKey> Known()
        {
            var keys = new HashSet<DesktopDisplayKey>();
            foreach (string path in Directory.EnumerateFiles(ProfileManager.CurrentProfileDir,
                         ZoneFilePrefix + "*" + ZoneFileSuffix))
            {
                string name = Path.GetFileName(path);
                string middle = name.Substring(ZoneFilePrefix.Length, name.Length - ZoneFilePrefix.Length - ZoneFileSuffix.Length);
                if (DesktopDisplayKey.TryParse(middle, out DesktopDisplayKey key)) keys.Add(key);
            }
            DesktopDisplayKey? current = Current;
            if (current.HasValue) keys.Add(current.Value);
            return keys
                .OrderByDescending(key => key == current)
                .ThenByDescending(key => key.Width * key.Height)
                .ThenBy(key => key.Dpi)
                .ToList();
        }

        /// <summary>"2560×1440 · 100%", followed by the user's name for it when there is one.</summary>
        public static string Describe(DesktopDisplayKey key)
        {
            string size = $"{key.Width}×{key.Height} · {key.ScalePercent}%";
            return LoadSettings().Labels.TryGetValue(key.ToString(), out string? label) && !string.IsNullOrWhiteSpace(label)
                ? Localization.Strings.Get("DisplaySchemeWithLabel", size, label.Trim())
                : size;
        }

        public static bool AutoApplyOnDisplayChange
        {
            get => LoadSettings().AutoApplyOnDisplayChange;
            set
            {
                DesktopDisplaySchemeSettings settings = LoadSettings();
                settings.AutoApplyOnDisplayChange = value;
                SaveSettings(settings);
            }
        }

        private static DesktopDisplaySchemeSettings LoadSettings()
        {
            try
            {
                string path = Path.Combine(ProfileManager.CurrentProfileDir, SettingsFileName);
                if (!File.Exists(path)) return new DesktopDisplaySchemeSettings();
                DesktopDisplaySchemeSettings? settings =
                    JsonConvert.DeserializeObject<DesktopDisplaySchemeSettings>(File.ReadAllText(path, Encoding.UTF8));
                if (settings == null) return new DesktopDisplaySchemeSettings();
                settings.Labels = new Dictionary<string, string>(settings.Labels ?? new(), StringComparer.OrdinalIgnoreCase);
                return settings;
            }
            catch
            {
                // Unreadable settings fall back to the defaults rather than blocking layout.
                return new DesktopDisplaySchemeSettings();
            }
        }

        private static void SaveSettings(DesktopDisplaySchemeSettings settings)
        {
            string path = Path.Combine(ProfileManager.CurrentProfileDir, SettingsFileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(settings, Formatting.Indented), new UTF8Encoding(false));
        }

        /// <summary>
        /// Hands the old single zone file to the display that is live when it is
        /// first seen - the display the user has been arranging on - and keeps the
        /// original beside it under a name nothing reads.
        /// </summary>
        private static void MigrateLegacyFile(DesktopDisplayKey key)
        {
            try
            {
                string directory = ProfileManager.CurrentProfileDir;
                string legacy = Path.Combine(directory, LegacyZoneFile);
                if (!File.Exists(legacy)) return;
                string target = ZoneFile(key);
                if (!File.Exists(target)) File.Copy(legacy, target);
                File.Move(legacy, Path.Combine(directory, MigratedLegacyFile), overwrite: true);
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"Desktop zone file moved to the scheme of {key}.");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Desktop zone file was not migrated: {ex.Message}");
            }
        }
    }
}
