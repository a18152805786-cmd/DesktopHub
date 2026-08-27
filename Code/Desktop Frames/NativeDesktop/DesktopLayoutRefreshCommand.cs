using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// The one command behind "refresh desktop layout".
    ///
    /// It picks up new desktop items, notices icons the user has dragged into a
    /// different group's zone, asks about them, and only then re-runs the
    /// layout. Everything the user decided by hand - manual grouping, manual
    /// zone position, manual zone size, locks - is read back in and survives.
    /// </summary>
    internal static class DesktopLayoutRefreshCommand
    {
        /// <summary>
        /// Runs a refresh. <paramref name="askAboutDroppedIcons"/> is false for
        /// paths where a prompt would be unwelcome, such as an automatic
        /// re-layout triggered by a resize.
        /// </summary>
        public static void Run(string auditDirectory, bool askAboutDroppedIcons = true)
        {
            DesktopLayoutPlan? live = DesktopGroupHeaderManager.CurrentPlan;
            if (askAboutDroppedIcons && live != null)
            {
                IReadOnlyList<DesktopZoneMismatch> mismatches =
                    DesktopGroupZoneService.FindIconsDroppedIntoOtherZones(live);
                if (mismatches.Count > 0)
                    ConfirmAndApply(mismatches);
            }

            NativeDesktopLayoutApplicationService.RefreshDualRailLayoutForHeaders(auditDirectory);
        }

        /// <summary>
        /// Asks before writing anything. An icon that happens to be sitting in a
        /// zone is not proof the user meant to regroup it, so a silent
        /// reclassification would be the wrong default - and a "cancel" leaves
        /// the classification exactly as it was.
        /// </summary>
        private static void ConfirmAndApply(IReadOnlyList<DesktopZoneMismatch> mismatches)
        {
            IReadOnlyList<DesktopZoneMismatch> accepted = ZoneDropConfirmationWindow.Ask(mismatches);
            if (accepted.Count == 0) return;

            var store = new NativeDesktopLayoutMetadataStore();
            var items = new List<DesktopLayoutItem>();
            foreach (DesktopZoneMismatch mismatch in accepted)
            {
                var item = new DesktopLayoutItem { OriginalPath = mismatch.OriginalPath };
                item.SetManualGroup(mismatch.ZoneGroup, DisplayName(mismatch.ZoneGroup));
                items.Add(item);
            }
            store.PersistLayoutGroups(items);
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                $"Manual grouping from zone drops: {string.Join(", ", accepted.Select(m => $"{m.DisplayName}->{m.ZoneGroup}"))}");
        }

        internal static string DisplayName(NativeDesktopLayoutGroupId group) => group switch
        {
            NativeDesktopLayoutGroupId.RESEARCH_STUDY => "科研 · 学习",
            NativeDesktopLayoutGroupId.AI_DEV => "AI · 开发",
            NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS => "办公 · 文档",
            NativeDesktopLayoutGroupId.DAILY_TOOLS => "日常工具",
            NativeDesktopLayoutGroupId.GAMES => "游戏",
            NativeDesktopLayoutGroupId.SYSTEM_TEMP => "系统 · 临时",
            _ => "待整理"
        };
    }

    /// <summary>
    /// "You dropped these here - should they belong here?" One row per icon, all
    /// ticked by default, and nothing is written unless the user confirms.
    /// </summary>
    internal sealed class ZoneDropConfirmationWindow : Window
    {
        private readonly List<(DesktopZoneMismatch Mismatch, System.Windows.Controls.CheckBox Box)> _rows = new();
        private bool _confirmed;

        public static IReadOnlyList<DesktopZoneMismatch> Ask(IReadOnlyList<DesktopZoneMismatch> mismatches)
        {
            var window = new ZoneDropConfirmationWindow(mismatches);
            window.ShowDialog();
            return window._confirmed
                ? window._rows.Where(row => row.Box.IsChecked == true).Select(row => row.Mismatch).ToList()
                : Array.Empty<DesktopZoneMismatch>();
        }

        private ZoneDropConfirmationWindow(IReadOnlyList<DesktopZoneMismatch> mismatches)
        {
            Title = Strings.ZoneDropTitle;
            Width = 520;
            Height = Math.Min(560, 210 + (mismatches.Count * 30));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xF7, 0xF8, 0xFA));

            var root = new System.Windows.Controls.Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
            { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var hint = new System.Windows.Controls.TextBlock
            {
                Text = mismatches.Count == 1
                    ? Strings.Get("ZoneDropSingleHint", mismatches[0].DisplayName,
                        DesktopLayoutRefreshCommand.DisplayName(mismatches[0].ZoneGroup))
                    : Strings.Get("ZoneDropMultipleHint", mismatches.Count),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(hint);
            System.Windows.Controls.Grid.SetRow(hint, 0);

            var list = new System.Windows.Controls.StackPanel();
            foreach (DesktopZoneMismatch mismatch in mismatches)
            {
                var box = new System.Windows.Controls.CheckBox
                {
                    IsChecked = true,
                    Margin = new Thickness(0, 4, 0, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Content = new System.Windows.Controls.TextBlock
                    {
                        Text = $"{mismatch.DisplayName}  →  {DesktopLayoutRefreshCommand.DisplayName(mismatch.ZoneGroup)}",
                        FontSize = 13,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    ToolTip = mismatch.OriginalPath
                };
                _rows.Add((mismatch, box));
                list.Children.Add(box);
            }
            var scroller = new System.Windows.Controls.ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled
            };
            root.Children.Add(scroller);
            System.Windows.Controls.Grid.SetRow(scroller, 1);

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var cancel = new System.Windows.Controls.Button
            {
                Content = Strings.BtnCancel,
                MinWidth = 96,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                IsCancel = true
            };
            cancel.Click += (_, _) => Close();
            var confirm = new System.Windows.Controls.Button
            {
                Content = Strings.BtnConfirm,
                MinWidth = 96,
                Height = 32,
                IsDefault = true
            };
            confirm.Click += (_, _) => { _confirmed = true; Close(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(confirm);
            root.Children.Add(buttons);
            System.Windows.Controls.Grid.SetRow(buttons, 2);

            Content = root;
        }
    }
}
