using IWshRuntimeLibrary;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;

namespace Desktop_Frames.ReferenceGrouping
{
    internal enum ReferenceType
    {
        Shortcut,
        Url,
        Folder
    }

    /// <summary>
    /// Reference-only metadata for a Desktop shortcut, URL, or folder.
    /// Filename intentionally mirrors OriginalPath for compatibility with the
    /// existing Frame renderer; the original object remains the source of truth.
    /// </summary>
    internal sealed class ReferenceFrameItem
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string OriginalPath { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public ReferenceType ReferenceType { get; init; }
        public string TargetPath { get; init; } = string.Empty;
        public string Arguments { get; init; } = string.Empty;
        public string WorkingDirectory { get; init; } = string.Empty;
        public string IconLocation { get; init; } = string.Empty;
        public string Uri { get; init; } = string.Empty;
        public string FrameId { get; init; } = string.Empty;
        public string IconSource { get; init; } = string.Empty;
        public string ManualFrameId { get; init; } = string.Empty;

        public static bool IsSupportedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (Directory.Exists(path)) return true;
            string extension = Path.GetExtension(path);
            return System.IO.File.Exists(path) &&
                (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".url", StringComparison.OrdinalIgnoreCase));
        }

        public static ReferenceFrameItem Create(string path, string frameId = "")
        {
            string originalPath = Path.GetFullPath(path);
            if (!IsSupportedPath(originalPath))
                throw new ArgumentException("Only existing .lnk, .url, and folder paths can be referenced.", nameof(path));

            if (Directory.Exists(originalPath))
            {
                return new ReferenceFrameItem
                {
                    OriginalPath = originalPath,
                    DisplayName = GetDisplayName(originalPath, true),
                    ReferenceType = ReferenceType.Folder,
                    TargetPath = originalPath,
                    FrameId = frameId,
                    IconSource = originalPath
                };
            }

            if (Path.GetExtension(originalPath).Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                return new ReferenceFrameItem
                {
                    OriginalPath = originalPath,
                    DisplayName = GetDisplayName(originalPath, false),
                    ReferenceType = ReferenceType.Url,
                    Uri = ReadUrl(originalPath),
                    FrameId = frameId,
                    IconSource = originalPath
                };
            }

            string targetPath = string.Empty;
            string arguments = string.Empty;
            string workingDirectory = string.Empty;
            string iconLocation = string.Empty;
            try
            {
                var shell = new WshShell();
                var shortcut = (IWshShortcut)shell.CreateShortcut(originalPath);
                targetPath = shortcut.TargetPath ?? string.Empty;
                arguments = shortcut.Arguments ?? string.Empty;
                workingDirectory = shortcut.WorkingDirectory ?? string.Empty;
                iconLocation = shortcut.IconLocation ?? string.Empty;
            }
            catch
            {
                targetPath = Utility.GetShortcutTarget(originalPath) ?? string.Empty;
                arguments = Utility.GetShortcutArguments(originalPath) ?? string.Empty;
            }

            return new ReferenceFrameItem
            {
                OriginalPath = originalPath,
                DisplayName = GetDisplayName(originalPath, false),
                ReferenceType = ReferenceType.Shortcut,
                TargetPath = targetPath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                IconLocation = iconLocation,
                FrameId = frameId,
                IconSource = originalPath
            };
        }

        public JObject ToJson(int displayOrder = 0)
        {
            bool isFolder = ReferenceType == ReferenceType.Folder;
            return new JObject
            {
                ["Id"] = Id,
                ["ItemKind"] = "Reference",
                ["ReferenceType"] = ReferenceType.ToString(),
                ["OriginalPath"] = OriginalPath,
                ["DisplayName"] = DisplayName,
                ["TargetPath"] = TargetPath,
                ["Arguments"] = Arguments,
                ["WorkingDirectory"] = WorkingDirectory,
                ["IconLocation"] = IconLocation,
                ["Uri"] = Uri,
                ["FrameId"] = FrameId,
                ["IconSource"] = IconSource,
                ["ManualFrameId"] = ManualFrameId,

                // Compatibility aliases consumed by the existing renderer.
                ["Filename"] = OriginalPath,
                ["IsFolder"] = isFolder,
                ["IsLink"] = ReferenceType == ReferenceType.Url,
                ["IsNetwork"] = IsNetworkPath(OriginalPath),
                ["AlwaysRunAsAdmin"] = false,
                ["DisplayOrder"] = displayOrder
            };
        }

        public static bool IsReference(JToken? item) =>
            string.Equals(item?["ItemKind"]?.ToString(), "Reference", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item?["OriginalPath"]?.ToString());

        public static string GetOriginalPath(JToken? item) =>
            item?["OriginalPath"]?.ToString() ?? item?["Filename"]?.ToString() ?? string.Empty;

        private static string GetDisplayName(string path, bool folder)
        {
            string value = folder
                ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(value) ? path : value;
        }

        private static string ReadUrl(string path)
        {
            try
            {
                string? line = System.IO.File.ReadLines(path)
                    .FirstOrDefault(value => value.StartsWith("URL=", StringComparison.OrdinalIgnoreCase));
                return line == null ? string.Empty : line.Substring(4).Trim();
            }
            catch { return string.Empty; }
        }

        private static bool IsNetworkPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }
    }
}
