using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>Resolution result, including the candidates behind an ambiguous label.</summary>
    public sealed record ResolvedDesktopIconIdentity(
        string ExplorerLabel,
        int LabelOrdinal,
        int ExplorerLabelCount,
        NativeIconIdentityStatus Status,
        string Identity,
        string OriginalPath,
        NativeDesktopCandidateType? CandidateType,
        IReadOnlyList<NativeDesktopIconCandidate> Candidates)
    {
        public IReadOnlyList<string> CandidatePaths => Candidates.Select(candidate => candidate.OriginalPath).ToArray();
    }

    public sealed record NativeDesktopIconCandidate(
        string OriginalPath,
        NativeDesktopCandidateType ItemType);

    /// <summary>
    /// Stage A only: resolves Explorer display labels to filesystem candidates.
    /// It never classifies candidates and never owns a layout GroupId.
    /// </summary>
    public sealed class NativeDesktopIconIdentityResolver
    {
        private readonly Dictionary<string, List<NativeDesktopIconCandidate>> _candidatesByDisplayName;

        public NativeDesktopIconIdentityResolver(IEnumerable<string>? desktopRoots = null)
        {
            IEnumerable<string> roots = desktopRoots ?? GetDesktopRoots();
            _candidatesByDisplayName = BuildIndex(roots);
        }

        public (NativeIconIdentityStatus Status, string Identity, string OriginalPath) Resolve(string displayName)
        {
            var resolved = ResolveDetailed(displayName, 1, 0);
            return (resolved.Status, resolved.Identity, resolved.OriginalPath);
        }

        /// <summary>
        /// Resolves one ListView item. Ordinal pairing is allowed only when the
        /// Explorer label count exactly matches the deterministic candidate count.
        /// A mismatch fails closed and returns no OriginalPath.
        /// </summary>
        public ResolvedDesktopIconIdentity ResolveDetailed(
            string displayName,
            int explorerLabelCount,
            int labelOrdinal)
        {
            string label = NormalizeLabel(displayName);
            if (label.Length == 0)
            {
                return NewResult(displayName, labelOrdinal, explorerLabelCount,
                    NativeIconIdentityStatus.Unresolved, $"unresolved-display:{displayName}",
                    string.Empty, null, Array.Empty<NativeDesktopIconCandidate>());
            }

            if (!_candidatesByDisplayName.TryGetValue(label, out List<NativeDesktopIconCandidate>? indexed))
            {
                return NewResult(displayName, labelOrdinal, explorerLabelCount,
                    NativeIconIdentityStatus.ShellNamespace, $"shell-display:{displayName}",
                    string.Empty, null, Array.Empty<NativeDesktopIconCandidate>());
            }

            NativeDesktopIconCandidate[] candidates = indexed
                .GroupBy(candidate => NormalizePath(candidate.OriginalPath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(candidate => CandidateTypeRank(candidate.ItemType))
                .ThenBy(candidate => NormalizePath(candidate.OriginalPath), StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => NormalizePath(candidate.OriginalPath), StringComparer.Ordinal)
                .ToArray();

            // A file is indexed under its full name and again under its name
            // without the extension, so identities still resolve when Explorer
            // hides known extensions. The cost is that "Report.zip" also answers
            // to the label "Report": put a folder named "Report" beside it while
            // Explorer IS showing extensions, and Explorer reports one icon
            // called "Report" while the index offers two candidates. The count
            // mismatch failed closed, so the folder lost its identity, dropped
            // out of its group, and - being unmanaged - became an obstacle that
            // pushed the whole rail inward.
            //
            // An exact filename match is stronger evidence than an
            // extension-stripped alias. When the exact matches alone account for
            // what Explorer reported, they are the answer. Otherwise the full
            // alias set stands, which is what lets a folder and a same-named
            // .url - whose extension Explorer always hides - still pair up by
            // ordinal.
            NativeDesktopIconCandidate[] exactNameMatches = candidates
                .Where(candidate => string.Equals(
                    Path.GetFileName(NormalizePath(candidate.OriginalPath)),
                    label,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (exactNameMatches.Length > 0 && exactNameMatches.Length == explorerLabelCount)
                candidates = exactNameMatches;

            if (explorerLabelCount <= 0 || explorerLabelCount != candidates.Length ||
                labelOrdinal < 0 || labelOrdinal >= explorerLabelCount)
            {
                return NewResult(displayName, labelOrdinal, explorerLabelCount,
                    NativeIconIdentityStatus.Ambiguous, $"ambiguous-display:{displayName}",
                    string.Empty, null, candidates);
            }

            NativeDesktopIconCandidate selected = candidates[labelOrdinal];
            NativeIconIdentityStatus status = candidates.Length == 1
                ? NativeIconIdentityStatus.Exact
                : NativeIconIdentityStatus.OrdinalResolved;
            return NewResult(displayName, labelOrdinal, explorerLabelCount, status,
                $"path:{NormalizePath(selected.OriginalPath)}", selected.OriginalPath,
                selected.ItemType, candidates);
        }

        private static ResolvedDesktopIconIdentity NewResult(
            string explorerLabel,
            int labelOrdinal,
            int explorerLabelCount,
            NativeIconIdentityStatus status,
            string identity,
            string originalPath,
            NativeDesktopCandidateType? candidateType,
            IReadOnlyList<NativeDesktopIconCandidate> candidates) =>
            new(explorerLabel, labelOrdinal, explorerLabelCount, status, identity,
                originalPath, candidateType, candidates);

        private static Dictionary<string, List<NativeDesktopIconCandidate>> BuildIndex(IEnumerable<string> roots)
        {
            var result = new Dictionary<string, List<NativeDesktopIconCandidate>>(StringComparer.OrdinalIgnoreCase);
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

                    var candidate = new NativeDesktopIconCandidate(NormalizePath(path), GetCandidateType(path));
                    foreach (string alias in GetDisplayAliases(path))
                    {
                        string key = NormalizeLabel(alias);
                        if (key.Length == 0) continue;
                        if (!result.TryGetValue(key, out List<NativeDesktopIconCandidate>? paths))
                        {
                            paths = new List<NativeDesktopIconCandidate>();
                            result[key] = paths;
                        }
                        paths.Add(candidate);
                    }
                }
            }
            return result;
        }

        private static NativeDesktopCandidateType GetCandidateType(string path)
        {
            if (Directory.Exists(path)) return NativeDesktopCandidateType.Directory;
            string extension = Path.GetExtension(path);
            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                return NativeDesktopCandidateType.LinkShortcut;
            if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
                return NativeDesktopCandidateType.InternetShortcut;
            return NativeDesktopCandidateType.RegularFile;
        }

        private static int CandidateTypeRank(NativeDesktopCandidateType type) => type switch
        {
            NativeDesktopCandidateType.Directory => 0,
            NativeDesktopCandidateType.LinkShortcut => 1,
            NativeDesktopCandidateType.InternetShortcut => 2,
            NativeDesktopCandidateType.RegularFile => 3,
            _ => int.MaxValue
        };

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
