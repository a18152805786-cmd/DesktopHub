using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// Owns the small lifecycle policy for the built-in Startup Tips frame.
    /// It deliberately does not infer identity from a single user-visible
    /// string: new frames carry a stable marker, while legacy frames require
    /// an exact normalized title/content pair.
    /// </summary>
    internal static class StartupTipsManager
    {
        public const string StartupTipsSystemId = "startup-tips";
        public const string SystemFrameIdProperty = "SystemFrameId";

        public enum LegacyMatchConfidence
        {
            NotMatch,
            LowConfidence,
            HighConfidence
        }

        public sealed class ReconciliationResult
        {
            public int StableIdentityMatches { get; set; }
            public int HighConfidenceLegacyMatches { get; set; }
            public int LowConfidenceLegacyMatches { get; set; }
            public bool StableIdentityAdded { get; set; }
            public bool LegacyFrameRemoved { get; set; }
            public bool AmbiguousLegacyPreserved { get; set; }
            public bool DuplicateStableIdentityPreserved { get; set; }
        }

        // These are historical built-in signatures, not user-facing UI text.
        // Keep them exact after line-ending normalization so edited Notes do
        // not become eligible for automatic migration.
        private static readonly HashSet<string> KnownTitles = new HashSet<string>(StringComparer.Ordinal)
        {
            "Desktop Frames + Startup Tips",
            "Desktop Frames + 使用提示"
        };

        private static readonly HashSet<string> KnownContents = new HashSet<string>(StringComparer.Ordinal)
        {
            Normalize(@"WELCOME TO DESKTOP FRAMES +
---------------------------
• Roll Up/Down: Double-click the frame title bar.
• Rename: Ctrl + Click the title bar (Enter to save).
• Search (SpotSearch): Press Ctrl + ` (Tilde) to find any icon instantly.
• Options: Click the '♥' menu icon (top-left).
• Reorder Icons on a frame: Ctrl + Drag icon to new position.
• Context Menu: Right-click icons or Frames for more options.

TIP: Ctrl + Click or Ctrl + Right-click, gives even more options.

Try customizing this frame! Right-click the title bar -> Customize..."),
            Normalize(@"欢迎使用 Desktop Frames +
---------------------------
• 展开/收起：双击分组标题栏。
• 重命名：按住 Ctrl 点击标题栏（按 Enter 保存）。
• 搜索（SpotSearch）：按 Ctrl + ` 快速查找图标。
• 设置：点击左上角的“♥”菜单图标。
• 重排图标：按住 Ctrl 拖动图标到新位置。
• 右键菜单：右键点击图标或分组查看更多操作。

提示：按住 Ctrl 点击或 Ctrl + 右键点击，可查看更多选项。

试试个性化这个分组！右键点击标题栏 → 个性化…")
        };

        public static bool ShouldCreateForEmptyProfile()
        {
            return !ProfileManager.StartupTipsDismissed;
        }

        public static bool IsStartupTipsFrame(dynamic frame)
        {
            return string.Equals(GetString(frame, "ItemsType"), "Note", StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetString(frame, SystemFrameIdProperty), StartupTipsSystemId, StringComparison.Ordinal);
        }

        public static LegacyMatchConfidence ClassifyLegacyFrame(dynamic frame)
        {
            if (!IsNoteFrame(frame)) return LegacyMatchConfidence.NotMatch;

            string title = Normalize(GetString(frame, "Title"));
            string content = Normalize(GetString(frame, "NoteContent"));
            bool knownTitle = KnownTitles.Contains(title);
            bool knownContent = KnownContents.Contains(content);

            if (knownTitle && knownContent) return LegacyMatchConfidence.HighConfidence;
            if (knownTitle || knownContent) return LegacyMatchConfidence.LowConfidence;
            return LegacyMatchConfidence.NotMatch;
        }

        public static bool IsHighConfidenceLegacy(dynamic frame)
        {
            return ClassifyLegacyFrame(frame) == LegacyMatchConfidence.HighConfidence;
        }

        /// <summary>
        /// Reconciles loaded frame data. It adds the stable marker to one
        /// exact legacy Tips frame, removes only an already-dismissed,
        /// identified Tips frame, and preserves ambiguity.
        /// </summary>
        public static ReconciliationResult ReconcileLoadedFrames()
        {
            var result = new ReconciliationResult();
            var frames = FrameDataManager.FrameData;
            if (frames == null) return result;

            var stableMatches = frames.Where(IsStartupTipsFrame).ToList();
            var legacyMatches = frames
                .Where(frame => !IsStartupTipsFrame(frame) && IsHighConfidenceLegacy(frame))
                .ToList();

            result.StableIdentityMatches = stableMatches.Count;
            result.HighConfidenceLegacyMatches = legacyMatches.Count;
            result.LowConfidenceLegacyMatches = frames.Count(frame =>
                !IsStartupTipsFrame(frame) && ClassifyLegacyFrame(frame) == LegacyMatchConfidence.LowConfidence);
            result.AmbiguousLegacyPreserved = result.LowConfidenceLegacyMatches > 0;

            // Multiple built-in candidates are not safe to resolve automatically.
            // Count stable and legacy signatures together so a mixed old/new
            // duplicate is preserved instead of producing two Tips frames.
            int identifiedCandidateCount = stableMatches.Count + legacyMatches.Count;
            if (identifiedCandidateCount > 1)
            {
                result.DuplicateStableIdentityPreserved = stableMatches.Count > 1;
                result.AmbiguousLegacyPreserved = result.AmbiguousLegacyPreserved || legacyMatches.Count > 0;
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                    $"Preserved {identifiedCandidateCount} Startup Tips candidates because identity is ambiguous.");
                return result;
            }

            object? identifiedFrame = stableMatches.Cast<object>().SingleOrDefault()
                ?? legacyMatches.Cast<object>().SingleOrDefault();
            if (identifiedFrame == null) return result;

            if (ProfileManager.StartupTipsDismissed)
            {
                frames.Remove(identifiedFrame);
                FrameDataManager.SaveFrameData();
                result.LegacyFrameRemoved = true;
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    "Removed the identified Startup Tips frame because global dismissal is already enabled.");
                return result;
            }

            if (!IsStartupTipsFrame(identifiedFrame))
            {
                SetString(identifiedFrame, SystemFrameIdProperty, StartupTipsSystemId);
                int index = frames.IndexOf(identifiedFrame);
                if (index >= 0) frames[index] = identifiedFrame;
                FrameDataManager.SaveFrameData();
                result.StableIdentityAdded = true;
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    "Attached stable Startup Tips identity to one exact legacy frame.");
            }

            return result;
        }

        /// <summary>
        /// Called only after the user confirms deletion of a frame.
        /// </summary>
        public static void HandleUserFrameDeletion(dynamic frame)
        {
            if (IsStartupTipsFrame(frame) || IsHighConfidenceLegacy(frame))
            {
                ProfileManager.SetStartupTipsDismissed(true);
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    "Startup Tips dismissed by an explicit user frame deletion.");
            }
        }

        private static bool IsNoteFrame(dynamic frame)
        {
            return string.Equals(GetString(frame, "ItemsType"), "Note", StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetString(object? frame, string propertyName)
        {
            if (frame == null) return null;

            if (frame is JObject jsonObject)
            {
                return jsonObject[propertyName]?.ToString();
            }

            if (frame is IDictionary<string, object> dictionary && dictionary.TryGetValue(propertyName, out object? value))
            {
                return value?.ToString();
            }

            return null;
        }

        private static void SetString(dynamic frame, string propertyName, string value)
        {
            if (frame is JObject jsonObject)
            {
                jsonObject[propertyName] = value;
                return;
            }

            if (frame is IDictionary<string, object> dictionary)
            {
                dictionary[propertyName] = value;
                return;
            }

            try { frame[propertyName] = value; }
            catch { }
        }

        private static string Normalize(string value)
        {
            var lines = (value ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n')
                .Select(line => line.TrimEnd());

            return string.Join("\n", lines).Trim();
        }
    }
}
