using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// Resolves Explorer display labels back to visible filesystem entries on
    /// the user and public Desktop. Ambiguous labels deliberately fail closed.
    /// Runtime ListView indexes are never treated as persistent identities.
    /// </summary>
    /// <summary>Resolution result, including the candidates behind an ambiguous label.</summary>
    public sealed record ResolvedDesktopIconIdentity(
        NativeIconIdentityStatus Status,
        string Identity,
        string OriginalPath,
        IReadOnlyList<string> CandidatePaths);

    public sealed class NativeDesktopIconIdentityResolver
    {
        private readonly Dictionary<string, List<string>> _pathsByDisplayName;

        public NativeDesktopIconIdentityResolver(IEnumerable<string>? desktopRoots = null)
        {
            IEnumerable<string> roots = desktopRoots ?? GetDesktopRoots();
            _pathsByDisplayName = BuildIndex(roots);
        }

        public (NativeIconIdentityStatus Status, string Identity, string OriginalPath) Resolve(string displayName)
        {
            var resolved = ResolveDetailed(displayName);
            return (resolved.Status, resolved.Identity, resolved.OriginalPath);
        }

        /// <summary>
        /// Resolve, plus the paths that made a label ambiguous.
        ///
        /// Failing closed on an ambiguous label is right for anything that needs
        /// to know WHICH file an icon is. It is needlessly strict for the one
        /// question that matters to grouping: if every candidate behind a label
        /// belongs in the same group, the ambiguity does not change the answer,
        /// and the caller can act on it.
        /// </summary>
        public ResolvedDesktopIconIdentity ResolveDetailed(string displayName)
        {
            string label = NormalizeLabel(displayName);
            if (label.Length == 0 || !_pathsByDisplayName.TryGetValue(label, out List<string>? candidates))
            {
                return new ResolvedDesktopIconIdentity(NativeIconIdentityStatus.UnmanagedShellItem,
                    $"shell-display:{displayName}", string.Empty, Array.Empty<string>());
            }

            string[] unique = candidates
                .Select(NormalizePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (unique.Length != 1)
            {
                return new ResolvedDesktopIconIdentity(NativeIconIdentityStatus.AmbiguousDisplayName,
                    $"ambiguous-display:{displayName}", string.Empty, unique);
            }

            return new ResolvedDesktopIconIdentity(NativeIconIdentityStatus.StablePath,
                $"path:{unique[0]}", unique[0], unique);
        }

        private static Dictionary<string, List<string>> BuildIndex(IEnumerable<string> roots)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                IEnumerable<string> entries;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (string path in entries)
                {
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(path);
                        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (string alias in GetDisplayAliases(path))
                    {
                        string key = NormalizeLabel(alias);
                        if (key.Length == 0) continue;
                        if (!result.TryGetValue(key, out List<string>? paths))
                        {
                            paths = new List<string>();
                            result[key] = paths;
                        }
                        paths.Add(path);
                    }
                }
            }
            return result;
        }

        private static IEnumerable<string> GetDisplayAliases(string path)
        {
            bool isDirectory = Directory.Exists(path);
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fileName = Path.GetFileName(trimmed);
            if (string.IsNullOrWhiteSpace(fileName)) yield break;

            yield return fileName;
            if (!isDirectory)
            {
                string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
                if (!string.Equals(withoutExtension, fileName, StringComparison.OrdinalIgnoreCase))
                    yield return withoutExtension;
            }
        }

        private static IEnumerable<string> GetDesktopRoots()
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.IsNullOrWhiteSpace(common)) yield return common;
        }

        private static string NormalizeLabel(string? value) => (value ?? string.Empty).Trim();

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim();
            }
        }
    }
}
