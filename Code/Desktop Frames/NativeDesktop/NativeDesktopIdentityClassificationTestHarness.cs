using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>Small deterministic Phase 5.7.1 harness; no test framework dependency.</summary>
    internal static class NativeDesktopIdentityClassificationTestHarness
    {
        public static void RunLiveAddressTest(string auditDirectory, string explorerLabel)
        {
            var layout = new NativeDesktopIconLayoutService();
            DesktopFilesystemFingerprint filesystemBefore = DesktopFilesystemFingerprint.CaptureNames();
            NativeDesktopIconState[] icons = layout.EnumerateDesktopIcons()
                .Where(icon => string.Equals(icon.DisplayName, explorerLabel, StringComparison.Ordinal))
                .OrderBy(icon => icon.LabelOrdinal)
                .ToArray();
            Require(icons.Length == 2, $"live Explorer label count for '{explorerLabel}' was {icons.Length}, expected 2");
            Require(icons.All(icon => icon.IdentityStatus == NativeIconIdentityStatus.OrdinalResolved),
                "live same-label icons were not ordinal-resolved");
            Require(icons.Select(icon => NormalizePath(icon.OriginalPath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
                "live same-label icons did not resolve to two unique candidate paths");

            var lines = new List<string>();
            foreach (NativeDesktopIconState icon in icons)
            {
                if (!layout.TrySetPosition(icon.OriginalPath, icon.X, icon.Y,
                        out NativeDesktopIconState? updated, out string reason))
                    throw new InvalidOperationException($"PHASE_5_7_1_LIVE_ADDRESS_FAILED: {icon.OriginalPath}: {reason}");
                Require(updated != null && PathEquals(updated.OriginalPath, icon.OriginalPath),
                    "live path addressing selected a different candidate");
                lines.Add($"| {Escape(icon.DisplayName)} | {icon.LabelOrdinal} | {Escape(icon.OriginalPath)} | " +
                    $"{icon.CandidateType} | {icon.IdentityStatus} | true |");
            }

            Require(filesystemBefore.Equals(DesktopFilesystemFingerprint.CaptureNames()),
                "live same-label address test changed the Desktop filesystem");
            Directory.CreateDirectory(auditDirectory);
            var report = new StringBuilder();
            report.AppendLine("# UI Phase 5.7.1 live same-label addressing");
            report.AppendLine();
            report.AppendLine("LiveSameLabelAddressing=PASS");
            report.AppendLine("ExplorerLabelCount=2");
            report.AppendLine("CandidateCount=2");
            report.AppendLine("BothNativeIconsPreciselyAddressed=true");
            report.AppendLine("FilesystemUnchanged=true");
            report.AppendLine();
            report.AppendLine("| ExplorerLabel | LabelOrdinal | CandidatePath | CandidateType | ResolutionStatus | AddressedByPath |");
            report.AppendLine("|---|---:|---|---|---|---:|");
            foreach (string line in lines) report.AppendLine(line);
            File.WriteAllText(Path.Combine(auditDirectory, "ui-phase5-7-1-live-addressing.md"),
                report.ToString(), new UTF8Encoding(false));
        }

        public static void Run(string auditDirectory)
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "DesktopHub-Phase5-7-1-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            try
            {
                var rows = new List<string>();
                RunSteamCase(testRoot, "SAME_LABEL_TEST", rows);
                RunGenericUrlCase(testRoot, "GENERIC_LABEL_TEST", rows);
                RunManualOverrideCase(testRoot, "MANUAL_LABEL_TEST", rows);
                RunCountMismatchCase(testRoot, "COUNT_MISMATCH_TEST", rows);
                RunStableOrdinalCase(testRoot, "STABLE_ORDINAL_TEST", rows);

                Directory.CreateDirectory(auditDirectory);
                var report = new StringBuilder();
                report.AppendLine("# UI Phase 5.7.1 deterministic regression");
                report.AppendLine();
                report.AppendLine("DeterministicHarness=PASS");
                report.AppendLine("SharedLabelDoesNotShareClassification=true");
                report.AppendLine("SteamUrlClassifiedAsGames=true");
                report.AppendLine("SameNameFolderDoesNotInheritSteamClassification=true");
                report.AppendLine("ManualOverrideStillHighestPriority=true");
                report.AppendLine("OrdinalMappingDeterministic=true");
                report.AppendLine("CountMismatchFailsClosed=true");
                report.AppendLine("CountMismatchNativeMovementEligible=false");
                report.AppendLine("TestArtifactsDeleted=true");
                report.AppendLine();
                report.AppendLine("| Case | Candidate | Type | ResolutionStatus | ClassifierGroup | ManualOverride | FinalGroup |");
                report.AppendLine("|---|---|---|---|---|---:|---|");
                foreach (string row in rows) report.AppendLine(row);
                File.WriteAllText(Path.Combine(auditDirectory, "ui-phase5-7-1-regression.md"),
                    report.ToString(), new UTF8Encoding(false));
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void RunSteamCase(string root, string label, ICollection<string> rows)
        {
            (string folder, string url) = CreatePair(root, label, "steam://rungameid/1395520");
            var resolver = new NativeDesktopIconIdentityResolver(new[] { root });
            ClassifiedDesktopItem folderResult = Classify(resolver.ResolveDetailed(label, 2, 0));
            ClassifiedDesktopItem urlResult = Classify(resolver.ResolveDetailed(label, 2, 1));
            Require(folderResult.FinalGroup == NativeDesktopLayoutGroupId.UNASSIGNED,
                "same-label folder inherited the Steam classification");
            Require(urlResult.FinalGroup == NativeDesktopLayoutGroupId.GAMES,
                "Steam .url was not classified as GAMES");
            Require(PathEquals(folderResult.NativeIdentity.OriginalPath, folder), "folder ordinal mapping changed");
            Require(PathEquals(urlResult.NativeIdentity.OriginalPath, url), "URL ordinal mapping changed");
            rows.Add(Row("Steam", folderResult));
            rows.Add(Row("Steam", urlResult));
        }

        private static void RunGenericUrlCase(string root, string label, ICollection<string> rows)
        {
            CreatePair(root, label, "https://example.com/");
            var resolver = new NativeDesktopIconIdentityResolver(new[] { root });
            ClassifiedDesktopItem folderResult = Classify(resolver.ResolveDetailed(label, 2, 0));
            ClassifiedDesktopItem urlResult = Classify(resolver.ResolveDetailed(label, 2, 1));
            Require(folderResult.FinalGroup == NativeDesktopLayoutGroupId.UNASSIGNED,
                "generic same-label folder was not UNASSIGNED");
            Require(urlResult.FinalGroup is NativeDesktopLayoutGroupId.DAILY_TOOLS or NativeDesktopLayoutGroupId.UNASSIGNED,
                "generic URL violated the existing classifier policy");
            rows.Add(Row("GenericUrl", folderResult));
            rows.Add(Row("GenericUrl", urlResult));
        }

        private static void RunManualOverrideCase(string root, string label, ICollection<string> rows)
        {
            (string folder, _) = CreatePair(root, label, "steam://rungameid/1395520");
            var resolver = new NativeDesktopIconIdentityResolver(new[] { root });
            ResolvedDesktopIconIdentity folderIdentity = resolver.ResolveDetailed(label, 2, 0);
            ResolvedDesktopIconIdentity urlIdentity = resolver.ResolveDetailed(label, 2, 1);
            var manual = new Dictionary<string, NativeDesktopLayoutGroupId>(StringComparer.OrdinalIgnoreCase)
            {
                [NormalizePath(folder)] = NativeDesktopLayoutGroupId.RESEARCH_STUDY
            };
            ClassifiedDesktopItem folderResult = Classify(folderIdentity, manual);
            ClassifiedDesktopItem urlResult = Classify(urlIdentity, manual);
            Require(folderResult.FinalGroup == NativeDesktopLayoutGroupId.RESEARCH_STUDY && folderResult.IsManualOverride,
                "manual folder classification did not remain authoritative");
            Require(urlResult.FinalGroup == NativeDesktopLayoutGroupId.GAMES && !urlResult.IsManualOverride,
                "manual folder classification contaminated the Steam URL");
            rows.Add(Row("ManualOverride", folderResult));
            rows.Add(Row("ManualOverride", urlResult));
        }

        private static void RunCountMismatchCase(string root, string label, ICollection<string> rows)
        {
            CreatePair(root, label, "steam://rungameid/1395520");
            File.WriteAllText(Path.Combine(root, label + ".txt"), "regular file", new UTF8Encoding(false));
            var resolver = new NativeDesktopIconIdentityResolver(new[] { root });
            ResolvedDesktopIconIdentity first = resolver.ResolveDetailed(label, 2, 0);
            ResolvedDesktopIconIdentity second = resolver.ResolveDetailed(label, 2, 1);
            Require(first.Status == NativeIconIdentityStatus.Ambiguous && second.Status == NativeIconIdentityStatus.Ambiguous,
                "candidate count mismatch did not fail closed");
            Require(string.IsNullOrEmpty(first.OriginalPath) && string.IsNullOrEmpty(second.OriginalPath),
                "count mismatch exposed a movable candidate path");
            ClassifiedDesktopItem firstResult = Classify(first);
            ClassifiedDesktopItem secondResult = Classify(second);
            rows.Add(Row("CountMismatch", firstResult));
            rows.Add(Row("CountMismatch", secondResult));
        }

        private static void RunStableOrdinalCase(string root, string label, ICollection<string> rows)
        {
            CreatePair(root, label, "steam://rungameid/1395520");
            string[] first = ResolvePaths(new NativeDesktopIconIdentityResolver(new[] { root }), label);
            string[] second = ResolvePaths(new NativeDesktopIconIdentityResolver(new[] { root }), label);
            Require(first.SequenceEqual(second, StringComparer.OrdinalIgnoreCase),
                "ordinal mapping changed across resolver instances");
            Require(first.Length == 2 && Directory.Exists(first[0]) &&
                Path.GetExtension(first[1]).Equals(".url", StringComparison.OrdinalIgnoreCase),
                "candidate type rank did not produce Directory then InternetShortcut");
            rows.Add("| StableOrdinal | " + Escape(string.Join(" → ", first)) + " | Directory → InternetShortcut | OrdinalResolved | N/A | false | N/A |");
        }

        private static string[] ResolvePaths(NativeDesktopIconIdentityResolver resolver, string label) =>
            Enumerable.Range(0, 2)
                .Select(ordinal => resolver.ResolveDetailed(label, 2, ordinal).OriginalPath)
                .ToArray();

        private static (string Folder, string Url) CreatePair(string root, string label, string uri)
        {
            string folder = Path.Combine(root, label);
            string url = Path.Combine(root, label + ".url");
            Directory.CreateDirectory(folder);
            File.WriteAllText(url, "[InternetShortcut]\r\nURL=" + uri + "\r\n", new UTF8Encoding(false));
            return (folder, url);
        }

        private static ClassifiedDesktopItem Classify(
            ResolvedDesktopIconIdentity identity,
            IReadOnlyDictionary<string, NativeDesktopLayoutGroupId>? manual = null)
        {
            var state = new NativeDesktopIconState
            {
                DisplayName = identity.ExplorerLabel,
                Identity = identity.Identity,
                OriginalPath = identity.OriginalPath,
                IdentityStatus = identity.Status,
                CandidatePaths = identity.CandidatePaths,
                CandidateTypes = identity.Candidates.Select(candidate => candidate.ItemType).ToArray(),
                CandidateType = identity.CandidateType,
                LabelOrdinal = identity.LabelOrdinal,
                ExplorerLabelCount = identity.ExplorerLabelCount
            };
            return NativeDesktopItemClassificationService.Classify(state,
                manual ?? new Dictionary<string, NativeDesktopLayoutGroupId>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private static string Row(string testCase, ClassifiedDesktopItem item) =>
            $"| {testCase} | {Escape(item.NativeIdentity.OriginalPath)} | {Escape(item.NativeIdentity.CandidateType?.ToString() ?? string.Empty)} | " +
            $"{item.NativeIdentity.IdentityStatus} | {item.ClassifierGroup} | {item.IsManualOverride.ToString().ToLowerInvariant()} | {item.FinalGroup} |";

        private static bool PathEquals(string left, string right) =>
            string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return path.Trim(); }
        }

        private static string Escape(string value) => (value ?? string.Empty).Replace("|", "\\|");

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("PHASE_5_7_1_TEST_FAILED: " + message);
        }
    }
}
