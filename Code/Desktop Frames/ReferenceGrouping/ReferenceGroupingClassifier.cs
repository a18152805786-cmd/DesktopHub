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
                new[] { "zotero", "hunnu", "research", "论文", "科研", "文献", "数据项目", "科研相关skills" },
                new[] { "study", "学习", "资料", "项目" }),
            new Rule(ReferenceGroup.AiDev,
                new[] { "codex", "kimi", "visual studio code", "vscode", "zcode", "claude", "github", "git bash", "git gui", "wsl", "powershell", "terminal", "ai工具" },
                new[] { "developer", "开发" }),
            new Rule(ReferenceGroup.WorkTools,
                new[] { "winword", "microsoft word", "powerpoint", "powerpnt", "excel", "stata", "chrome" },
                new[] { "office", "工作" }),
            new Rule(ReferenceGroup.Games,
                new[] { "steam://", "steam.exe", "steam", "battle.net", "blizzard", "hearthstone", "firestone", "hades", "游戏" },
                new[] { "game", "mod", "bepinex" }),
            new Rule(ReferenceGroup.DailyTools,
                new[] { "微信", "wechat", "百度网盘", "夸克网盘", "vpn", "msi afterburner", "pixel repair" },
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
            foreach (Rule rule in Rules)
            {
                string? displayMatch = rule.StrongTerms.FirstOrDefault(term => display.Contains(Normalize(term)));
                if (displayMatch != null)
                {
                    return new ReferenceGroupingDecision(rule.Group, GroupingConfidence.High,
                        $"DisplayName matched '{displayMatch}'.");
                }
            }

            var metadataSources = new[]
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
            ReferenceGroup.WorkTools => Strings.GroupWork,
            ReferenceGroup.Games => Strings.GroupGames,
            ReferenceGroup.DailyTools => Strings.GroupDaily,
            _ => Strings.GroupingUnassigned
        };

        private static string Normalize(string? value) =>
            (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}
