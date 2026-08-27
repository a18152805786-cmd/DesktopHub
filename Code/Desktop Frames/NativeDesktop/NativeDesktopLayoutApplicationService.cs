using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// Manual command/API surface for Phase 5.3. It has no startup auto-apply,
    /// watcher, timer, mouse hook, or Frame dependency.
    /// </summary>
    internal static class NativeDesktopLayoutApplicationService
    {
        public const bool NativeDesktopLayoutAutoApply = false;
        private const string PlanJsonName = "ui-phase5-3-layout-plan.json";
        private const string BaselineName = "ui-phase5-3-desktop-layout-before.json";

        public static bool IsNativeLayoutCommand(IEnumerable<string> args) =>
            args.Any(arg => arg.StartsWith("--native-layout-", StringComparison.OrdinalIgnoreCase));

        public static int Run(IEnumerable<string> rawArgs)
        {
            string[] args = rawArgs.ToArray();
            string auditDirectory = ExtractValue(args, "--native-layout-audit=")
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audit");
            Directory.CreateDirectory(auditDirectory);

            try
            {
                if (args.Any(arg => arg.Equals("--native-layout-preview", StringComparison.OrdinalIgnoreCase)))
                {
                    DesktopLayoutPlan plan = CreateAndWritePreview(auditDirectory);
                    WriteResults(auditDirectory, $"Preview=PASS; StableIconCount={plan.Items.Count(item => item.IsStableIdentity)}");
                    return 0;
                }

                if (args.Any(arg => arg.Equals("--native-layout-refresh", StringComparison.OrdinalIgnoreCase)))
                {
                    DesktopLayoutPlan plan = CreateAndWritePreview(auditDirectory);
                    foreach (NativeDesktopLayoutGroupId group in new[]
                    {
                        NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS,
                        NativeDesktopLayoutGroupId.AI_DEV,
                        NativeDesktopLayoutGroupId.RESEARCH_STUDY
                    })
                    {
                        NativeDesktopLayoutBatchResult result = Apply(auditDirectory, plan, group);
                        if (!result.Applied)
                            throw new InvalidOperationException($"Refresh stopped at {group}: {result.Reason}");
                    }
                    return 0;
                }

                string? apply = ExtractValue(args, "--native-layout-apply=");
                if (!string.IsNullOrWhiteSpace(apply))
                {
                    if (!Enum.TryParse(apply, true, out NativeDesktopLayoutGroupId group) ||
                        group == NativeDesktopLayoutGroupId.UNASSIGNED)
                        throw new ArgumentException("--native-layout-apply must name a managed NativeDesktopLayoutGroupId.");
                    DesktopLayoutPlan plan = LoadOrCreatePlan(auditDirectory);
                    NativeDesktopLayoutBatchResult result = Apply(auditDirectory, plan, group);
                    if (!result.Applied)
                        throw new InvalidOperationException($"Apply stopped at {group}: {result.Reason}");
                    return 0;
                }

                if (args.Any(arg => arg.Equals("--native-layout-restore", StringComparison.OrdinalIgnoreCase)))
                {
                    RestoreOriginalDesktopLayout(auditDirectory);
                    return 0;
                }

                WriteResults(auditDirectory, "Status=NO_NATIVE_LAYOUT_ACTION");
                return 2;
            }
            catch (Exception ex)
            {
                WriteResults(auditDirectory, "Status=FAIL\nError=" + ex);
                return 1;
            }
        }

        public static DesktopLayoutPlan CreateAndWritePreview(string auditDirectory)
        {
            Directory.CreateDirectory(auditDirectory);
            var metadata = new NativeDesktopLayoutMetadataStore();
            ManualLayoutResolution manual = metadata.LoadManualResolution();
            var layout = new NativeDesktopIconLayoutService();
            var planner = new NativeDesktopLayoutPlanner(layout, manual.Groups, manual.UnmappedManualPaths);
            DesktopLayoutPlan plan = planner.CreatePlan();
            string jsonPath = Path.Combine(auditDirectory, PlanJsonName);
            File.WriteAllText(jsonPath, JsonConvert.SerializeObject(plan, Formatting.Indented), new UTF8Encoding(false));
            WritePreview(Path.Combine(auditDirectory, "ui-phase5-3-native-layout-preview.md"), plan);
            WriteVisualPlan(Path.Combine(auditDirectory, "ui-phase5-3-layout-plan.md"), plan);
            return plan;
        }

        public static NativeDesktopLayoutBatchResult ApplyNativeDesktopLayout(string auditDirectory, NativeDesktopLayoutGroupId group) =>
            Apply(auditDirectory, LoadOrCreatePlan(auditDirectory), group);

        public static NativeDesktopRestoreResult RestoreOriginalDesktopLayout(string auditDirectory)
        {
            string baselinePath = Path.Combine(auditDirectory, BaselineName);
            if (!File.Exists(baselinePath)) throw new FileNotFoundException("Phase 5.3 baseline was not found.", baselinePath);
            var layout = new NativeDesktopIconLayoutService();
            NativeDesktopLayoutSnapshot baseline = NativeDesktopIconLayoutService.LoadSnapshot(baselinePath);
            NativeDesktopSnapshotValidationResult validation = layout.ValidateSnapshot(baseline);
            if (validation.AutoArrangeEnabled || validation.Missing != 0 || validation.Ambiguous != 0)
                throw new InvalidOperationException($"RESTORE_DRY_RUN_FAILED: CanResolve={validation.CanResolve}; Missing={validation.Missing}; Ambiguous={validation.Ambiguous}; AutoArrange={validation.AutoArrangeEnabled}");

            NativeDesktopRestoreResult result = layout.Restore(baseline);
            if (result.Failed != 0) throw new InvalidOperationException("RESTORE_FAILED: " + string.Join("; ", result.Errors));
            WriteRestore(Path.Combine(auditDirectory, "ui-phase5-3-restore-results.md"), validation, result);
            return result;
        }

        private static DesktopLayoutPlan LoadOrCreatePlan(string auditDirectory)
        {
            string path = Path.Combine(auditDirectory, PlanJsonName);
            if (!File.Exists(path)) return CreateAndWritePreview(auditDirectory);
            return JsonConvert.DeserializeObject<DesktopLayoutPlan>(File.ReadAllText(path, Encoding.UTF8))
                ?? throw new InvalidDataException("Native Desktop layout plan is empty or invalid.");
        }

        private static NativeDesktopLayoutBatchResult Apply(string auditDirectory, DesktopLayoutPlan plan, NativeDesktopLayoutGroupId group)
        {
            string batchName = BatchName(group);
            var result = new NativeDesktopLayoutBatchResult { BatchName = batchName, GroupId = group };
            if (plan.AutoArrangeEnabled)
            {
                result.Reason = "AUTO_ARRANGE_ENABLED";
                WriteBatch(auditDirectory, result, Array.Empty<DesktopLayoutItem>(), Array.Empty<NativeDesktopIconSnapshotItem>(), Array.Empty<NativeDesktopIconSnapshotItem>());
                return result;
            }
            if (plan.DuplicateTargetCoordinates != 0 || plan.TargetOutsideDesktopWorkArea != 0)
            {
                result.Reason = "INVALID_PLAN_COORDINATES";
                return result;
            }

            List<DesktopLayoutItem> candidates = plan.Items
                .Where(item => string.Equals(item.LayoutGroupId, group.ToString(), StringComparison.OrdinalIgnoreCase) && item.IsEligibleForApply)
                .ToList();
            result.Intended = candidates.Count;
            if (candidates.Count == 0)
            {
                result.Applied = true;
                result.FilesystemUnchanged = true;
                result.Reason = "ALREADY_APPLIED_OR_NO_ELIGIBLE_ITEMS";
                WriteBatch(auditDirectory, result, candidates, Array.Empty<NativeDesktopIconSnapshotItem>(), Array.Empty<NativeDesktopIconSnapshotItem>());
                WriteResults(auditDirectory, $"{batchName}=PASS\nGroup={group}\nItemsMoved=0\nReason={result.Reason}\nFilesystemUnchanged=True\nOtherUnmanagedIconsChanged=False");
                return result;
            }

            var layout = new NativeDesktopIconLayoutService();
            NativeDesktopListViewState state = layout.GetListViewState();
            if (state.AutoArrangeEnabled)
            {
                result.Reason = "AUTO_ARRANGE_ENABLED";
                return result;
            }

            string baselinePath = Path.Combine(auditDirectory, BaselineName);
            if (!File.Exists(baselinePath))
                NativeDesktopIconLayoutService.SaveSnapshot(layout.CaptureSnapshot(), baselinePath);
            NativeDesktopLayoutSnapshot baseline = NativeDesktopIconLayoutService.LoadSnapshot(baselinePath);
            NativeDesktopSnapshotValidationResult validation = layout.ValidateSnapshot(baseline);
            if (validation.AutoArrangeEnabled || validation.Missing != 0 || validation.Ambiguous != 0)
            {
                result.Reason = $"SNAPSHOT_DRY_RUN_FAILED CanResolve={validation.CanResolve}; Missing={validation.Missing}; Ambiguous={validation.Ambiguous}";
                return result;
            }

            NativeDesktopLayoutSnapshot before = layout.CaptureSnapshot();
            DesktopFilesystemFingerprint filesystemBefore = DesktopFilesystemFingerprint.Capture();
            if (!TargetsAreVacant(layout.EnumerateDesktopIcons(), candidates, plan, state))
            {
                result.Reason = "TARGET_SLOT_OCCUPIED";
                WriteBatch(auditDirectory, result, candidates, before.Icons, before.Icons);
                return result;
            }

            foreach (DesktopLayoutItem candidate in candidates)
            {
                if (!layout.TrySetPosition(candidate.OriginalPath, candidate.TargetX, candidate.TargetY, out _, out string reason))
                {
                    result.Errors.Add($"{candidate.DisplayName}: {reason}");
                    NativeDesktopRestoreResult rollback = layout.Restore(before);
                    result.Reason = rollback.Failed == 0 ? "APPLY_FAILED_ROLLED_BACK" : "APPLY_FAILED_ROLLBACK_FAILED";
                    WriteBatch(auditDirectory, result, candidates, before.Icons, layout.CaptureSnapshot().Icons);
                    return result;
                }
                result.Moved++;
            }

            NativeDesktopLayoutSnapshot after = layout.CaptureSnapshot();
            result.OtherUnmanagedIconsChanged = OtherIconsChanged(before, after, candidates, plan, state);
            result.FilesystemUnchanged = filesystemBefore.Equals(DesktopFilesystemFingerprint.Capture());
            bool targetsVerified = TargetsVerified(after, candidates, plan, state);
            if (result.OtherUnmanagedIconsChanged || !result.FilesystemUnchanged || !targetsVerified)
            {
                NativeDesktopRestoreResult rollback = layout.Restore(before);
                result.Reason = rollback.Failed == 0 ? "POST_APPLY_VERIFICATION_FAILED_ROLLED_BACK" : "POST_APPLY_VERIFICATION_FAILED_ROLLBACK_FAILED";
                WriteBatch(auditDirectory, result, candidates, before.Icons, layout.CaptureSnapshot().Icons);
                return result;
            }

            new NativeDesktopLayoutMetadataStore().PersistLayoutGroups(candidates);
            result.Applied = true;
            result.Reason = "APPLIED";
            WriteBatch(auditDirectory, result, candidates, before.Icons, after.Icons);
            WriteResults(auditDirectory, $"{batchName}=PASS\nGroup={group}\nItemsMoved={result.Moved}\nSnapshotDryRun=PASS CanResolve={validation.CanResolve}\nFilesystemUnchanged={result.FilesystemUnchanged}\nOtherUnmanagedIconsChanged={result.OtherUnmanagedIconsChanged}");
            return result;
        }

        private static bool TargetsAreVacant(IReadOnlyList<NativeDesktopIconState> current, IReadOnlyList<DesktopLayoutItem> candidates,
            DesktopLayoutPlan plan, NativeDesktopListViewState state)
        {
            Drawing.Rectangle work = new(plan.WorkAreaLeft, plan.WorkAreaTop, plan.WorkAreaWidth, plan.WorkAreaHeight);
            var candidatePaths = candidates.Select(item => NormalizePath(item.OriginalPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (DesktopLayoutItem candidate in candidates)
            {
                string target = NativeDesktopLayoutPlanner.SlotKey(candidate.TargetX, candidate.TargetY, work, Math.Max(1, state.GridSpacingX), Math.Max(1, state.GridSpacingY));
                if (current.Any(icon => !candidatePaths.Contains(NormalizePath(icon.OriginalPath)) &&
                    NativeDesktopLayoutPlanner.SlotKey(icon.X, icon.Y, work, Math.Max(1, state.GridSpacingX), Math.Max(1, state.GridSpacingY)) == target))
                    return false;
            }
            return true;
        }

        private static bool TargetsVerified(NativeDesktopLayoutSnapshot after, IReadOnlyList<DesktopLayoutItem> candidates,
            DesktopLayoutPlan plan, NativeDesktopListViewState state)
        {
            Drawing.Rectangle work = new(plan.WorkAreaLeft, plan.WorkAreaTop, plan.WorkAreaWidth, plan.WorkAreaHeight);
            var current = after.Icons.Where(item => item.IdentityStatus == NativeIconIdentityStatus.StablePath)
                .ToDictionary(item => NormalizePath(item.OriginalPath), StringComparer.OrdinalIgnoreCase);
            foreach (DesktopLayoutItem candidate in candidates)
            {
                if (!current.TryGetValue(NormalizePath(candidate.OriginalPath), out NativeDesktopIconSnapshotItem? item)) return false;
                string actual = NativeDesktopLayoutPlanner.SlotKey(item.X, item.Y, work, Math.Max(1, state.GridSpacingX), Math.Max(1, state.GridSpacingY));
                string target = NativeDesktopLayoutPlanner.SlotKey(candidate.TargetX, candidate.TargetY, work, Math.Max(1, state.GridSpacingX), Math.Max(1, state.GridSpacingY));
                if (!string.Equals(actual, target, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private static bool OtherIconsChanged(NativeDesktopLayoutSnapshot before, NativeDesktopLayoutSnapshot after,
            IReadOnlyList<DesktopLayoutItem> candidates, DesktopLayoutPlan plan, NativeDesktopListViewState state)
        {
            Drawing.Rectangle work = new(plan.WorkAreaLeft, plan.WorkAreaTop, plan.WorkAreaWidth, plan.WorkAreaHeight);
            var selected = candidates.Select(item => NormalizePath(item.OriginalPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var afterByKey = after.Icons.Select((item, index) => new { item, index })
                .ToDictionary(entry => SnapshotKey(entry.item, entry.index), entry => entry.item, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in before.Icons.Select((item, index) => new { item, index }))
            {
                if (entry.item.IdentityStatus == NativeIconIdentityStatus.StablePath && selected.Contains(NormalizePath(entry.item.OriginalPath))) continue;
                if (!afterByKey.TryGetValue(SnapshotKey(entry.item, entry.index), out NativeDesktopIconSnapshotItem? now)) return true;
                string oldSlot = NativeDesktopLayoutPlanner.SlotKey(entry.item.X, entry.item.Y, work, Math.Max(1, state.GridSpacingX), Math.Max(1, state.GridSpacingY));
                string newSlot = NativeDesktopLayoutPlanner.SlotKey(now.X, now.Y, work, Math.Max(1, state.GridSpacingX), Math.Max(1, state.GridSpacingY));
                if (!string.Equals(oldSlot, newSlot, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void WritePreview(string path, DesktopLayoutPlan plan)
        {
            var markdown = new StringBuilder();
            markdown.AppendLine("# UI Phase 5.3 Native Desktop Layout Preview");
            markdown.AppendLine();
            markdown.AppendLine($"- GeneratedUtc: `{plan.CreatedUtc:O}`");
            markdown.AppendLine($"- DesktopWorkArea: `{plan.WorkAreaLeft},{plan.WorkAreaTop} {plan.WorkAreaWidth}x{plan.WorkAreaHeight}`");
            markdown.AppendLine($"- DPI: `{plan.Dpi}`; IconGrid: `{plan.GridSpacingX}x{plan.GridSpacingY}`");
            markdown.AppendLine($"- AutoArrangeEnabled: `{plan.AutoArrangeEnabled}`; AlignToGridEnabled: `{plan.AlignToGridEnabled}`");
            markdown.AppendLine();
            markdown.AppendLine("| Name | Type | OriginalPath | CurrentX | CurrentY | ProposedGroup | Confidence | Reason | ProposedTargetX | ProposedTargetY |");
            markdown.AppendLine("|---|---|---|---:|---:|---|---|---|---:|---:|");
            foreach (DesktopLayoutItem item in plan.Items.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                markdown.AppendLine($"| {Cell(item.DisplayName)} | {Cell(item.Type)} | {Cell(item.OriginalPath)} | {item.CurrentX} | {item.CurrentY} | {Cell(item.GroupDisplayName)} | {Cell(item.ClassificationConfidence)} | {Cell(item.ClassificationReason)} | {item.TargetX} | {item.TargetY} |");
            File.WriteAllText(path, markdown.ToString(), new UTF8Encoding(false));
        }

        private static void WriteVisualPlan(string path, DesktopLayoutPlan plan)
        {
            var markdown = new StringBuilder("# UI Phase 5.3 Visual Layout Plan\n\n");
            foreach (DesktopLayoutGroup group in plan.Groups)
            {
                markdown.AppendLine($"## {group.GroupDisplayName}\n");
                markdown.AppendLine($"- Region: `{group.Region}`");
                markdown.AppendLine($"- Classified: `{group.PlannedItemCount}`; eligible HIGH-confidence moves: `{group.EligibleItemCount}`");
                foreach (DesktopLayoutItem item in plan.Items.Where(item => item.LayoutGroupId == group.GroupId.ToString())
                    .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                    markdown.AppendLine($"- `{item.DisplayName}` → `({item.TargetX}, {item.TargetY})` — {item.ApplyReason}");
                markdown.AppendLine();
            }
            markdown.AppendLine($"- DuplicateTargetCoordinates: `{plan.DuplicateTargetCoordinates}`");
            markdown.AppendLine($"- TargetOutsideDesktopWorkArea: `{plan.TargetOutsideDesktopWorkArea}`");
            File.WriteAllText(path, markdown.ToString(), new UTF8Encoding(false));
        }

        private static void WriteBatch(string auditDirectory, NativeDesktopLayoutBatchResult result, IReadOnlyList<DesktopLayoutItem> candidates,
            IReadOnlyList<NativeDesktopIconSnapshotItem> before, IReadOnlyList<NativeDesktopIconSnapshotItem> after)
        {
            var afterByPath = after.Where(item => item.IdentityStatus == NativeIconIdentityStatus.StablePath)
                .GroupBy(item => NormalizePath(item.OriginalPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var beforeByPath = before.Where(item => item.IdentityStatus == NativeIconIdentityStatus.StablePath)
                .GroupBy(item => NormalizePath(item.OriginalPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var csv = new StringBuilder("Name,BeforeX,BeforeY,AfterX,AfterY\r\n");
            foreach (DesktopLayoutItem item in candidates)
            {
                beforeByPath.TryGetValue(NormalizePath(item.OriginalPath), out NativeDesktopIconSnapshotItem? oldItem);
                afterByPath.TryGetValue(NormalizePath(item.OriginalPath), out NativeDesktopIconSnapshotItem? newItem);
                csv.Append(Csv(item.DisplayName)).Append(',').Append(oldItem?.X ?? item.CurrentX).Append(',').Append(oldItem?.Y ?? item.CurrentY)
                    .Append(',').Append(newItem?.X ?? item.CurrentX).Append(',').Append(newItem?.Y ?? item.CurrentY).Append("\r\n");
            }
            File.WriteAllText(Path.Combine(auditDirectory, $"ui-phase5-3-{result.BatchName.ToLowerInvariant()}.csv"), csv.ToString(), new UTF8Encoding(false));
        }

        private static void WriteRestore(string path, NativeDesktopSnapshotValidationResult validation, NativeDesktopRestoreResult result)
        {
            File.WriteAllText(path, $"# UI Phase 5.3 Restore Results\n\nCanResolve={validation.CanResolve}\n\nMissing={validation.Missing}\n\nAmbiguous={validation.Ambiguous}\n\nRestored={result.Restored}\n\nFailed={result.Failed}\n", new UTF8Encoding(false));
        }

        private static void WriteResults(string auditDirectory, string content) =>
            File.WriteAllText(Path.Combine(auditDirectory, "ui-phase5-3-layout-results.md"), "# UI Phase 5.3 Native Layout Results\n\n" + content + "\n", new UTF8Encoding(false));

        private static string BatchName(NativeDesktopLayoutGroupId group) => group switch
        {
            NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS => "batch-a",
            NativeDesktopLayoutGroupId.AI_DEV => "batch-b",
            NativeDesktopLayoutGroupId.RESEARCH_STUDY => "batch-c",
            NativeDesktopLayoutGroupId.DAILY_TOOLS => "batch-d",
            NativeDesktopLayoutGroupId.GAMES => "batch-e",
            _ => "batch"
        };

        private static string? ExtractValue(IEnumerable<string> args, string prefix) => args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..].Trim('"');
        private static string SnapshotKey(NativeDesktopIconSnapshotItem item, int index) => $"{item.IdentityStatus}|{NormalizePath(item.OriginalPath)}|{item.DisplayName}|{index}";
        private static string Cell(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        private static string NormalizePath(string path) { try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); } catch { return path?.Trim() ?? string.Empty; } }
    }

    internal sealed class ManualLayoutResolution
    {
        public Dictionary<string, NativeDesktopLayoutGroupId> Groups { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> UnmappedManualPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class NativeDesktopLayoutMetadataStore
    {
        private const string FileName = "native-desktop-layout-metadata.json";
        private readonly string _path = Path.Combine(ProfileManager.CurrentProfileDir, FileName);

        public ManualLayoutResolution LoadManualResolution()
        {
            var resolution = new ManualLayoutResolution();
            if (File.Exists(_path))
            {
                NativeDesktopLayoutMetadataDocument? document = JsonConvert.DeserializeObject<NativeDesktopLayoutMetadataDocument>(File.ReadAllText(_path, Encoding.UTF8));
                foreach (var pair in document?.Items ?? new Dictionary<string, NativeDesktopLayoutMetadata>())
                    if (pair.Value.IsManualOverride && Enum.TryParse(pair.Value.LayoutGroupId, true, out NativeDesktopLayoutGroupId group))
                        resolution.Groups[NormalizePath(pair.Key)] = group;
            }

            string framesPath = ProfileManager.GetProfileFilePath("frames.json");
            if (!File.Exists(framesPath)) return resolution;
            try
            {
                JArray frames = JArray.Parse(File.ReadAllText(framesPath, Encoding.UTF8));
                foreach (JObject frame in frames.OfType<JObject>())
                {
                    string title = frame["Title"]?.ToString() ?? string.Empty;
                    foreach (JObject item in (frame["Items"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                    {
                        string path = NormalizePath(item["OriginalPath"]?.ToString() ?? item["Filename"]?.ToString() ?? string.Empty);
                        if (string.IsNullOrWhiteSpace(path)) continue;
                        string manualGroup = item["ManualLayoutGroupId"]?.ToString() ?? string.Empty;
                        if (Enum.TryParse(manualGroup, true, out NativeDesktopLayoutGroupId explicitGroup)) resolution.Groups[path] = explicitGroup;
                        else if (!string.IsNullOrWhiteSpace(item["ManualFrameId"]?.ToString()))
                        {
                            NativeDesktopLayoutGroupId? mapped = MapFrameTitle(title);
                            if (mapped.HasValue) resolution.Groups[path] = mapped.Value;
                            else resolution.UnmappedManualPaths.Add(path);
                        }
                    }
                }
            }
            catch { /* A malformed legacy frame file must never enable a move. */ }
            return resolution;
        }

        public void PersistLayoutGroups(IEnumerable<DesktopLayoutItem> items)
        {
            var document = File.Exists(_path)
                ? JsonConvert.DeserializeObject<NativeDesktopLayoutMetadataDocument>(File.ReadAllText(_path, Encoding.UTF8)) ?? new NativeDesktopLayoutMetadataDocument()
                : new NativeDesktopLayoutMetadataDocument();
            foreach (DesktopLayoutItem item in items)
            {
                string path = NormalizePath(item.OriginalPath);
                document.Items[path] = new NativeDesktopLayoutMetadata
                {
                    LayoutGroupId = item.LayoutGroupId,
                    IsManualOverride = item.IsManualOverride,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
            }
            File.WriteAllText(_path, JsonConvert.SerializeObject(document, Formatting.Indented), new UTF8Encoding(false));
        }

        private static NativeDesktopLayoutGroupId? MapFrameTitle(string title)
        {
            string value = title.Trim();
            if (value.Contains("科研") || value.Contains("研究")) return NativeDesktopLayoutGroupId.RESEARCH_STUDY;
            if (value.Contains("AI", StringComparison.OrdinalIgnoreCase) || value.Contains("开发")) return NativeDesktopLayoutGroupId.AI_DEV;
            if (value.Contains("办公") || value.Contains("文档")) return NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS;
            if (value.Contains("日常")) return NativeDesktopLayoutGroupId.DAILY_TOOLS;
            if (value.Contains("游戏")) return NativeDesktopLayoutGroupId.GAMES;
            if (value.Contains("系统") || value.Contains("临时")) return NativeDesktopLayoutGroupId.SYSTEM_TEMP;
            return null;
        }

        private static string NormalizePath(string path) { try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); } catch { return path?.Trim() ?? string.Empty; } }
    }

    internal sealed class NativeDesktopLayoutMetadataDocument
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, NativeDesktopLayoutMetadata> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class NativeDesktopLayoutMetadata
    {
        public string LayoutGroupId { get; set; } = string.Empty;
        public bool IsManualOverride { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }

    internal sealed class DesktopFilesystemFingerprint : IEquatable<DesktopFilesystemFingerprint>
    {
        private readonly List<string> _entries;
        private DesktopFilesystemFingerprint(IEnumerable<string> entries) => _entries = entries.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        public static DesktopFilesystemFingerprint Capture()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            IEnumerable<string> entries = Directory.Exists(desktop)
                ? Directory.EnumerateFileSystemEntries(desktop, "*", SearchOption.TopDirectoryOnly).Select(path =>
                {
                    FileAttributes attributes = File.GetAttributes(path);
                    DateTime lastWrite = File.GetLastWriteTimeUtc(path);
                    long length = File.Exists(path) ? new FileInfo(path).Length : -1;
                    return $"{Path.GetFileName(path)}|{attributes}|{length}|{lastWrite.Ticks}";
                })
                : Enumerable.Empty<string>();
            return new DesktopFilesystemFingerprint(entries);
        }
        public bool Equals(DesktopFilesystemFingerprint? other) => other != null && _entries.SequenceEqual(other._entries, StringComparer.OrdinalIgnoreCase);
        public override bool Equals(object? obj) => Equals(obj as DesktopFilesystemFingerprint);
        public override int GetHashCode() => _entries.Count;
    }
}
