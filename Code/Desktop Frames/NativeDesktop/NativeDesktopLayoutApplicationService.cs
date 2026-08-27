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
using Desktop_Frames.Search;

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
        private const string CompactPlanJsonName = "ui-phase5-3b-plan.json";
        private const string CompactBaselineName = "ui-phase5-3b-before.json";

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
                if (args.Any(arg => arg.Equals("--native-layout-compact-preview", StringComparison.OrdinalIgnoreCase)))
                {
                    DesktopLayoutPlan plan = CreateAndWriteCompactPreview(auditDirectory);
                    WriteCompactResults(auditDirectory, $"Preview=PASS\nStableIconCount={plan.Items.Count(item => item.IsStableIdentity)}");
                    return 0;
                }

                if (args.Any(arg => arg.Equals("--native-layout-compact-refresh", StringComparison.OrdinalIgnoreCase)))
                {
                    DesktopLayoutPlan plan = CreateAndWriteCompactPreview(auditDirectory);
                    foreach (NativeDesktopLayoutGroupId group in new[]
                    {
                        NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS,
                        NativeDesktopLayoutGroupId.AI_DEV,
                        NativeDesktopLayoutGroupId.RESEARCH_STUDY
                    })
                    {
                        NativeDesktopLayoutBatchResult result = Apply(auditDirectory, plan, group,
                            CompactBaselineName, "ui-phase5-3b");
                        if (!result.Applied)
                            throw new InvalidOperationException($"Compact refresh stopped at {group}: {result.Reason}");
                    }
                    return 0;
                }

                if (args.Any(arg => arg.Equals("--native-layout-compact-restore", StringComparison.OrdinalIgnoreCase)))
                {
                    RestoreSnapshot(auditDirectory, CompactBaselineName, "ui-phase5-3b-restore.md");
                    return 0;
                }

                if (args.Any(arg => arg.Equals("--native-layout-clean-empty-default-frame", StringComparison.OrdinalIgnoreCase)))
                {
                    string? framesPath = ExtractValue(args, "--native-layout-frames=");
                    EmptyDefaultFrameCleanupResult result = CleanupEmptyDefaultFrame(auditDirectory, framesPath);
                    WriteCompactResults(auditDirectory, result.ToAuditText());
                    return result.Removed || !result.Found ? 0 : 1;
                }

                if (args.Any(arg => arg.Equals("--native-layout-search-smoke", StringComparison.OrdinalIgnoreCase)))
                {
                    RunSearchSmoke(auditDirectory);
                    return 0;
                }

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

        public static DesktopLayoutPlan CreateAndWriteCompactPreview(string auditDirectory)
        {
            Directory.CreateDirectory(auditDirectory);
            var layout = new NativeDesktopIconLayoutService();
            string baselinePath = Path.Combine(auditDirectory, CompactBaselineName);
            if (!File.Exists(baselinePath))
                NativeDesktopIconLayoutService.SaveSnapshot(layout.CaptureSnapshot(), baselinePath);

            var metadata = new NativeDesktopLayoutMetadataStore();
            ManualLayoutResolution manual = metadata.LoadManualResolution();
            var planner = new NativeDesktopLayoutPlanner(layout, manual.Groups, manual.UnmappedManualPaths);
            DesktopLayoutPlan plan = planner.CreatePlan();
            File.WriteAllText(Path.Combine(auditDirectory, CompactPlanJsonName),
                JsonConvert.SerializeObject(plan, Formatting.Indented), new UTF8Encoding(false));
            WriteCompactPreview(Path.Combine(auditDirectory, "ui-phase5-3b-preview.md"), plan);
            WriteUnassignedPreview(Path.Combine(auditDirectory, "ui-phase5-3b-unassigned-preview.md"), plan);
            WriteLayoutMap(Path.Combine(auditDirectory, "ui-phase5-3b-layout-map.svg"), plan);
            return plan;
        }

        public static NativeDesktopLayoutBatchResult ApplyNativeDesktopLayout(string auditDirectory, NativeDesktopLayoutGroupId group) =>
            Apply(auditDirectory, LoadOrCreatePlan(auditDirectory), group);

        public static NativeDesktopRestoreResult RestoreOriginalDesktopLayout(string auditDirectory)
            => RestoreSnapshot(auditDirectory, BaselineName, "ui-phase5-3-restore-results.md");

        private static NativeDesktopRestoreResult RestoreSnapshot(string auditDirectory, string baselineName, string resultName)
        {
            string baselinePath = Path.Combine(auditDirectory, baselineName);
            if (!File.Exists(baselinePath)) throw new FileNotFoundException("Native Desktop layout baseline was not found.", baselinePath);
            var layout = new NativeDesktopIconLayoutService();
            NativeDesktopLayoutSnapshot baseline = NativeDesktopIconLayoutService.LoadSnapshot(baselinePath);
            NativeDesktopSnapshotValidationResult validation = layout.ValidateSnapshot(baseline);
            if (validation.AutoArrangeEnabled || validation.Missing != 0 || validation.Ambiguous != 0)
                throw new InvalidOperationException($"RESTORE_DRY_RUN_FAILED: CanResolve={validation.CanResolve}; Missing={validation.Missing}; Ambiguous={validation.Ambiguous}; AutoArrange={validation.AutoArrangeEnabled}");

            NativeDesktopRestoreResult result = layout.Restore(baseline);
            if (result.Failed != 0) throw new InvalidOperationException("RESTORE_FAILED: " + string.Join("; ", result.Errors));
            WriteRestore(Path.Combine(auditDirectory, resultName), validation, result);
            return result;
        }

        private static DesktopLayoutPlan LoadOrCreatePlan(string auditDirectory)
        {
            string path = Path.Combine(auditDirectory, PlanJsonName);
            if (!File.Exists(path)) return CreateAndWritePreview(auditDirectory);
            return JsonConvert.DeserializeObject<DesktopLayoutPlan>(File.ReadAllText(path, Encoding.UTF8))
                ?? throw new InvalidDataException("Native Desktop layout plan is empty or invalid.");
        }

        private static NativeDesktopLayoutBatchResult Apply(string auditDirectory, DesktopLayoutPlan plan,
            NativeDesktopLayoutGroupId group, string baselineName = BaselineName, string auditPrefix = "ui-phase5-3")
        {
            string batchName = BatchName(group);
            var result = new NativeDesktopLayoutBatchResult { BatchName = batchName, GroupId = group };
            if (plan.AutoArrangeEnabled)
            {
                result.Reason = "AUTO_ARRANGE_ENABLED";
                WriteBatch(auditDirectory, result, Array.Empty<DesktopLayoutItem>(), Array.Empty<NativeDesktopIconSnapshotItem>(), Array.Empty<NativeDesktopIconSnapshotItem>(), auditPrefix);
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
                WriteBatch(auditDirectory, result, candidates, Array.Empty<NativeDesktopIconSnapshotItem>(), Array.Empty<NativeDesktopIconSnapshotItem>(), auditPrefix);
                WriteApplyResult(auditDirectory, auditPrefix, $"{batchName}=PASS\nGroup={group}\nItemsMoved=0\nReason={result.Reason}\nFilesystemUnchanged=True\nOtherUnmanagedIconsChanged=False");
                return result;
            }

            var layout = new NativeDesktopIconLayoutService();
            NativeDesktopListViewState state = layout.GetListViewState();
            if (state.AutoArrangeEnabled)
            {
                result.Reason = "AUTO_ARRANGE_ENABLED";
                return result;
            }

            string baselinePath = Path.Combine(auditDirectory, baselineName);
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
                WriteBatch(auditDirectory, result, candidates, before.Icons, before.Icons, auditPrefix);
                return result;
            }

            IReadOnlyList<(DesktopLayoutItem Item, int X, int Y)> staging =
                BuildStagingPositions(layout.EnumerateDesktopIcons(), candidates, plan, state);
            if (staging.Count != candidates.Count)
            {
                result.Reason = "NO_SAFE_STAGING_SLOTS";
                WriteBatch(auditDirectory, result, candidates, before.Icons, before.Icons, auditPrefix);
                return result;
            }

            foreach ((DesktopLayoutItem item, int x, int y) in staging)
            {
                if (!layout.TrySetPosition(item.OriginalPath, x, y, out _, out string reason))
                {
                    result.Errors.Add($"{item.DisplayName} staging: {reason}");
                    NativeDesktopRestoreResult rollback = layout.Restore(before);
                    result.Reason = rollback.Failed == 0 ? "STAGING_FAILED_ROLLED_BACK" : "STAGING_FAILED_ROLLBACK_FAILED";
                    WriteBatch(auditDirectory, result, candidates, before.Icons, layout.CaptureSnapshot().Icons, auditPrefix);
                    return result;
                }
            }

            foreach (DesktopLayoutItem candidate in candidates)
            {
                if (!layout.TrySetPosition(candidate.OriginalPath, candidate.TargetX, candidate.TargetY, out _, out string reason))
                {
                    result.Errors.Add($"{candidate.DisplayName}: {reason}");
                    NativeDesktopRestoreResult rollback = layout.Restore(before);
                    result.Reason = rollback.Failed == 0 ? "APPLY_FAILED_ROLLED_BACK" : "APPLY_FAILED_ROLLBACK_FAILED";
                    WriteBatch(auditDirectory, result, candidates, before.Icons, layout.CaptureSnapshot().Icons, auditPrefix);
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
                WriteBatch(auditDirectory, result, candidates, before.Icons, layout.CaptureSnapshot().Icons, auditPrefix);
                return result;
            }

            new NativeDesktopLayoutMetadataStore().PersistLayoutGroups(candidates);
            result.Applied = true;
            result.Reason = "APPLIED";
            WriteBatch(auditDirectory, result, candidates, before.Icons, after.Icons, auditPrefix);
            WriteApplyResult(auditDirectory, auditPrefix, $"{batchName}=PASS\nGroup={group}\nItemsMoved={result.Moved}\nSnapshotDryRun=PASS CanResolve={validation.CanResolve}\nFilesystemUnchanged={result.FilesystemUnchanged}\nOtherUnmanagedIconsChanged={result.OtherUnmanagedIconsChanged}");
            return result;
        }

        private static IReadOnlyList<(DesktopLayoutItem Item, int X, int Y)> BuildStagingPositions(
            IReadOnlyList<NativeDesktopIconState> current, IReadOnlyList<DesktopLayoutItem> candidates,
            DesktopLayoutPlan plan, NativeDesktopListViewState state)
        {
            Drawing.Rectangle work = new(plan.WorkAreaLeft, plan.WorkAreaTop, plan.WorkAreaWidth, plan.WorkAreaHeight);
            int gridX = Math.Max(1, state.GridSpacingX);
            int gridY = Math.Max(1, state.GridSpacingY);
            var unavailable = current.Select(icon => NativeDesktopLayoutPlanner.SlotKey(icon.X, icon.Y, work, gridX, gridY))
                .Concat(candidates.Select(item => NativeDesktopLayoutPlanner.SlotKey(item.TargetX, item.TargetY, work, gridX, gridY)))
                .ToHashSet(StringComparer.Ordinal);
            var staging = new List<(DesktopLayoutItem Item, int X, int Y)>();
            int columns = Math.Max(1, (work.Width - 1) / gridX) + 1;
            int rows = Math.Max(1, (work.Height - 1) / gridY) + 1;

            foreach (DesktopLayoutItem item in candidates)
            {
                bool found = false;
                for (int row = rows - 1; row >= 0 && !found; row--)
                for (int column = columns - 1; column >= 0; column--)
                {
                    int x = work.Left + (column * gridX);
                    int y = work.Top + (row * gridY);
                    string key = NativeDesktopLayoutPlanner.SlotKey(x, y, work, gridX, gridY);
                    if (unavailable.Contains(key)) continue;
                    unavailable.Add(key);
                    staging.Add((item, x, y));
                    found = true;
                    break;
                }
                if (!found) return Array.Empty<(DesktopLayoutItem Item, int X, int Y)>();
            }
            return staging;
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

        private static void WriteCompactPreview(string path, DesktopLayoutPlan plan)
        {
            var markdown = new StringBuilder("# UI Phase 5.3b Compact Reflow Preview\n\n");
            markdown.AppendLine($"- LayoutMode: {plan.LayoutMode}; UnassignedLayoutMode: {plan.UnassignedLayoutMode}");
            markdown.AppendLine($"- DesktopWorkArea: {plan.WorkAreaWidth}x{plan.WorkAreaHeight}; DPI: {plan.Dpi}; Grid: {plan.GridSpacingX}x{plan.GridSpacingY}");
            markdown.AppendLine($"- CentralClearZone: {plan.CentralClearZoneLeft},{plan.CentralClearZoneTop} {plan.CentralClearZoneWidth}x{plan.CentralClearZoneHeight}; preserved: {plan.CentralClearZonePreserved}");
            markdown.AppendLine($"- DuplicateTargetCoordinates: {plan.DuplicateTargetCoordinates}; TargetOutsideWorkArea: {plan.TargetOutsideDesktopWorkArea}\n");
            markdown.AppendLine("| Group | Anchor | Columns | Rows | AspectRatio | Fragmentation | Fallback | Classified | Eligible |");
            markdown.AppendLine("|---|---|---:|---:|---:|---:|---|---:|---:|");
            foreach (DesktopLayoutGroup group in plan.Groups)
                markdown.AppendLine($"| {Cell(group.GroupDisplayName)} | {group.Anchor} | {group.Columns} | {group.Rows} | {group.AspectRatio:0.###} | {group.FragmentationCount} | {group.UsedFallbackRegion} | {group.PlannedItemCount} | {group.EligibleItemCount} |");
            markdown.AppendLine();
            markdown.AppendLine("| Name | Group | Current | Target | Eligible | Reason |");
            markdown.AppendLine("|---|---|---|---|---|---|");
            foreach (DesktopLayoutItem item in plan.Items.OrderBy(item => item.LayoutGroupId).ThenBy(item => item.TargetY).ThenBy(item => item.TargetX))
                markdown.AppendLine($"| {Cell(item.DisplayName)} | {item.LayoutGroupId} | ({item.CurrentX}, {item.CurrentY}) | ({item.TargetX}, {item.TargetY}) | {item.IsEligibleForApply} | {item.ApplyReason} |");
            File.WriteAllText(path, markdown.ToString(), new UTF8Encoding(false));
        }

        private static void WriteUnassignedPreview(string path, DesktopLayoutPlan plan)
        {
            List<DesktopLayoutItem> items = plan.Items.Where(item => item.LayoutGroupId == NativeDesktopLayoutGroupId.UNASSIGNED.ToString()).ToList();
            int columns = Math.Min(5, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(items.Count))));
            int rows = (int)Math.Ceiling(items.Count / (double)columns);
            int totalColumns = Math.Max(1, (plan.WorkAreaWidth - 1) / Math.Max(1, plan.GridSpacingX)) + 1;
            int totalRows = Math.Max(1, (plan.WorkAreaHeight - 1) / Math.Max(1, plan.GridSpacingY)) + 1;
            int left = Math.Max(0, totalColumns - columns);
            int top = Math.Max(0, totalRows - rows);
            var markdown = new StringBuilder("# UI Phase 5.3b Unassigned Compact Preview\n\n");
            markdown.AppendLine("ApplyUnassigned=false\n");
            markdown.AppendLine("This is a coordinate simulation only. Shell namespace, ambiguous, and unassigned items remain untouched.\n");
            markdown.AppendLine("| Name | Current | SimulatedBottomRightTarget |");
            markdown.AppendLine("|---|---|---|");
            for (int index = 0; index < items.Count; index++)
            {
                int x = plan.WorkAreaLeft + ((left + (index % columns)) * plan.GridSpacingX);
                int y = plan.WorkAreaTop + ((top + (index / columns)) * plan.GridSpacingY);
                markdown.AppendLine($"| {Cell(items[index].DisplayName)} | ({items[index].CurrentX}, {items[index].CurrentY}) | ({x}, {y}) |");
            }
            File.WriteAllText(path, markdown.ToString(), new UTF8Encoding(false));
        }

        private static void WriteLayoutMap(string path, DesktopLayoutPlan plan)
        {
            static string Color(string group) => group switch
            {
                "RESEARCH_STUDY" => "#1f8a70",
                "AI_DEV" => "#2563a6",
                "OFFICE_DOCUMENTS" => "#c07a24",
                _ => "#777777"
            };
            static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;

            var svg = new StringBuilder();
            svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{plan.WorkAreaWidth}\" height=\"{plan.WorkAreaHeight}\" viewBox=\"0 0 {plan.WorkAreaWidth} {plan.WorkAreaHeight}\">");
            svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#f2f4f6\"/>");
            svg.AppendLine($"<rect x=\"{plan.CentralClearZoneLeft}\" y=\"{plan.CentralClearZoneTop}\" width=\"{plan.CentralClearZoneWidth}\" height=\"{plan.CentralClearZoneHeight}\" fill=\"none\" stroke=\"#8a929a\" stroke-dasharray=\"12 10\" stroke-width=\"3\"/>");
            svg.AppendLine($"<text x=\"{plan.CentralClearZoneLeft + 12}\" y=\"{plan.CentralClearZoneTop + 28}\" font-family=\"Segoe UI\" font-size=\"20\" fill=\"#59616a\">Central clear zone</text>");
            foreach (DesktopLayoutItem item in plan.Items)
            {
                bool managed = item.LayoutGroupId is "RESEARCH_STUDY" or "AI_DEV" or "OFFICE_DOCUMENTS";
                string fill = managed ? Color(item.LayoutGroupId) : "#9aa1a8";
                int x = item.TargetX + 4;
                int y = item.TargetY + 4;
                svg.AppendLine($"<rect x=\"{x}\" y=\"{y}\" width=\"{Math.Max(20, plan.GridSpacingX - 8)}\" height=\"{Math.Max(20, plan.GridSpacingY - 8)}\" rx=\"3\" fill=\"{fill}\" opacity=\"{(managed ? "0.88" : "0.38")}\"><title>{Xml(item.DisplayName)} [{item.LayoutGroupId}]</title></rect>");
            }
            svg.AppendLine("</svg>");
            File.WriteAllText(path, svg.ToString(), new UTF8Encoding(false));
        }

        private static EmptyDefaultFrameCleanupResult CleanupEmptyDefaultFrame(string auditDirectory, string? explicitFramesPath)
        {
            string framesPath = string.IsNullOrWhiteSpace(explicitFramesPath)
                ? ProfileManager.GetProfileFilePath("frames.json")
                : Path.GetFullPath(explicitFramesPath);
            var result = new EmptyDefaultFrameCleanupResult { FramesPath = framesPath };
            if (!File.Exists(framesPath)) return result;

            JArray frames = JArray.Parse(File.ReadAllText(framesPath, Encoding.UTF8));
            List<JObject> matches = frames.OfType<JObject>().Where(IsConfirmedEmptyDefaultFrame).ToList();
            result.Found = matches.Count > 0;
            result.MatchCount = matches.Count;
            if (matches.Count != 1)
            {
                result.Reason = matches.Count == 0 ? "NO_CONFIRMED_EMPTY_DEFAULT_FRAME" : "AMBIGUOUS_MULTIPLE_DEFAULT_FRAMES";
                return result;
            }

            string backupDirectory = Path.Combine(auditDirectory, "backups", "ui-phase5-3b");
            Directory.CreateDirectory(backupDirectory);
            string backupPath = Path.Combine(backupDirectory, "frames-before-empty-default-cleanup.json");
            File.Copy(framesPath, backupPath, overwrite: true);
            result.BackupPath = backupPath;
            frames.Remove(matches[0]);
            File.WriteAllText(framesPath, frames.ToString(Formatting.Indented), new UTF8Encoding(false));
            result.Removed = true;
            result.Reason = "CONFIRMED_EMPTY_NON_SYSTEM_DEFAULT_FRAME_REMOVED";
            return result;
        }

        private static bool IsConfirmedEmptyDefaultFrame(JObject frame)
        {
            string title = frame["Title"]?.ToString().Trim() ?? string.Empty;
            bool builtInTitle = title.Equals("New Frame - Drop your shortcuts here", StringComparison.Ordinal) ||
                title.Equals("新建分组 - 将快捷方式拖到这里", StringComparison.Ordinal);
            return builtInTitle && frame["ItemsType"]?.ToString().Equals("Data", StringComparison.OrdinalIgnoreCase) == true &&
                string.IsNullOrWhiteSpace(frame["SystemFrameId"]?.ToString()) &&
                ((frame["Items"] as JArray)?.Count ?? 0) == 0 && ((frame["Tabs"] as JArray)?.Count ?? 0) == 0;
        }

        private static void RunSearchSmoke(string auditDirectory)
        {
            var service = new SearchService();
            service.Refresh();
            string[] queries = { "Codex", "论文", "PDF" };
            var markdown = new StringBuilder("# UI Phase 5.3b SpotSearch Smoke Test\n\n");
            bool passed = true;
            foreach (string query in queries)
            {
                IReadOnlyList<SearchResult> results = service.Search(query, 5);
                bool found = results.Count > 0;
                passed &= found;
                markdown.AppendLine($"## {query}\n");
                markdown.AppendLine($"Result={ (found ? "PASS" : "FAIL") }\n");
                foreach (SearchResult result in results)
                    markdown.AppendLine($"- {result.DisplayName} | {result.OriginalPath} | {result.SourceProvider}");
                markdown.AppendLine();
            }
            markdown.AppendLine($"SearchRegression={(passed ? "PASS" : "FAIL")}");
            File.WriteAllText(Path.Combine(auditDirectory, "ui-phase5-3b-search.md"), markdown.ToString(), new UTF8Encoding(false));
            if (!passed) throw new InvalidOperationException("SPOT_SEARCH_SMOKE_FAILED");
        }

        private static void WriteBatch(string auditDirectory, NativeDesktopLayoutBatchResult result, IReadOnlyList<DesktopLayoutItem> candidates,
            IReadOnlyList<NativeDesktopIconSnapshotItem> before, IReadOnlyList<NativeDesktopIconSnapshotItem> after,
            string auditPrefix = "ui-phase5-3")
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
            File.WriteAllText(Path.Combine(auditDirectory, $"{auditPrefix}-{result.BatchName.ToLowerInvariant()}.csv"), csv.ToString(), new UTF8Encoding(false));
        }

        private static void WriteRestore(string path, NativeDesktopSnapshotValidationResult validation, NativeDesktopRestoreResult result)
        {
            File.WriteAllText(path, $"# UI Phase 5.3 Restore Results\n\nCanResolve={validation.CanResolve}\n\nMissing={validation.Missing}\n\nAmbiguous={validation.Ambiguous}\n\nRestored={result.Restored}\n\nFailed={result.Failed}\n", new UTF8Encoding(false));
        }

        private static void WriteResults(string auditDirectory, string content) =>
            File.WriteAllText(Path.Combine(auditDirectory, "ui-phase5-3-layout-results.md"), "# UI Phase 5.3 Native Layout Results\n\n" + content + "\n", new UTF8Encoding(false));

        private static void WriteCompactResults(string auditDirectory, string content) =>
            File.WriteAllText(Path.Combine(auditDirectory, "ui-phase5-3b-command-log.md"),
                "# UI Phase 5.3b Command Log\n\n" + content + "\n", new UTF8Encoding(false));

        private static void WriteApplyResult(string auditDirectory, string auditPrefix, string content)
        {
            if (string.Equals(auditPrefix, "ui-phase5-3b", StringComparison.OrdinalIgnoreCase))
                WriteCompactResults(auditDirectory, content);
            else
                WriteResults(auditDirectory, content);
        }

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

    internal sealed class EmptyDefaultFrameCleanupResult
    {
        public string FramesPath { get; init; } = string.Empty;
        public bool Found { get; set; }
        public int MatchCount { get; set; }
        public bool Removed { get; set; }
        public string BackupPath { get; set; } = string.Empty;
        public string Reason { get; set; } = "FRAMES_FILE_NOT_FOUND";

        public string ToAuditText() =>
            $"EmptyDefaultFrameFound={Found}\nEmptyDefaultFrameMatches={MatchCount}\n" +
            $"EmptyDefaultFrameRemoved={Removed}\nFramesPath={FramesPath}\nBackupPath={BackupPath}\nReason={Reason}";
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
