using System;
using System.Collections.Generic;
using System.Linq;
using Desktop_Frames.Localization;

namespace Desktop_Frames.ReferenceGrouping
{
    internal enum ReferenceGroup
    {
        AiDev,
        ResearchStudy,
        OfficeDocuments,
        SystemTemp,
        // Retained for existing serialized/reference-frame callers. New
        // classifications use OfficeDocuments instead.
        WorkTools,
        Games,
        DailyTools,
        Unassigned
    }

    internal enum GroupingConfidence
    {
        High,
        Medium,
        Low,
        Unassigned
    }

    internal sealed record ReferenceGroupingDecision(
        ReferenceGroup Group,
        GroupingConfidence Confidence,
        string Reason,
        bool IsManualOverride = false);

    internal static class ReferenceGroupingClassifier
    {
        private sealed record Rule(ReferenceGroup Group, string[] StrongTerms, string[] WeakTerms);

        private static readonly IReadOnlyList<Rule> Rules = new[]
        {
            // Research terms precede general AI-tool terms so mixed names such as
            // "科研相关Skills-KimiWork" keep their more specific user meaning.
            new Rule(ReferenceGroup.ResearchStudy,
                new[] { "zotero", "hunnu", "research", "stata", "obsidian", "论文", "科研", "文献", "数据项目", "科研相关skills", "论文下载", "ai工具学习", "graduate", "研究资料", "研究" },
                new[] { "study", "学习", "资料", "项目" }),
            new Rule(ReferenceGroup.AiDev,
                new[] { "codex", "kimi", "visual studio code", "vscode", "zcode", "claude", "workbuddy", "github", "git bash", "git gui", "wsl", "powershell", "terminal", "ai工具", ".ssh" },
                new[] { "developer", "开发" }),
            new Rule(ReferenceGroup.OfficeDocuments,
                new[] { "文档", "ppt", "pdf", "数据表格", "图片", "压缩包", "安装包", "libreoffice", "winword", "microsoft word", "powerpoint", "powerpnt", "excel" },
                new[] { "office", "工作" }),
            new Rule(ReferenceGroup.Games,
                new[] { "steam://", "steam.exe", "steam", "battle.net", "blizzard", "hearthstone", "firestone", "hades", "游戏" },
                new[] { "game", "mod", "bepinex" }),
            new Rule(ReferenceGroup.SystemTemp,
                new[] { "temp", "logs" },
                Array.Empty<string>()),
            new Rule(ReferenceGroup.DailyTools,
                new[] { "google chrome", "chrome", "microsoft edge", "edge", "微信", "wechat", "百度网盘", "夸克网盘", "vpn", "clash verge", "clash", "run-clash", "nvidia app", "logitech g hub", "revo uninstaller", "msi afterburner", "pixel repair", "free download manager", "creative app" },
                new[] { "网盘", "utility", "工具" })
        };

        public static ReferenceGroupingDecision Classify(ReferenceFrameItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.ManualFrameId))
            {
                return new ReferenceGroupingDecision(
                    ReferenceGroup.Unassigned,
                    GroupingConfidence.High,
                    "ManualFrameId is authoritative; automatic classification is skipped.",
                    true);
            }

            string display = Normalize(item.DisplayName);
            string targetName = string.IsNullOrWhiteSpace(item.TargetPath)
                ? string.Empty
                : System.IO.Path.GetFileName(item.TargetPath.TrimEnd(
                    System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.AltDirectorySeparatorChar));
            // Folder names are intentionally evaluated last. A shortcut's
            // visible name is stronger evidence than its target/arguments/URI;
            // an ambiguous folder name is not enough to move an icon.
            if (item.ReferenceType != ReferenceType.Folder)
            {
                foreach (Rule rule in Rules)
                {
                    string? displayMatch = rule.StrongTerms.FirstOrDefault(term => display.Contains(Normalize(term)));
                    if (displayMatch != null)
                    {
                        return new ReferenceGroupingDecision(rule.Group, GroupingConfidence.High,
                            $"DisplayName matched '{displayMatch}'.");
                    }
                }
            }

            string? gameLocation = GameLibraryEvidence(item);
            if (gameLocation != null)
            {
                return new ReferenceGroupingDecision(ReferenceGroup.Games, GroupingConfidence.High,
                    $"TargetPath is inside a game library directory '{gameLocation}'.");
            }

            var metadataSources = item.ReferenceType == ReferenceType.Folder
                ? new[]
                {
                    (Label: "Arguments", Value: Normalize(item.Arguments)),
                    (Label: "Uri", Value: Normalize(item.Uri))
                }
                : new[]
                {
                    (Label: "TargetPath", Value: Normalize(targetName)),
                    (Label: "Arguments", Value: Normalize(item.Arguments)),
                    (Label: "Uri", Value: Normalize(item.Uri))
                };
            foreach (var source in metadataSources)
            {
                foreach (Rule rule in Rules)
                {
                    string? metadataMatch = rule.StrongTerms.FirstOrDefault(term =>
                        source.Value.Contains(Normalize(term)));
                    if (metadataMatch != null)
                    {
                        return new ReferenceGroupingDecision(rule.Group, GroupingConfidence.High,
                            $"{source.Label} matched '{metadataMatch}'.");
                    }
                }
            }

            if (item.ReferenceType == ReferenceType.Folder)
            {
                foreach (Rule rule in Rules)
                {
                    string? folderMatch = rule.StrongTerms.FirstOrDefault(term =>
                        rule.Group == ReferenceGroup.OfficeDocuments
                            ? string.Equals(display, Normalize(term), StringComparison.Ordinal)
                            : display.Contains(Normalize(term)));
                    if (folderMatch != null)
                    {
                        return new ReferenceGroupingDecision(rule.Group, GroupingConfidence.High,
                            $"FolderName matched '{folderMatch}'.");
                    }
                }
            }

            string weakMetadata = Normalize(string.Join(" ", metadataSources.Select(source => source.Value)));
            foreach (Rule rule in Rules)
            {
                string? weakMatch = rule.WeakTerms.FirstOrDefault(term =>
                    display.Contains(Normalize(term)) || weakMetadata.Contains(Normalize(term)));
                if (weakMatch != null)
                {
                    return new ReferenceGroupingDecision(rule.Group, GroupingConfidence.Medium,
                        $"Only a broad keyword matched '{weakMatch}'.");
                }
            }

            return new ReferenceGroupingDecision(ReferenceGroup.Unassigned,
                GroupingConfidence.Unassigned, "No high-confidence rule matched.");
        }

        public static string GroupTitle(ReferenceGroup group) => group switch
        {
            ReferenceGroup.AiDev => Strings.GroupAiDev,
            ReferenceGroup.ResearchStudy => Strings.GroupResearch,
            ReferenceGroup.OfficeDocuments => Strings.GroupWork,
            ReferenceGroup.SystemTemp => "系统 · 临时",
            ReferenceGroup.WorkTools => Strings.GroupWork,
            ReferenceGroup.Games => Strings.GroupGames,
            ReferenceGroup.DailyTools => Strings.GroupDaily,
            _ => Strings.GroupingUnassigned
        };

        /// <summary>
        /// Returns the directory segment that proves a shortcut target lives in
        /// a game library, or null. Matching whole path segments - not a bare
        /// substring of the full path - keeps an unrelated folder from being
        /// swept in by an accidental letter sequence.
        /// </summary>
        private static string? GameLibraryEvidence(ReferenceFrameItem item)
        {
            if (item.ReferenceType == ReferenceType.Folder) return null;
            if (string.IsNullOrWhiteSpace(item.TargetPath)) return null;

            string[] segments;
            try
            {
                segments = item.TargetPath.Split(
                    new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
            }
            catch
            {
                return null;
            }

            // The last segment is the file itself; only its parent directories
            // are evidence about where the target is installed.
            for (int index = 0; index < segments.Length - 1; index++)
            {
                string segment = segments[index];
                if (segment.EndsWith("games", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    return segment;
                }
            }
            return null;
        }

        private static string Normalize(string? value) =>
            (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}
