using System;
using System.Collections.Generic;

namespace Desktop_Frames.NativeDesktop
{
    public enum NativeDesktopLayoutGroupId
    {
        RESEARCH_STUDY,
        AI_DEV,
        OFFICE_DOCUMENTS,
        DAILY_TOOLS,
        GAMES,
        SYSTEM_TEMP,
        UNASSIGNED
    }

    /// <summary>Serializable, side-effect-free result of native Desktop layout planning.</summary>
    public sealed class DesktopLayoutPlan
    {
        public int Version { get; init; } = 1;
        public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
        public int WorkAreaLeft { get; init; }
        public int WorkAreaTop { get; init; }
        public int WorkAreaWidth { get; init; }
        public int WorkAreaHeight { get; init; }
        public int Dpi { get; init; } = 96;
        public int GridSpacingX { get; init; }
        public int GridSpacingY { get; init; }
        public bool AutoArrangeEnabled { get; init; }
        public bool AlignToGridEnabled { get; init; }
        public bool PrimaryMonitorOnly { get; init; } = true;
        public List<DesktopLayoutGroup> Groups { get; init; } = new();
        public List<DesktopLayoutItem> Items { get; init; } = new();
        public int DuplicateTargetCoordinates { get; set; }
        public int TargetOutsideDesktopWorkArea { get; set; }
    }

    public sealed class DesktopLayoutGroup
    {
        public NativeDesktopLayoutGroupId GroupId { get; init; }
        public string GroupDisplayName { get; init; } = string.Empty;
        public string Region { get; init; } = string.Empty;
        public int PlannedItemCount { get; set; }
        public int EligibleItemCount { get; set; }
    }

    public sealed class DesktopLayoutItem
    {
        public string LayoutGroupId { get; init; } = string.Empty;
        public string GroupDisplayName { get; init; } = string.Empty;
        public string OriginalPath { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public int CurrentX { get; init; }
        public int CurrentY { get; init; }
        public int TargetX { get; set; }
        public int TargetY { get; set; }
        public string ClassificationConfidence { get; init; } = string.Empty;
        public string ClassificationReason { get; init; } = string.Empty;
        public bool IsStableIdentity { get; init; }
        public bool IsShellNamespace { get; init; }
        public bool IsManualOverride { get; init; }
        public bool IsEligibleForApply { get; set; }
        public string ApplyReason { get; set; } = string.Empty;
    }

    public sealed class NativeDesktopLayoutBatchResult
    {
        public string BatchName { get; init; } = string.Empty;
        public NativeDesktopLayoutGroupId GroupId { get; init; }
        public bool Applied { get; set; }
        public string Reason { get; set; } = string.Empty;
        public int Intended { get; set; }
        public int Moved { get; set; }
        public bool OtherUnmanagedIconsChanged { get; set; }
        public bool FilesystemUnchanged { get; set; }
        public List<string> Errors { get; } = new();
    }
}
