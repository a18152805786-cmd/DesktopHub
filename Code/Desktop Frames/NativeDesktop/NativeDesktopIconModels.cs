using System;
using System.Collections.Generic;

namespace Desktop_Frames.NativeDesktop
{
    public enum NativeIconIdentityStatus
    {
        // Preserve 0/1/2 for compatibility with existing numeric snapshots.
        Exact = 0,
        Ambiguous = 1,
        ShellNamespace = 2,
        OrdinalResolved = 3,
        Unresolved = 4
    }

    public enum NativeDesktopCandidateType
    {
        Directory,
        LinkShortcut,
        InternetShortcut,
        RegularFile
    }

    public static class NativeIconIdentityStatusExtensions
    {
        public static bool IsResolved(this NativeIconIdentityStatus status) =>
            status is NativeIconIdentityStatus.Exact or NativeIconIdentityStatus.OrdinalResolved;
    }

    public sealed class NativeDesktopIconState
    {
        public int RuntimeIndex { get; init; }
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>Paths behind this icon's label, in deterministic ordinal order.</summary>
        public IReadOnlyList<string> CandidatePaths { get; init; } = Array.Empty<string>();

        /// <summary>Filesystem types for CandidatePaths, in the same deterministic order.</summary>
        public IReadOnlyList<NativeDesktopCandidateType> CandidateTypes { get; init; } = Array.Empty<NativeDesktopCandidateType>();

        /// <summary>Position of this icon among the icons sharing its label, in ListView order.</summary>
        public int LabelOrdinal { get; init; }
        public int ExplorerLabelCount { get; init; }
        public NativeDesktopCandidateType? CandidateType { get; init; }
        public string Identity { get; init; } = string.Empty;
        public string OriginalPath { get; init; } = string.Empty;
        public NativeIconIdentityStatus IdentityStatus { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
    }

    public sealed class NativeDesktopIconSnapshotItem
    {
        public string Identity { get; init; } = string.Empty;
        public string OriginalPath { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public NativeIconIdentityStatus IdentityStatus { get; init; }
        public int LabelOrdinal { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
    }

    public sealed class NativeDesktopLayoutSnapshot
    {
        public int Version { get; init; } = 1;
        public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
        public bool AutoArrangeEnabled { get; init; }
        public bool AlignToGridEnabled { get; init; }
        public int GridSpacingX { get; init; }
        public int GridSpacingY { get; init; }
        public List<NativeDesktopIconSnapshotItem> Icons { get; init; } = new();
    }

    public sealed class NativeDesktopRestoreResult
    {
        public int Restored { get; internal set; }
        public int SkippedMissing { get; internal set; }
        public int SkippedAmbiguous { get; internal set; }
        public int SkippedUnmanaged { get; internal set; }
        public int Failed { get; internal set; }
        public List<string> Errors { get; } = new();
    }

    public sealed class NativeDesktopSnapshotValidationResult
    {
        public int CanResolve { get; internal set; }
        public int Missing { get; internal set; }
        public int Ambiguous { get; internal set; }
        public int SkippedUnmanaged { get; internal set; }
        public bool AutoArrangeEnabled { get; internal set; }
    }

    public sealed class NativeDesktopSafeGrid
    {
        public int ClientWidth { get; init; }
        public int ClientHeight { get; init; }
        public int IconSpacingX { get; init; }
        public int IconSpacingY { get; init; }
        public int SafeColumns { get; init; }
        public int SafeRows { get; init; }
        public int RightSafetyMargin { get; init; }
        public int BottomSafetyMargin { get; init; }
    }

    public sealed class NativeDesktopListViewState
    {
        public IntPtr Handle { get; init; }
        public bool AutoArrangeEnabled { get; init; }
        public bool AlignToGridEnabled { get; init; }
        public int GridSpacingX { get; init; }
        public int GridSpacingY { get; init; }
    }
}
