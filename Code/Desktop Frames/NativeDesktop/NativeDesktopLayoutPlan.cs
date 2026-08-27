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

    public enum NativeDesktopLayoutMode
    {
        Legacy,
        CompactGrouped,
        DualRail
    }

    public enum DesktopRail
    {
        Left,
        Right,
        Fixed
    }

    /// <summary>Serializable, side-effect-free result of native Desktop layout planning.</summary>
    public sealed class DesktopLayoutPlan
    {
        public int Version { get; init; } = 2;
        public string LayoutMode { get; init; } = NativeDesktopLayoutMode.CompactGrouped.ToString();
        public string UnassignedLayoutMode { get; init; } = "PRESERVE";
        public int ListViewClientWidth { get; init; }
        public int ListViewClientHeight { get; init; }
        public int SafeColumns { get; init; }
        public int SafeRows { get; init; }
        public int SafeRightMargin { get; init; }
        public int SafeBottomMargin { get; init; }
        public bool RequestedVsActualValidated { get; set; }
        public int ExplorerClampFailures { get; set; }
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
        public int CentralClearZoneLeft { get; set; }
        public int CentralClearZoneTop { get; set; }
        public int CentralClearZoneWidth { get; set; }
        public int CentralClearZoneHeight { get; set; }
        public bool CentralClearZonePreserved { get; set; }
        /// <summary>Single X baseline every left-rail group is aligned to.</summary>
        public int LeftRailX { get; set; }
        /// <summary>Single X baseline every right-rail group is aligned to.</summary>
        public int RightRailX { get; set; }
        public int LeftRailColumn { get; set; }
        public int RightRailColumn { get; set; }
        public bool LeftRailAligned { get; set; }
        public bool RightRailAligned { get; set; }
        public bool GroupSpacingConsistent { get; set; }
        /// <summary>Spare grid rows left at the bottom of each rail.</summary>
        public int LeftRailColumnSlack { get; set; }
        public int RightRailColumnSlack { get; set; }
        public int HeaderToFirstIconGap { get; set; } = DesktopLayoutSpacing.HeaderToFirstIcon;
        public int GroupSeparationGap { get; set; }
        /// <summary>Read-back items whose actual pixels equalled the request exactly.</summary>
        public int ExactMatches { get; set; }
        /// <summary>Read-back items Explorer snapped inside the SAME logical grid slot.</summary>
        public int SameGridSlotMatches { get; set; }
        public int ManagedIconsOutsideAssignedBounds { get; set; }
        public int UnassignedIconsOutsideUnassignedBounds { get; set; }
        public int DuplicateTargetCoordinates { get; set; }
        public int TargetOutsideDesktopWorkArea { get; set; }
    }

    public sealed class DesktopLayoutGroup
    {
        public NativeDesktopLayoutGroupId GroupId { get; init; }
        public string GroupDisplayName { get; init; } = string.Empty;
        public string Region { get; init; } = string.Empty;
        public string Anchor { get; init; } = string.Empty;
        public DesktopRail Rail { get; init; } = DesktopRail.Fixed;
        public int HomeRegionLeft { get; init; }
        public int HomeRegionTop { get; init; }
        public int HomeRegionRight { get; init; }
        public int HomeRegionBottom { get; init; }
        public int Columns { get; set; }
        public int Rows { get; set; }
        public int BoundingWidthGridSlots { get; set; }
        public int BoundingHeightGridSlots { get; set; }
        public double AspectRatio { get; set; }
        public int FragmentationCount { get; set; }
        public bool UsedFallbackRegion { get; set; }
        public int PlannedItemCount { get; set; }
        public int EligibleItemCount { get; set; }
        public int HeaderLeft { get; set; }
        public int HeaderTop { get; set; }
        public int HeaderWidth { get; set; }
        public int HeaderHeight { get; set; }
        public int IconBoundingLeft { get; set; }
        public int IconBoundingTop { get; set; }
        public int IconBoundingRight { get; set; }
        public int IconBoundingBottom { get; set; }
        /// <summary>Extra columns this group was widened by so its rail fit the screen.</summary>
        public int ExtraColumns { get; set; }

        /// <summary>Grid column the group's first icon column occupies.</summary>
        public int RailColumn { get; set; }
        /// <summary>Grid row the group's first icon row occupies.</summary>
        public int RailRow { get; set; }
        /// <summary>A visually weightless group (SYSTEM_TEMP) reserves its rhythm row but draws no header.</summary>
        public bool ShowHeader { get; set; } = true;
        /// <summary>True when the user dragged this group and the position is pinned.</summary>
        public bool HasManualAnchor { get; set; }
        /// <summary>True when the user locked the group so its header refuses to drag.</summary>
        public bool Locked { get; set; }
    }

    /// <summary>
    /// The single spacing system every Dual-Rail group is laid out with. No
    /// group computes its own margins.
    /// </summary>
    public static class DesktopLayoutSpacing
    {
        /// <summary>Gap between a header baseline and the first icon row below it.</summary>
        public const int HeaderToFirstIcon = 14;
        /// <summary>Header chrome height in device-independent pixels.</summary>
        public const int HeaderHeight = 26;
        /// <summary>Rows reserved above every group, header drawn or not, so the rhythm never varies.</summary>
        public const int GroupLeadingRows = 1;
    }

    public sealed class DesktopLayoutItem
    {
        public string LayoutGroupId { get; set; } = string.Empty;
        public string GroupDisplayName { get; set; } = string.Empty;
        public string OriginalPath { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public int CurrentX { get; init; }
        public int CurrentY { get; init; }
        public int TargetX { get; set; }
        public int TargetY { get; set; }
        public string ClassificationConfidence { get; init; } = string.Empty;
        public string ClassificationReason { get; set; } = string.Empty;
        public bool IsStableIdentity { get; init; }
        public bool IsShellNamespace { get; init; }
        public bool IsManualOverride { get; set; }
        public string LayoutArea { get; set; } = string.Empty;
        public bool IsEligibleForApply { get; set; }
        public string ApplyReason { get; set; } = string.Empty;
        public int ActualX { get; set; }
        public int ActualY { get; set; }
        public int DeltaX { get; set; }
        public int DeltaY { get; set; }
        public bool TargetSatisfied { get; set; }
        public string ReadBackReason { get; set; } = string.Empty;

        /// <summary>
        /// Records a grouping the user chose by hand. A manual group always
        /// outranks ReferenceGroupingClassifier on every later plan.
        /// </summary>
        public void SetManualGroup(NativeDesktopLayoutGroupId group, string displayName)
        {
            LayoutGroupId = group.ToString();
            GroupDisplayName = displayName;
            IsManualOverride = true;
            ClassificationReason = "ManualLayoutGroupId chosen by the user.";
        }
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

