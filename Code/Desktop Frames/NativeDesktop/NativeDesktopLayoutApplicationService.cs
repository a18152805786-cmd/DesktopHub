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
        private const string FullPlanJsonName = "ui-phase5-4-full-layout-plan.json";
        private const string FullBaselineName = "ui-phase5-4-desktop-before.json";
        private const string DualRailPlanJsonName = "ui-phase5-5-layout-plan.json";
        private const string DualRailBaselineName = "ui-phase5-5-desktop-before.json";

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
                if (args.Any(arg => arg.Equals("--native-layout-identity-tests", StringComparison.OrdinalIgnoreCase)))
                {
                    NativeDesktopIdentityClassificationTestHarness.Run(auditDirectory);
                    return 0;
                }

                string? liveIdentityLabel = ExtractValue(args, "--native-layout-live-identity-label=");
                if (!string.IsNullOrWhiteSpace(liveIdentityLabel))
                {
                    NativeDesktopIdentityClassificationTestHarness.RunLiveAddressTest(auditDirectory, liveIdentityLabel);
                    return 0;
                }

                if (args.Any(arg => arg.Equals("--native-layout-phase571-game-drag", StringComparison.OrdinalIgnoreCase)))
                {
                    RunPhase571GameDragRegression(auditDirectory);
                    return 0;
                }

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

                if (args.Any(arg => arg.Equals("--native-layout-full-preview", StringComparison.OrdinalIgnoreCase)))
                {
                    DesktopLayoutPlan plan = CreateAndWriteFullPreview(auditDirectory);
                    WriteFullResults(auditDirectory, plan, "Preview=PASS");
                    return 0;
                }

                if (args.Any(arg => arg.Equals("--native-layout-full-apply", StringComparison.OrdinalIgnoreCase)))
                {
                    ApplyFullDesktopLayout(auditDirectory);
                    return 0;
                }

                if (args.Any(arg => arg.Equals("--native-layout-full-refresh", StringComparison.OrdinalIgnoreCase)))
                {
                    DesktopLayoutPlan plan = CreateAndWriteFullPreview(auditDirectory);
                    WriteFullResults(auditDirectory, plan, "Refresh=PASS\nTargetCoordinatesAlreadySatisfied=true");
                    return 0;
                }

                if (args.Any(arg => arg.Equals("--native-layout-dualrail-preview", StringComparison.OrdinalIgnoreCase)))
                {
                    DesktopLayoutPlan plan = CreateAndWriteDualRailPreview(auditDirectory);
                    WriteDualRailResults(auditDirectory, plan, "Preview=PASS");
                    return 0;
                }

                if (args.Any(arg => arg.Equals("--native-layout-dualrail-apply", StringComparison.OrdinalIgnoreCase)))
                {
                    ApplyDualRailLayout(auditDirectory);
                    return 0;
                }

                if (args.Any(arg => arg.Equals("--native-layout-dualrail-refresh", StringComparison.OrdinalIgnoreCase)))
                {
                    DesktopLayoutPlan plan = CreateAndWriteDualRailPreview(auditDirectory);
                    WriteDualRailResults(auditDirectory, plan, "Refresh=PASS\nTargetCoordinatesAlreadySatisfied=true");
                    return 0;
                }

                if (args.Any(arg => arg.Equals("--native-layout-dualrail-restore", StringComparison.OrdinalIgnoreCase)))
                {
                    RestoreDualRailLayout(auditDirectory);
                    return 0;
                }

                if (args.Any(arg => arg.Equals("--native-layout-dualrail-show", StringComparison.OrdinalIgnoreCase)))
                {
                    DesktopLayoutPlan plan = LoadDualRailPlan(auditDirectory);
                    DesktopGroupHeaderManager.Show(plan);
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

        public static DesktopLayoutPlan CreateAndWriteFullPreview(string auditDirectory)
        {
            Directory.CreateDirectory(auditDirectory);
            var layout = new NativeDesktopIconLayoutService();
            string baselinePath = Path.Combine(auditDirectory, FullBaselineName);
            if (!File.Exists(baselinePath))
                NativeDesktopIconLayoutService.SaveSnapshot(layout.CaptureSnapshot(), baselinePath);

            var metadata = new NativeDesktopLayoutMetadataStore();
            ManualLayoutResolution manual = metadata.LoadManualResolution();
            var planner = new NativeDesktopLayoutPlanner(layout, manual.Groups, manual.UnmappedManualPaths);
            DesktopLayoutPlan plan = planner.PlanFullDesktopLayout();
            File.WriteAllText(
                Path.Combine(auditDirectory, FullPlanJsonName),
                JsonConvert.SerializeObject(plan, Formatting.Indented),
                new UTF8Encoding(false));
            WriteFilesystemFingerprint(Path.Combine(auditDirectory, "ui-phase5-4-desktop-files-before.csv"));
            WriteFullPreview(Path.Combine(auditDirectory, "ui-phase5-4-full-layout-preview.md"), plan);
            WriteFullLayoutMap(Path.Combine(auditDirectory, "ui-phase5-4-full-layout-map.svg"), plan);
            return plan;
        }

        public static void RestoreFullDesktopLayout(string auditDirectory) =>
            RestoreSnapshot(auditDirectory, FullBaselineName, "ui-phase5-4-restore-results.md");

        public static void ApplyFullDesktopLayout(string auditDirectory)
        {
            Directory.CreateDirectory(auditDirectory);
            string baselinePath = Path.Combine(auditDirectory, FullBaselineName);
            if (!File.Exists(baselinePath))
                NativeDesktopIconLayoutService.SaveSnapshot(new NativeDesktopIconLayoutService().CaptureSnapshot(), baselinePath);

            DesktopLayoutPlan plan = LoadFullPlan(auditDirectory);
            if (plan.AutoArrangeEnabled)
                throw new InvalidOperationException("FULL_APPLY_BLOCKED_AUTO_ARRANGE_ENABLED");
            if (plan.DuplicateTargetCoordinates != 0 || plan.TargetOutsideDesktopWorkArea != 0 || !plan.CentralClearZonePreserved)
            {
                throw new InvalidOperationException(
                    $"FULL_APPLY_DRY_RUN_FAILED DuplicateTargets={plan.DuplicateTargetCoordinates}; " +
                    $"OutOfBounds={plan.TargetOutsideDesktopWorkArea}; CentralViolations={!plan.CentralClearZonePreserved}");
            }

            List<DesktopLayoutItem> managedItems = plan.Items
                .Where(item => item.IsStableIdentity && !item.IsShellNamespace)
                .ToList();
            List<DesktopLayoutItem> eligibleItems = managedItems.Where(item => item.IsEligibleForApply).ToList();
            if (eligibleItems.Count == 0)
            {
                WriteFullResults(auditDirectory, plan, "Apply=PASS\nItemsMoved=0\nReason=ALREADY_AT_TARGET");
                return;
            }

            List<DesktopLayoutItem> candidates = managedItems;

            var layout = new NativeDesktopIconLayoutService();
            NativeDesktopListViewState state = layout.GetListViewState();
            if (state.AutoArrangeEnabled)
                throw new InvalidOperationException("FULL_APPLY_BLOCKED_AUTO_ARRANGE_ENABLED");

            NativeDesktopLayoutSnapshot baseline = NativeDesktopIconLayoutService.LoadSnapshot(baselinePath);
            baseline = HealRestorePoint(layout, baseline, baselinePath);
            NativeDesktopSnapshotValidationResult validation = layout.ValidateSnapshot(baseline);
            if (validation.AutoArrangeEnabled || validation.Missing != 0 || validation.Ambiguous != 0)
            {
                throw new InvalidOperationException(
                    $"FULL_SNAPSHOT_DRY_RUN_FAILED CanResolve={validation.CanResolve}; Missing={validation.Missing}; Ambiguous={validation.Ambiguous}");
            }

            NativeDesktopLayoutSnapshot before = layout.CaptureSnapshot();
            DesktopFilesystemFingerprint filesystemBefore = DesktopFilesystemFingerprint.Capture();
            if (!TargetsAreVacant(layout.EnumerateDesktopIcons(), candidates, plan, state))
                throw new InvalidOperationException("FULL_APPLY_BLOCKED_TARGET_SLOT_OCCUPIED");

            IReadOnlyList<(DesktopLayoutItem Item, int X, int Y)> staging =
                BuildStagingPositions(layout.EnumerateDesktopIcons(), candidates, plan, state);
            if (staging.Count != candidates.Count)
                throw new InvalidOperationException("FULL_APPLY_BLOCKED_NO_SAFE_STAGING_SLOTS");

            try
            {
                foreach ((DesktopLayoutItem item, int x, int y) in staging)
                {
                    if (!layout.TrySetPosition(item.OriginalPath, x, y, out _, out string reason))
                        throw new InvalidOperationException($"{item.DisplayName} staging failed: {reason}");
                }

                foreach (DesktopLayoutItem candidate in candidates)
                {
                    if (!layout.TrySetPosition(candidate.OriginalPath, candidate.TargetX, candidate.TargetY, out NativeDesktopIconState? updated, out string reason))
                        throw new InvalidOperationException($"{candidate.DisplayName} target failed: {reason}");
                    if (updated != null)
                    {
                        candidate.TargetX = updated.X;
                        candidate.TargetY = updated.Y;
                    }
                }
            }
            catch
            {
                layout.Restore(before);
                throw;
            }

            NativeDesktopLayoutSnapshot after = layout.CaptureSnapshot();
            bool otherIconsChanged = FullOtherIconsChanged(before, after, candidates, plan, state);
            bool filesystemUnchanged = filesystemBefore.Equals(DesktopFilesystemFingerprint.Capture());
            bool targetsVerified = TargetsVerified(after, candidates, plan, state);
            if (otherIconsChanged || !filesystemUnchanged || !targetsVerified)
            {
                layout.Restore(before);
                throw new InvalidOperationException(
                    $"FULL_APPLY_VERIFICATION_FAILED OtherIconsChanged={otherIconsChanged}; FilesystemUnchanged={filesystemUnchanged}; TargetsVerified={targetsVerified}; Mismatch={string.Join(" | ", FullTargetMismatches(after, candidates, plan, state).Take(10))}");
            }

            new NativeDesktopLayoutMetadataStore().PersistLayoutGroups(candidates);
            var movedByGroup = eligibleItems.GroupBy(item => item.LayoutGroupId)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var result = new StringBuilder("Apply=PASS");
            result.Append('\n').Append("ItemsMoved=").Append(eligibleItems.Count);
            foreach (var pair in movedByGroup.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                result.Append('\n').Append(pair.Key).Append("PositionChanged=").Append(pair.Value);
            WriteFullResults(auditDirectory, plan, result.ToString());
        }

        private static DesktopLayoutPlan LoadFullPlan(string auditDirectory)
        {
            string path = Path.Combine(auditDirectory, FullPlanJsonName);
            if (!File.Exists(path))
                return CreateAndWriteFullPreview(auditDirectory);
            return JsonConvert.DeserializeObject<DesktopLayoutPlan>(File.ReadAllText(path, Encoding.UTF8))
                ?? throw new InvalidDataException("Full Desktop layout plan is empty or invalid.");
        }

        private static void WriteFullResults(string auditDirectory, DesktopLayoutPlan plan, string header)
        {
            var markdown = new StringBuilder("# UI Phase 5.4 Full Desktop Visual Consolidation Results\n\n");
            markdown.AppendLine(header);
            markdown.AppendLine();
            markdown.AppendLine($"StableIconCount={plan.Items.Count(item => item.IsStableIdentity)}");
            markdown.AppendLine($"AmbiguousIconCount={plan.Items.Count(item => !item.IsStableIdentity && !item.IsShellNamespace)}");
            markdown.AppendLine($"ShellNamespaceCount={plan.Items.Count(item => item.IsShellNamespace)}");
            markdown.AppendLine($"ManagedIconsOutsideAssignedBounds={plan.ManagedIconsOutsideAssignedBounds}");
            markdown.AppendLine($"UnassignedIconsOutsideUnassignedBounds={plan.UnassignedIconsOutsideUnassignedBounds}");
            markdown.AppendLine($"CentralClearZone={plan.CentralClearZoneLeft},{plan.CentralClearZoneTop} {plan.CentralClearZoneWidth}x{plan.CentralClearZoneHeight}");
            markdown.AppendLine($"CentralClearZonePreserved={plan.CentralClearZonePreserved}");
            markdown.AppendLine($"DuplicateTargetCoordinates={plan.DuplicateTargetCoordinates}");
            markdown.AppendLine($"TargetOutsideWorkArea={plan.TargetOutsideDesktopWorkArea}");
            markdown.AppendLine($"VisualQualityGate={(FullVisualQualityGatePasses(plan) ? "PASS" : "FAIL")}");
            markdown.AppendLine("GroupLabelsDeferred=true");
            markdown.AppendLine("FilesystemMove=false");
            markdown.AppendLine("FilesystemDelete=false");
            markdown.AppendLine("FilesystemRename=false");
            markdown.AppendLine("FilesystemCopy=false");
            markdown.AppendLine("AutoArrangeModified=false");
            markdown.AppendLine("AlignToGridModified=false");
            markdown.AppendLine("ExplorerInjection=false");
            markdown.AppendLine("InputForwarding=false");
            markdown.AppendLine("ShellInjection=false");
            File.WriteAllText(
                Path.Combine(auditDirectory, "ui-phase5-4-results.md"),
                markdown.ToString(),
                new UTF8Encoding(false));
        }

        private static void WriteFullPreview(string path, DesktopLayoutPlan plan)
        {
            var markdown = new StringBuilder("# UI Phase 5.4 Full Desktop Layout Preview\n\n");
            markdown.AppendLine($"WorkArea={plan.WorkAreaWidth}x{plan.WorkAreaHeight}");
            markdown.AppendLine($"Grid={plan.GridSpacingX}x{plan.GridSpacingY}");
            markdown.AppendLine($"CentralClearZone={plan.CentralClearZoneLeft},{plan.CentralClearZoneTop} {plan.CentralClearZoneWidth}x{plan.CentralClearZoneHeight}");
            markdown.AppendLine($"VisualQualityGate={(FullVisualQualityGatePasses(plan) ? "PASS" : "FAIL")}\n");
            markdown.AppendLine("| Group | MemberCount | Anchor | Columns | Rows | BoundingRect | AspectRatio | Fragmentation | Eligible |");
            markdown.AppendLine("|---|---:|---|---:|---:|---|---:|---:|---:|");
            foreach (DesktopLayoutGroup group in plan.Groups)
            {
                (int left, int top, int right, int bottom) = GroupBounds(plan, group.GroupId.ToString());
                markdown.AppendLine(
                    $"| {Cell(group.GroupDisplayName)} | {group.PlannedItemCount} | {group.Anchor} | {group.Columns} | {group.Rows} | " +
                    $"{left},{top} {right-left}x{bottom-top} | {group.AspectRatio:0.###} | {group.FragmentationCount} | {group.EligibleItemCount} |");
            }

            markdown.AppendLine("\n| Name | Group | Current | Target | Eligible | Reason |");
            markdown.AppendLine("|---|---|---|---|---|---|");
            foreach (DesktopLayoutItem item in plan.Items.OrderBy(item => item.LayoutGroupId).ThenBy(item => item.TargetY).ThenBy(item => item.TargetX))
            {
                markdown.AppendLine(
                    $"| {Cell(item.DisplayName)} | {item.LayoutGroupId} | ({item.CurrentX}, {item.CurrentY}) | " +
                    $"({item.TargetX}, {item.TargetY}) | {item.IsEligibleForApply} | {Cell(item.ApplyReason)} |");
            }

            File.WriteAllText(path, markdown.ToString(), new UTF8Encoding(false));
        }

        private static void WriteFullLayoutMap(string path, DesktopLayoutPlan plan)
        {
            static string Color(string group) => group switch
            {
                "RESEARCH_STUDY" => "#1f8a70",
                "AI_DEV" => "#2563a6",
                "OFFICE_DOCUMENTS" => "#c07a24",
                "DAILY_TOOLS" => "#7c5cd6",
                "GAMES" => "#b3405c",
                "SYSTEM_TEMP" => "#6b7280",
                _ => "#8a929a"
            };
            static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;

            var svg = new StringBuilder();
            svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{plan.WorkAreaWidth}\" height=\"{plan.WorkAreaHeight}\" viewBox=\"0 0 {plan.WorkAreaWidth} {plan.WorkAreaHeight}\">");
            svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#f4f6f8\"/>");
            svg.AppendLine($"<rect x=\"{plan.CentralClearZoneLeft}\" y=\"{plan.CentralClearZoneTop}\" width=\"{plan.CentralClearZoneWidth}\" height=\"{plan.CentralClearZoneHeight}\" fill=\"#ffffff\" opacity=\"0.55\" stroke=\"#7c8792\" stroke-dasharray=\"14 10\" stroke-width=\"4\"/>");
            svg.AppendLine($"<text x=\"{plan.CentralClearZoneLeft + 20}\" y=\"{plan.CentralClearZoneTop + 36}\" font-family=\"Segoe UI\" font-size=\"22\" fill=\"#59616a\">Central clear zone</text>");

            foreach (DesktopLayoutGroup group in plan.Groups.Where(group => group.PlannedItemCount > 0))
            {
                string groupId = group.GroupId.ToString();
                (int left, int top, int right, int bottom) = GroupBounds(plan, groupId);
                svg.AppendLine($"<rect x=\"{left-12}\" y=\"{top-12}\" width=\"{right-left+24}\" height=\"{bottom-top+24}\" rx=\"12\" fill=\"{Color(groupId)}\" opacity=\"0.08\" stroke=\"{Color(groupId)}\" stroke-width=\"2\"/>");
                svg.AppendLine($"<text x=\"{left}\" y=\"{Math.Max(24, top-22)}\" font-family=\"Segoe UI\" font-size=\"18\" fill=\"#30363c\">{Xml(group.GroupDisplayName)}</text>");
            }

            foreach (DesktopLayoutItem item in plan.Items)
            {
                bool movable = item.IsStableIdentity && !item.IsShellNamespace;
                string fill = movable ? Color(item.LayoutGroupId) : "#9aa1a8";
                int x = item.TargetX + 5;
                int y = item.TargetY + 5;
                svg.AppendLine($"<rect x=\"{x}\" y=\"{y}\" width=\"{Math.Max(18, plan.GridSpacingX - 10)}\" height=\"{Math.Max(18, plan.GridSpacingY - 10)}\" rx=\"4\" fill=\"{fill}\" opacity=\"{(movable ? "0.88" : "0.34")}\"><title>{Xml(item.DisplayName)} [{item.LayoutGroupId}]</title></rect>");
            }

            svg.AppendLine("</svg>");
            File.WriteAllText(path, svg.ToString(), new UTF8Encoding(false));
        }

        private static void WriteFilesystemFingerprint(string path)
        {
            if (File.Exists(path))
                return;

            var rows = new List<string> { "Name,FullPath,Type,LengthBytes,LastWriteTimeUtc" };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            })
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                foreach (string entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly))
                {
                    if (!seen.Add(entry))
                        continue;
                    bool isDirectory = Directory.Exists(entry);
                    string type = isDirectory ? "Directory" : Path.GetExtension(entry) is ".lnk" or ".url" ? "Shortcut" : "File";
                    long length = isDirectory ? -1 : new FileInfo(entry).Length;
                    DateTime lastWrite = File.GetLastWriteTimeUtc(entry);
                    rows.Add($"{Csv(Path.GetFileName(entry))},{Csv(entry)},{type},{(isDirectory ? string.Empty : length.ToString(CultureInfo.InvariantCulture))},{lastWrite.ToString("O", CultureInfo.InvariantCulture)}");
                }
            }

            File.WriteAllLines(path, rows, new UTF8Encoding(false));
        }

        private static (int Left, int Top, int Right, int Bottom) GroupBounds(DesktopLayoutPlan plan, string groupId)
        {
            List<DesktopLayoutItem> items = plan.Items
                .Where(item => item.LayoutGroupId == groupId && item.IsStableIdentity && !item.IsShellNamespace)
                .ToList();
            if (items.Count == 0)
                return (0, 0, 0, 0);

            int left = items.Min(item => item.TargetX);
            int top = items.Min(item => item.TargetY);
            int right = items.Max(item => item.TargetX) + plan.GridSpacingX;
            int bottom = items.Max(item => item.TargetY) + plan.GridSpacingY;
            return (left, top, right, bottom);
        }

        private static bool FullVisualQualityGatePasses(DesktopLayoutPlan plan)
        {
            if (plan.DuplicateTargetCoordinates != 0 || plan.TargetOutsideDesktopWorkArea != 0 || !plan.CentralClearZonePreserved)
                return false;
            if (plan.Groups.Any(group => group.PlannedItemCount > 0 && (group.Columns > 6 || group.FragmentationCount != 1)))
                return false;
            if (!InterGroupSpacingValid(plan))
                return false;
            return true;
        }

        private static bool InterGroupSpacingValid(DesktopLayoutPlan plan)
        {
            var bounds = plan.Groups.Where(group => group.PlannedItemCount > 0)
                .Select(group => (GroupId: group.GroupId.ToString(), Rect: GroupBounds(plan, group.GroupId.ToString())))
                .ToList();
            for (int i = 0; i < bounds.Count; i++)
            {
                for (int j = i + 1; j < bounds.Count; j++)
                {
                    Drawing.Rectangle expanded = new(bounds[i].Rect.Left, bounds[i].Rect.Top, bounds[i].Rect.Right - bounds[i].Rect.Left, bounds[i].Rect.Bottom - bounds[i].Rect.Top);
                    expanded.Inflate(plan.GridSpacingX, plan.GridSpacingY);
                    Drawing.Rectangle other = new(bounds[j].Rect.Left, bounds[j].Rect.Top, bounds[j].Rect.Right - bounds[j].Rect.Left, bounds[j].Rect.Bottom - bounds[j].Rect.Top);
                    if (expanded.IntersectsWith(other))
                        return false;
                }
            }
            return true;
        }

        public static DesktopLayoutPlan CreateAndWriteDualRailPreview(string auditDirectory)
        {
            Directory.CreateDirectory(auditDirectory);
            var layout = new NativeDesktopIconLayoutService();
            string baselinePath = Path.Combine(auditDirectory, DualRailBaselineName);
            if (!File.Exists(baselinePath))
                NativeDesktopIconLayoutService.SaveSnapshot(layout.CaptureSnapshot(), baselinePath);

            DesktopLayoutPlan plan = CreateDualRailPlan();
            File.WriteAllText(Path.Combine(auditDirectory, DualRailPlanJsonName),
                JsonConvert.SerializeObject(plan, Formatting.Indented), new UTF8Encoding(false));
            WriteIdentityClassificationAudit(
                Path.Combine(auditDirectory, "ui-phase5-7-1-identity-classification-audit.md"), plan);
            WriteFilesystemFingerprint(Path.Combine(auditDirectory, "ui-phase5-5-desktop-files-before.csv"));
            WriteFullPreview(Path.Combine(auditDirectory, "ui-phase5-5-layout-preview.md"), plan);
            WriteFullLayoutMap(Path.Combine(auditDirectory, "ui-phase5-5-layout-map.svg"), plan);
            return plan;
        }

        /// <summary>
        /// Plans with proposed zone metadata without saving it or writing an
        /// audit/restore point. Resize uses the normal planner before commit.
        /// </summary>
        internal static DesktopLayoutPlan CreateDualRailPlan(
            IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupZone>? proposedZones = null)
        {
            var metadata = new NativeDesktopLayoutMetadataStore();
            ManualLayoutResolution manual = metadata.LoadManualResolution();
            var planner = new NativeDesktopLayoutPlanner(new NativeDesktopIconLayoutService(),
                manual.Groups, manual.UnmappedManualPaths, proposedZones ?? DesktopGroupZoneStore.Load());
            return planner.PlanDualRailLayout();
        }

        private static void WriteIdentityClassificationAudit(string path, DesktopLayoutPlan plan)
        {
            static string Cell(string? value) => (value ?? string.Empty)
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", " ");

            var markdown = new StringBuilder();
            markdown.AppendLine("# UI Phase 5.7.1 identity / classification audit");
            markdown.AppendLine();
            markdown.AppendLine("IdentityClassificationSeparated=true");
            markdown.AppendLine("OrdinalMappingMethod=ItemTypeRank+NormalizedOriginalPath");
            markdown.AppendLine("SharedExplorerLabelDoesNotShareClassification=true");
            markdown.AppendLine();
            markdown.AppendLine("| ExplorerLabel | LabelOrdinal | CandidatePath | CandidateType | TargetPath | Arguments | URI | ManualOverride | ClassifierGroup | Confidence | ResolutionStatus | FinalGroup |");
            markdown.AppendLine("|---|---:|---|---|---|---|---|---:|---|---|---|---|");
            foreach (DesktopLayoutItem item in plan.Items
                         .OrderBy(item => item.ExplorerLabel, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.LabelOrdinal))
            {
                markdown.AppendLine($"| {Cell(item.ExplorerLabel)} | {item.LabelOrdinal} | {Cell(item.OriginalPath)} | " +
                    $"{Cell(item.CandidateType)} | {Cell(item.TargetPath)} | {Cell(item.Arguments)} | {Cell(item.Uri)} | " +
                    $"{item.IsManualOverride.ToString().ToLowerInvariant()} | {Cell(item.ClassifierGroup)} | " +
                    $"{Cell(item.ClassificationConfidence)} | {Cell(item.ResolutionStatus)} | {Cell(item.LayoutGroupId)} |");
            }
            File.WriteAllText(path, markdown.ToString(), new UTF8Encoding(false));
        }

        private static void RunPhase571GameDragRegression(string auditDirectory)
        {
            DesktopLayoutPlan initial = CreateAndWriteDualRailPreview(auditDirectory);
            ApplyDualRailLayout(auditDirectory);
            initial = LoadDualRailPlan(auditDirectory);
            DesktopLayoutGroup gamesBefore = initial.Groups.Single(group =>
                group.GroupId == NativeDesktopLayoutGroupId.GAMES);
            int expectedColumn = gamesBefore.RailColumn + 1;
            int expectedMemberCount = initial.Items.Count(item =>
                string.Equals(item.LayoutGroupId, NativeDesktopLayoutGroupId.GAMES.ToString(), StringComparison.Ordinal) &&
                item.IsStableIdentity && !item.IsShellNamespace);

            DesktopGroupMoveResult move = DesktopGroupDragService.MoveGroup(
                initial, NativeDesktopLayoutGroupId.GAMES, 1, 0);
            if (!move.Moved || move.IconsMoved != expectedMemberCount || !move.FilesystemUnchanged)
            {
                throw new InvalidOperationException(
                    $"PHASE_5_7_1_GAME_DRAG_FAILED Moved={move.Moved}; Reason={move.Reason}; " +
                    $"IconsMoved={move.IconsMoved}; Expected={expectedMemberCount}; FilesystemUnchanged={move.FilesystemUnchanged}");
            }

            DesktopLayoutPlan persisted = CreateAndWriteDualRailPreview(auditDirectory);
            DesktopLayoutGroup gamesAfter = persisted.Groups.Single(group =>
                group.GroupId == NativeDesktopLayoutGroupId.GAMES);
            bool mappingStillSeparated = persisted.Items
                .Where(item => string.Equals(item.ExplorerLabel, "The Seance of Blake Manor", StringComparison.Ordinal))
                .OrderBy(item => item.LabelOrdinal)
                .Select(item => item.LayoutGroupId)
                .SequenceEqual(new[] { "UNASSIGNED", "GAMES" }, StringComparer.Ordinal);
            if (!gamesAfter.HasManualAnchor || gamesAfter.RailColumn != expectedColumn ||
                gamesAfter.RailRow != gamesBefore.RailRow || !mappingStillSeparated)
            {
                throw new InvalidOperationException(
                    $"PHASE_5_7_1_GAME_DRAG_PERSISTENCE_FAILED ExpectedColumn={expectedColumn}; " +
                    $"ActualColumn={gamesAfter.RailColumn}; ManualAnchor={gamesAfter.HasManualAnchor}; " +
                    $"IdentityClassificationSeparated={mappingStillSeparated}");
            }

            var report = new StringBuilder();
            report.AppendLine("# UI Phase 5.7.1 GAMES group drag regression");
            report.AppendLine();
            report.AppendLine("GroupMoveAccepted=true");
            report.AppendLine("DeltaColumns=1");
            report.AppendLine("DeltaRows=0");
            report.AppendLine($"GameIconsMovedTogether={move.IconsMoved == expectedMemberCount}");
            report.AppendLine($"GameIconsMoved={move.IconsMoved}");
            report.AppendLine("OwnMembersAreObstacles=false");
            report.AppendLine("OwnSameLabelIconsDoNotBlock=true");
            report.AppendLine($"InternalLayoutPreserved={move.InternalLayoutPreserved}");
            report.AppendLine($"FilesystemUnchanged={move.FilesystemUnchanged}");
            report.AppendLine($"ManualPositionPersisted={gamesAfter.HasManualAnchor}");
            report.AppendLine($"PersistedColumn={gamesAfter.RailColumn}");
            report.AppendLine($"PersistedRow={gamesAfter.RailRow}");
            report.AppendLine($"IdentityClassificationStillSeparated={mappingStillSeparated}");
            File.WriteAllText(Path.Combine(auditDirectory, "ui-phase5-7-1-group-drag.md"),
                report.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// Replans and reapplies the Dual-Rail layout, then rebuilds the group
        /// headers. This is what "restore automatic position" and a manual
        /// regrouping call; it is a user gesture, never a background task.
        /// </summary>
        public static void RefreshDualRailLayoutForHeaders(string auditDirectory)
        {
            if (string.IsNullOrWhiteSpace(auditDirectory))
                auditDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audit");
            CreateAndWriteDualRailPreview(auditDirectory);
            ApplyDualRailLayout(auditDirectory);
            DesktopLayoutPlan plan = LoadDualRailPlan(auditDirectory);
            DesktopGroupHeaderManager.Show(plan, Path.Combine(auditDirectory, DualRailPlanJsonName));
        }

        public static void RestoreDualRailLayout(string auditDirectory)
        {
            DesktopGroupHeaderManager.CloseAll();
            RestoreSnapshot(auditDirectory, DualRailBaselineName, "ui-phase5-5-restore-results.md");
        }

        public static void ApplyDualRailLayout(string auditDirectory)
        {
            Directory.CreateDirectory(auditDirectory);
            string baselinePath = Path.Combine(auditDirectory, DualRailBaselineName);
            if (!File.Exists(baselinePath))
                NativeDesktopIconLayoutService.SaveSnapshot(new NativeDesktopIconLayoutService().CaptureSnapshot(), baselinePath);

            DesktopLayoutPlan plan = LoadDualRailPlan(auditDirectory);
            NativeDesktopSafeGrid safeGrid = new NativeDesktopIconLayoutService().GetSafeDesktopGrid();
            ValidateDualRailPlan(plan, safeGrid);

            List<DesktopLayoutItem> managedItems = plan.Items
                .Where(item => item.IsStableIdentity && !item.IsShellNamespace)
                .ToList();
            List<DesktopLayoutItem> eligibleItems = managedItems.Where(item => item.IsEligibleForApply).ToList();
            if (eligibleItems.Count == 0)
            {
                // Even when nothing needs to move, we must read back the actual
                // Explorer positions and verify they match the planned grid
                // slots.  Only a real read-back may set this flag.
                var readBackLayout = new NativeDesktopIconLayoutService();
                IReadOnlyList<NativeDesktopIconState> readBackIcons = readBackLayout.EnumerateDesktopIcons();
                var safeGridForCheck = readBackLayout.GetSafeDesktopGrid();
                GridSlotVerification verification = VerifyLogicalGridSlots(plan, managedItems,
                    item => readBackIcons.FirstOrDefault(icon =>
                        !string.IsNullOrEmpty(icon.OriginalPath) &&
                        string.Equals(NormalizePath(icon.OriginalPath), NormalizePath(item.OriginalPath),
                            StringComparison.OrdinalIgnoreCase)) is { } match
                        ? (match.X, match.Y)
                        : null,
                    safeGridForCheck);
                plan.RequestedVsActualValidated = true;
                plan.ExactMatches = verification.ExactMatches;
                plan.SameGridSlotMatches = verification.SameGridSlotMatches;
                plan.ExplorerClampFailures = verification.ClampFailures;
                if (verification.ClampFailures != 0)
                    throw new InvalidOperationException(
                        $"DUAL_RAIL_READ_BACK_FAILED ClampFailures={verification.ClampFailures}; " +
                        $"First={string.Join(" | ", verification.Failures.Take(5))}");
                WriteDualRailResults(auditDirectory, plan,
                    $"Apply=PASS\nItemsMoved=0\nReason=ALREADY_AT_TARGET\n" +
                    $"ExactMatches={verification.ExactMatches}\n" +
                    $"SameGridSlotMatches={verification.SameGridSlotMatches}\n" +
                    $"ExplorerClampFailures={verification.ClampFailures}");
                DesktopGroupHeaderManager.Save(plan, Path.Combine(auditDirectory, DualRailPlanJsonName));
                return;
            }

            var layout = new NativeDesktopIconLayoutService();
            NativeDesktopListViewState state = layout.GetListViewState();
            if (state.AutoArrangeEnabled)
                throw new InvalidOperationException("DUAL_RAIL_APPLY_BLOCKED_AUTO_ARRANGE_ENABLED");

            NativeDesktopLayoutSnapshot baseline = NativeDesktopIconLayoutService.LoadSnapshot(baselinePath);
            baseline = HealRestorePoint(layout, baseline, baselinePath);
            NativeDesktopSnapshotValidationResult validation = layout.ValidateSnapshot(baseline);
            if (validation.AutoArrangeEnabled || validation.Missing != 0 || validation.Ambiguous != 0)
            {
                throw new InvalidOperationException(
                    $"DUAL_RAIL_SNAPSHOT_DRY_RUN_FAILED CanResolve={validation.CanResolve}; " +
                    $"Missing={validation.Missing}; Ambiguous={validation.Ambiguous}");
            }

            NativeDesktopLayoutSnapshot before = layout.CaptureSnapshot();
            DesktopFilesystemFingerprint filesystemBefore = DesktopFilesystemFingerprint.Capture();
            List<DesktopLayoutItem> candidates = managedItems;
            if (!TargetsAreVacant(layout.EnumerateDesktopIcons(), candidates, plan, state))
                throw new InvalidOperationException("DUAL_RAIL_APPLY_BLOCKED_TARGET_SLOT_OCCUPIED");

            IReadOnlyList<(DesktopLayoutItem Item, int X, int Y)> staging =
                BuildStagingPositions(layout.EnumerateDesktopIcons(), candidates, plan, state);
            if (staging.Count != candidates.Count)
                throw new InvalidOperationException("DUAL_RAIL_APPLY_BLOCKED_NO_SAFE_STAGING_SLOTS");

            try
            {
                foreach ((DesktopLayoutItem item, int x, int y) in staging)
                {
                    if (!layout.TrySetPosition(item.OriginalPath, x, y, out _, out string reason))
                        throw new InvalidOperationException($"{item.DisplayName} staging failed: {reason}");
                }

                foreach (DesktopLayoutItem candidate in candidates)
                {
                    if (!layout.TrySetPosition(candidate.OriginalPath, candidate.TargetX, candidate.TargetY, out _, out string reason))
                        throw new InvalidOperationException($"{candidate.DisplayName} target failed: {reason}");
                }
            }
            catch
            {
                layout.Restore(before);
                throw;
            }

            NativeDesktopLayoutSnapshot after = layout.CaptureSnapshot();
            // Explorer can relocate a neighbour a moment after it processes a
            // position message, so one read-back is not proof that nothing else
            // moved. A second, immediate pass catches a displacement that landed
            // between the two reads; it costs one enumeration and no waiting.
            NativeDesktopLayoutSnapshot settled = layout.CaptureSnapshot();
            bool otherIconsChanged = FullOtherIconsChanged(before, after, candidates, plan, state) ||
                                     FullOtherIconsChanged(before, settled, candidates, plan, state);
            bool filesystemUnchanged = filesystemBefore.Equals(DesktopFilesystemFingerprint.Capture());
            List<string> mismatches = FullTargetMismatches(after, candidates, plan, state);
            bool targetsVerified = mismatches.Count == 0;
            if (otherIconsChanged || !filesystemUnchanged || !targetsVerified)
            {
                layout.Restore(before);
                throw new InvalidOperationException(
                    $"DUAL_RAIL_APPLY_VERIFICATION_FAILED OtherIconsChanged={otherIconsChanged}; " +
                    $"FilesystemUnchanged={filesystemUnchanged}; TargetSatisfied={targetsVerified}; " +
                    $"Mismatch={string.Join(" | ", mismatches.Take(10))}");
            }

            NativeDesktopSafeGrid safeGridAfter = layout.GetSafeDesktopGrid();
            GridSlotVerification applyVerification = VerifyLogicalGridSlots(plan, candidates,
                item => after.Icons.FirstOrDefault(icon =>
                    !string.IsNullOrEmpty(icon.OriginalPath) &&
                    string.Equals(NormalizePath(icon.OriginalPath), NormalizePath(item.OriginalPath),
                        StringComparison.OrdinalIgnoreCase)) is { } match
                    ? (match.X, match.Y)
                    : null,
                safeGridAfter);

            new NativeDesktopLayoutMetadataStore().PersistLayoutGroups(candidates);
            plan.RequestedVsActualValidated = true;
            plan.ExactMatches = applyVerification.ExactMatches;
            plan.SameGridSlotMatches = applyVerification.SameGridSlotMatches;
            plan.ExplorerClampFailures = applyVerification.ClampFailures;
            if (applyVerification.ClampFailures != 0)
            {
                layout.Restore(before);
                throw new InvalidOperationException(
                    $"DUAL_RAIL_APPLY_CLAMPED ClampFailures={applyVerification.ClampFailures}; " +
                    $"First={string.Join(" | ", applyVerification.Failures.Take(5))}");
            }
            var movedByGroup = eligibleItems.GroupBy(item => item.LayoutGroupId)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var result = new StringBuilder("Apply=PASS");
            result.Append('\n').Append("ItemsMoved=").Append(eligibleItems.Count);
            foreach (var pair in movedByGroup.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                result.Append('\n').Append(pair.Key).Append("PositionChanged=").Append(pair.Value);
            result.Append('\n').Append("RequestedVsActualValidated=True");
            result.Append('\n').Append("ExactMatches=").Append(plan.ExactMatches);
            result.Append('\n').Append("SameGridSlotMatches=").Append(plan.SameGridSlotMatches);
            result.Append('\n').Append("ExplorerClampFailures=").Append(plan.ExplorerClampFailures);
            WriteDualRailResults(auditDirectory, plan, result.ToString());
            DesktopGroupHeaderManager.Save(plan, Path.Combine(auditDirectory, DualRailPlanJsonName));
        }

        /// <summary>Outcome of comparing requested against actual Explorer positions.</summary>
        internal sealed class GridSlotVerification
        {
            /// <summary>Actual pixels equalled the request exactly.</summary>
            public int ExactMatches { get; set; }
            /// <summary>Explorer snapped the icon, but into the SAME logical grid slot.</summary>
            public int SameGridSlotMatches { get; set; }
            /// <summary>Explorer put the icon in a different slot, or clamped it away.</summary>
            public int ClampFailures { get; set; }
            public List<string> Failures { get; } = new();
        }

        /// <summary>
        /// Requested-versus-actual validation by LOGICAL GRID SLOT.
        ///
        /// A pixel tolerance of one whole cell used to be accepted, which meant
        /// an icon that had actually landed in the neighbouring slot still
        /// passed. The only question that matters is whether the requested and
        /// the actual coordinate resolve to the same slot of the Explorer grid,
        /// so that is what is compared. Anything else is a clamp failure.
        /// </summary>
        private static GridSlotVerification VerifyLogicalGridSlots(
            DesktopLayoutPlan plan,
            IEnumerable<DesktopLayoutItem> items,
            Func<DesktopLayoutItem, (int X, int Y)?> actualLookup,
            NativeDesktopSafeGrid safeGrid)
        {
            var verification = new GridSlotVerification();
            int spacingX = Math.Max(1, safeGrid.IconSpacingX);
            int spacingY = Math.Max(1, safeGrid.IconSpacingY);
            foreach (DesktopLayoutItem item in items)
            {
                (int X, int Y)? actual = actualLookup(item);
                if (actual == null)
                {
                    item.TargetSatisfied = false;
                    item.ReadBackReason = "ICON_NOT_FOUND";
                    verification.ClampFailures++;
                    verification.Failures.Add($"{item.DisplayName}: not found");
                    continue;
                }

                item.ActualX = actual.Value.X;
                item.ActualY = actual.Value.Y;
                item.DeltaX = actual.Value.X - item.TargetX;
                item.DeltaY = actual.Value.Y - item.TargetY;

                (int requestedColumn, int requestedRow) = ToSlot(item.TargetX, item.TargetY, plan, spacingX, spacingY);
                (int actualColumn, int actualRow) = ToSlot(actual.Value.X, actual.Value.Y, plan, spacingX, spacingY);
                bool sameSlot = requestedColumn == actualColumn && requestedRow == actualRow;
                // The safe grid is a set of slots, so judge the slot. Explorer draws
                // an icon a few pixels inside its cell; at 150% scaling the raw
                // coordinate of an icon in the last safe column overruns the pixel
                // margin even though the cell it occupies is entirely on screen.
                bool insideSafe = actualColumn >= 0 && actualRow >= 0 &&
                    actualColumn < safeGrid.SafeColumns && actualRow < safeGrid.SafeRows;

                item.TargetSatisfied = sameSlot && insideSafe;
                if (item.DeltaX == 0 && item.DeltaY == 0)
                {
                    item.ReadBackReason = "EXACT";
                    verification.ExactMatches++;
                    verification.SameGridSlotMatches++;
                }
                else if (item.TargetSatisfied)
                {
                    item.ReadBackReason = "EXPLORER_SNAP_SAME_SLOT";
                    verification.SameGridSlotMatches++;
                }
                else
                {
                    item.ReadBackReason = sameSlot ? "OUTSIDE_SAFE_GRID" : "DIFFERENT_GRID_SLOT";
                    verification.ClampFailures++;
                    verification.Failures.Add(
                        $"{item.DisplayName}: requested slot ({requestedColumn},{requestedRow}) " +
                        $"at ({item.TargetX},{item.TargetY}) but landed in slot ({actualColumn},{actualRow}) " +
                        $"at ({actual.Value.X},{actual.Value.Y})");
                }
            }
            return verification;
        }

        private static (int Column, int Row) ToSlot(int x, int y, DesktopLayoutPlan plan, int spacingX, int spacingY) =>
            ((int)Math.Round((x - plan.WorkAreaLeft) / (double)spacingX),
             (int)Math.Round((y - plan.WorkAreaTop) / (double)spacingY));

        /// <summary>
        /// Drops restore-point entries whose desktop item is gone and writes the
        /// snapshot back.
        ///
        /// The baseline is captured once and never refreshed, so every icon the
        /// user later deletes or moves off the Desktop left a dead entry behind.
        /// The preflight counted those as Missing and refused the apply, which
        /// meant a single tidy-up permanently broke every layout refresh with
        /// SNAPSHOT_DRY_RUN_FAILED until someone edited the file by hand.
        ///
        /// A deleted icon is not a hazard to a forward apply and is not
        /// restorable either way, so it is pruned here instead. Ambiguity and
        /// auto-arrange remain hard blocks: those really can put an icon back in
        /// the wrong place.
        /// </summary>
        private static NativeDesktopLayoutSnapshot HealRestorePoint(NativeDesktopIconLayoutService layout,
            NativeDesktopLayoutSnapshot baseline, string baselinePath)
        {
            NativeDesktopLayoutSnapshot healed = layout.PruneMissing(baseline, out int pruned);
            if (pruned == 0) return baseline;

            NativeDesktopIconLayoutService.ReplaceSnapshot(healed, baselinePath);
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                $"Restore point healed: dropped {pruned} entr{(pruned == 1 ? "y" : "ies")} whose desktop item no longer exists.");
            return healed;
        }

        /// <summary>The same non-mutating gates apply to preview and commit.</summary>
        internal static void ValidateDualRailPlan(DesktopLayoutPlan plan, NativeDesktopSafeGrid safeGrid)
        {
            int outOfSafeGrid = plan.Items.Count(item => item.IsStableIdentity && !item.IsShellNamespace &&
                (item.TargetX - plan.WorkAreaLeft + safeGrid.IconSpacingX + safeGrid.RightSafetyMargin > safeGrid.ClientWidth ||
                 item.TargetY - plan.WorkAreaTop + safeGrid.IconSpacingY + safeGrid.BottomSafetyMargin > safeGrid.ClientHeight));
            if (plan.AutoArrangeEnabled)
                throw new InvalidOperationException("DUAL_RAIL_APPLY_BLOCKED_AUTO_ARRANGE_ENABLED");
            if (plan.DuplicateTargetCoordinates != 0 || plan.TargetOutsideDesktopWorkArea != 0 ||
                !plan.CentralClearZonePreserved || outOfSafeGrid != 0)
            {
                throw new InvalidOperationException(
                    $"DUAL_RAIL_DRY_RUN_FAILED DuplicateTargets={plan.DuplicateTargetCoordinates}; " +
                    $"OutOfBounds={plan.TargetOutsideDesktopWorkArea}; OutOfSafeGrid={outOfSafeGrid}; " +
                    $"CentralViolations={!plan.CentralClearZonePreserved}");
            }
        }

        internal static string GetDualRailPlanPath(string auditDirectory) =>
            Path.Combine(auditDirectory, DualRailPlanJsonName);

        private static DesktopLayoutPlan LoadDualRailPlan(string auditDirectory)
        {
            string path = Path.Combine(auditDirectory, DualRailPlanJsonName);
            if (!File.Exists(path))
                return CreateAndWriteDualRailPreview(auditDirectory);
            return JsonConvert.DeserializeObject<DesktopLayoutPlan>(File.ReadAllText(path, Encoding.UTF8))
                ?? throw new InvalidDataException("Dual-Rail layout plan is empty or invalid.");
        }

        private static void WriteDualRailResults(string auditDirectory, DesktopLayoutPlan plan, string header)
        {
            var markdown = new StringBuilder("# UI Phase 5.5 Dual-Rail Layout Results\n\n");
            markdown.AppendLine(header);
            markdown.AppendLine();
            markdown.AppendLine($"DesktopWorkArea={plan.WorkAreaWidth}x{plan.WorkAreaHeight}");
            markdown.AppendLine($"DesktopListViewClientRect={plan.ListViewClientWidth}x{plan.ListViewClientHeight}");
            markdown.AppendLine($"IconSpacingX={plan.GridSpacingX}");
            markdown.AppendLine($"IconSpacingY={plan.GridSpacingY}");
            markdown.AppendLine($"SafeColumns={plan.SafeColumns}");
            markdown.AppendLine($"SafeRows={plan.SafeRows}");
            markdown.AppendLine($"RightSafetyMargin={plan.SafeRightMargin}");
            markdown.AppendLine($"BottomSafetyMargin={plan.SafeBottomMargin}");
            markdown.AppendLine($"StableIconCount={plan.Items.Count(item => item.IsStableIdentity)}");
            markdown.AppendLine($"AmbiguousCount={plan.Items.Count(item => !item.IsStableIdentity && !item.IsShellNamespace)}");
            markdown.AppendLine($"ShellNamespaceCount={plan.Items.Count(item => item.IsShellNamespace)}");
            markdown.AppendLine($"RequestedVsActualValidated={plan.RequestedVsActualValidated}");
            markdown.AppendLine($"ExactMatches={plan.ExactMatches}");
            markdown.AppendLine($"SameGridSlotMatches={plan.SameGridSlotMatches}");
            markdown.AppendLine($"ExplorerClampFailures={plan.ExplorerClampFailures}");
            markdown.AppendLine($"LeftRailX={plan.LeftRailX} (column {plan.LeftRailColumn})");
            markdown.AppendLine($"RightRailX={plan.RightRailX} (column {plan.RightRailColumn})");
            markdown.AppendLine($"LeftRailAligned={plan.LeftRailAligned}");
            markdown.AppendLine($"RightRailAligned={plan.RightRailAligned}");
            markdown.AppendLine($"LeftRailStacks={plan.LeftRailStackCount}");
            markdown.AppendLine($"RightRailStacks={plan.RightRailStackCount}");
            markdown.AppendLine($"GroupSpacingConsistent={plan.GroupSpacingConsistent}");
            markdown.AppendLine($"HeaderToFirstIconGap={plan.HeaderToFirstIconGap}");
            markdown.AppendLine($"GroupSeparationGap={plan.GroupSeparationGap}");
            markdown.AppendLine($"LeftRailSpareRows={plan.LeftRailColumnSlack}");
            markdown.AppendLine($"RightRailSpareRows={plan.RightRailColumnSlack}");
            markdown.AppendLine($"SystemTempHeaderShown={plan.Groups.Any(g => g.GroupId == NativeDesktopLayoutGroupId.SYSTEM_TEMP && g.ShowHeader)}");
            markdown.AppendLine($"ManualGroupAnchors={plan.Groups.Count(g => g.HasManualAnchor)}");
            markdown.AppendLine($"ManagedIconsOutsideAssignedBounds={plan.ManagedIconsOutsideAssignedBounds}");
            markdown.AppendLine($"UnassignedIconsOutsideUnassignedBounds={plan.UnassignedIconsOutsideUnassignedBounds}");
            markdown.AppendLine($"CentralClearZone={plan.CentralClearZoneLeft},{plan.CentralClearZoneTop} {plan.CentralClearZoneWidth}x{plan.CentralClearZoneHeight}");
            markdown.AppendLine($"CentralClearZoneViolations={(!plan.CentralClearZonePreserved ? 1 : 0)}");
            markdown.AppendLine($"DuplicateTargetCoordinates={plan.DuplicateTargetCoordinates}");
            markdown.AppendLine($"TargetOutsideWorkArea={plan.TargetOutsideDesktopWorkArea}");
            markdown.AppendLine($"VisualQualityGate={(FullVisualQualityGatePasses(plan) ? "PASS" : "FAIL")}");
            markdown.AppendLine("GroupHeadersVisible=true");
            markdown.AppendLine("FilesystemMove=false");
            markdown.AppendLine("FilesystemDelete=false");
            markdown.AppendLine("FilesystemRename=false");
            markdown.AppendLine("FilesystemCopy=false");
            File.WriteAllText(Path.Combine(auditDirectory, "ui-phase5-5-apply-results.md"),
                markdown.ToString(), new UTF8Encoding(false));
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
            baseline = HealRestorePoint(layout, baseline, baselinePath);
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
            baseline = HealRestorePoint(layout, baseline, baselinePath);
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

        private static List<string> FullTargetMismatches(
            NativeDesktopLayoutSnapshot after,
            IReadOnlyList<DesktopLayoutItem> candidates,
            DesktopLayoutPlan plan,
            NativeDesktopListViewState state)
        {
            Drawing.Rectangle work = new(plan.WorkAreaLeft, plan.WorkAreaTop, plan.WorkAreaWidth, plan.WorkAreaHeight);
            int gridX = Math.Max(1, state.GridSpacingX);
            int gridY = Math.Max(1, state.GridSpacingY);
            var current = after.Icons.Where(item => !string.IsNullOrEmpty(item.OriginalPath))
                .ToDictionary(item => NormalizePath(item.OriginalPath), StringComparer.OrdinalIgnoreCase);
            var failures = new List<string>();
            foreach (DesktopLayoutItem candidate in candidates)
            {
                if (!current.TryGetValue(NormalizePath(candidate.OriginalPath), out NativeDesktopIconSnapshotItem? item))
                {
                    failures.Add($"{candidate.DisplayName}: MISSING");
                    continue;
                }

                string actual = NativeDesktopLayoutPlanner.SlotKey(item.X, item.Y, work, gridX, gridY);
                string target = NativeDesktopLayoutPlanner.SlotKey(candidate.TargetX, candidate.TargetY, work, gridX, gridY);
                if (!string.Equals(actual, target, StringComparison.Ordinal))
                    failures.Add($"{candidate.DisplayName}: actual={item.X},{item.Y} target={candidate.TargetX},{candidate.TargetY}");
            }
            return failures;
        }

        private static bool TargetsVerified(NativeDesktopLayoutSnapshot after, IReadOnlyList<DesktopLayoutItem> candidates,
            DesktopLayoutPlan plan, NativeDesktopListViewState state)
        {
            Drawing.Rectangle work = new(plan.WorkAreaLeft, plan.WorkAreaTop, plan.WorkAreaWidth, plan.WorkAreaHeight);
            var current = after.Icons.Where(item => !string.IsNullOrEmpty(item.OriginalPath))
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

        private static bool FullOtherIconsChanged(
            NativeDesktopLayoutSnapshot before,
            NativeDesktopLayoutSnapshot after,
            IReadOnlyList<DesktopLayoutItem> candidates,
            DesktopLayoutPlan plan,
            NativeDesktopListViewState state)
        {
            Drawing.Rectangle work = new(plan.WorkAreaLeft, plan.WorkAreaTop, plan.WorkAreaWidth, plan.WorkAreaHeight);
            int gridX = Math.Max(1, state.GridSpacingX);
            int gridY = Math.Max(1, state.GridSpacingY);
            var selected = candidates.Select(item => NormalizePath(item.OriginalPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<(NativeIconIdentityStatus Status, string Path, string Name, int X, int Y)> SnapshotOthers(NativeDesktopLayoutSnapshot snapshot) =>
                snapshot.Icons
                    .Where(item => !(!string.IsNullOrEmpty(item.OriginalPath) &&
                                     selected.Contains(NormalizePath(item.OriginalPath))))
                    .Select(item => (item.IdentityStatus, NormalizePath(item.OriginalPath), item.DisplayName,
                                     item.X, item.Y))
                    .OrderBy(entry => entry.Item1)
                    .ThenBy(entry => entry.Item2, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Item3, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(entry => entry.Item4)
                    .ThenBy(entry => entry.Item5)
                    .ToList();

            return !SnapshotOthers(before).SequenceEqual(SnapshotOthers(after));
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
                if (!string.IsNullOrEmpty(entry.item.OriginalPath) && selected.Contains(NormalizePath(entry.item.OriginalPath))) continue;
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
            string[] queries = { "Codex", "论文", "PDF", "Chrome", "Steam" };
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
            File.WriteAllText(Path.Combine(auditDirectory, "ui-phase5-5-search-smoke.md"), markdown.ToString(), new UTF8Encoding(false));
            if (!passed) throw new InvalidOperationException("SPOT_SEARCH_SMOKE_FAILED");
        }

        private static void WriteBatch(string auditDirectory, NativeDesktopLayoutBatchResult result, IReadOnlyList<DesktopLayoutItem> candidates,
            IReadOnlyList<NativeDesktopIconSnapshotItem> before, IReadOnlyList<NativeDesktopIconSnapshotItem> after,
            string auditPrefix = "ui-phase5-3")
        {
            var afterByPath = after.Where(item => !string.IsNullOrEmpty(item.OriginalPath))
                .GroupBy(item => NormalizePath(item.OriginalPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var beforeByPath = before.Where(item => !string.IsNullOrEmpty(item.OriginalPath))
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
        /// <summary>
        /// Names and kinds of the visible Desktop entries, without their sizes
        /// or timestamps. This is exactly the "nothing was moved, deleted,
        /// renamed, or copied" property, and it does not trip over an unrelated
        /// process writing inside a Desktop folder while a move is in flight.
        /// </summary>
        public static DesktopFilesystemFingerprint CaptureNames()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            IEnumerable<string> entries = Directory.Exists(desktop)
                ? Directory.EnumerateFileSystemEntries(desktop, "*", SearchOption.TopDirectoryOnly)
                    .Select(path => $"{Path.GetFileName(path)}|{(Directory.Exists(path) ? "D" : "F")}")
                : Enumerable.Empty<string>();
            return new DesktopFilesystemFingerprint(entries);
        }

        public bool Equals(DesktopFilesystemFingerprint? other) => other != null && _entries.SequenceEqual(other._entries, StringComparer.OrdinalIgnoreCase);
        public override bool Equals(object? obj) => Equals(obj as DesktopFilesystemFingerprint);
        public override int GetHashCode() => _entries.Count;
    }
}








