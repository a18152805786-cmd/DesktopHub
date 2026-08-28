using Desktop_Frames.ReferenceGrouping;
using System;
using System.Collections.Generic;
using System.IO;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// Stage B result. Unlike NativeDesktopIconIdentityResolver, this DTO may
    /// own a GroupId because it represents classification of one resolved path.
    /// </summary>
    internal sealed record ClassifiedDesktopItem(
        NativeDesktopIconState NativeIdentity,
        NativeDesktopLayoutGroupId FinalGroup,
        string GroupDisplayName,
        string ClassifierGroup,
        string Confidence,
        bool IsManualOverride,
        string Reason,
        ReferenceFrameItem? Reference);

    /// <summary>
    /// Stage B orchestration. Exactly one resolved candidate path enters the
    /// classifier. Candidate lists and shared Explorer labels are never inputs
    /// to a classification decision.
    /// </summary>
    internal static class NativeDesktopItemClassificationService
    {
        public static ClassifiedDesktopItem Classify(
            NativeDesktopIconState identity,
            IReadOnlyDictionary<string, NativeDesktopLayoutGroupId> manualLayoutGroups,
            ISet<string> manualButUnmappedPaths)
        {
            if (!identity.IdentityStatus.IsResolved() || string.IsNullOrWhiteSpace(identity.OriginalPath))
            {
                return Result(identity, NativeDesktopLayoutGroupId.UNASSIGNED, "UNASSIGNED",
                    identity.IdentityStatus.ToString(), false,
                    "Unresolved identity is an occupied obstacle and is never moved.", null);
            }

            string path = NormalizePath(identity.OriginalPath);
            ReferenceFrameItem? reference = TryCreateReference(path);

            if (manualLayoutGroups.TryGetValue(path, out NativeDesktopLayoutGroupId manualGroup))
            {
                return Result(identity, manualGroup, "SKIPPED_MANUAL_OVERRIDE", "HIGH", true,
                    "ManualLayoutGroupId is authoritative; automatic classification is skipped.", reference);
            }

            if (manualButUnmappedPaths.Contains(path))
            {
                return Result(identity, NativeDesktopLayoutGroupId.UNASSIGNED, "SKIPPED_MANUAL_OVERRIDE",
                    "HIGH", true,
                    "ManualFrameId is authoritative but has no mapped LayoutGroupId; icon remains in place.",
                    reference);
            }

            if (reference == null)
            {
                return Result(identity, NativeDesktopLayoutGroupId.UNASSIGNED, "UNASSIGNED", "UNASSIGNED",
                    false, "Desktop item is not a supported shortcut, URL, or folder.", null);
            }

            ReferenceGroupingDecision decision = ReferenceGroupingClassifier.Classify(reference);
            NativeDesktopLayoutGroupId group = Map(decision.Group);
            return Result(identity, group, group.ToString(), decision.Confidence.ToString().ToUpperInvariant(),
                decision.IsManualOverride, decision.Reason, reference);
        }

        private static ClassifiedDesktopItem Result(
            NativeDesktopIconState identity,
            NativeDesktopLayoutGroupId finalGroup,
            string classifierGroup,
            string confidence,
            bool isManualOverride,
            string reason,
            ReferenceFrameItem? reference) =>
            new(identity, finalGroup, DisplayName(finalGroup), classifierGroup, confidence,
                isManualOverride, reason, reference);

        private static ReferenceFrameItem? TryCreateReference(string path)
        {
            if (!ReferenceFrameItem.IsSupportedPath(path)) return null;
            try { return ReferenceFrameItem.Create(path); }
            catch { return null; }
        }

        private static NativeDesktopLayoutGroupId Map(ReferenceGroup group) => group switch
        {
            ReferenceGroup.ResearchStudy => NativeDesktopLayoutGroupId.RESEARCH_STUDY,
            ReferenceGroup.AiDev => NativeDesktopLayoutGroupId.AI_DEV,
            ReferenceGroup.OfficeDocuments or ReferenceGroup.WorkTools => NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS,
            ReferenceGroup.DailyTools => NativeDesktopLayoutGroupId.DAILY_TOOLS,
            ReferenceGroup.Games => NativeDesktopLayoutGroupId.GAMES,
            ReferenceGroup.SystemTemp => NativeDesktopLayoutGroupId.SYSTEM_TEMP,
            _ => NativeDesktopLayoutGroupId.UNASSIGNED
        };

        private static string DisplayName(NativeDesktopLayoutGroupId group) => group switch
        {
            NativeDesktopLayoutGroupId.RESEARCH_STUDY => "科研 · 学习",
            NativeDesktopLayoutGroupId.AI_DEV => "AI · 开发",
            NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS => "办公 · 文档",
            NativeDesktopLayoutGroupId.DAILY_TOOLS => "日常工具",
            NativeDesktopLayoutGroupId.GAMES => "游戏",
            NativeDesktopLayoutGroupId.SYSTEM_TEMP => "系统 · 临时",
            _ => "待整理"
        };

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return path?.Trim() ?? string.Empty; }
        }
    }
}
