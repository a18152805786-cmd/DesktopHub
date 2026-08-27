using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// The lightweight manual grouping entry point. It lists everything that
    /// landed in 待整理 and lets the user say where each item really belongs.
    /// The choice is written as ManualLayoutGroupId, which outranks the
    /// automatic classifier from then on.
    /// </summary>
    internal sealed class ManualClassificationWindow : Window
    {
        private static readonly NativeDesktopLayoutGroupId[] Assignable =
        {
            NativeDesktopLayoutGroupId.RESEARCH_STUDY,
            NativeDesktopLayoutGroupId.AI_DEV,
            NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS,
            NativeDesktopLayoutGroupId.DAILY_TOOLS,
            NativeDesktopLayoutGroupId.GAMES,
            NativeDesktopLayoutGroupId.SYSTEM_TEMP,
            NativeDesktopLayoutGroupId.UNASSIGNED
        };

        private readonly List<Row> _rows = new();

        private sealed record Row(DesktopLayoutItem Item, ComboBox Selector, NativeDesktopLayoutGroupId Original);

        /// <summary>Opens the triage list for everything currently sitting in 待整理.</summary>
        public static void ShowDialogFor(DesktopLayoutPlan? plan, string auditDirectory)
        {
            if (plan == null) return;
            List<DesktopLayoutItem> pending = plan.Items
                .Where(item => item.IsStableIdentity && !item.IsShellNamespace &&
                    ParseGroup(item.LayoutGroupId) == NativeDesktopLayoutGroupId.UNASSIGNED)
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (pending.Count == 0)
            {
                MessageBox.Show(Strings.ManualGroupingNothingToSort, Strings.ManualGroupingTitle,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new ManualClassificationWindow(pending, auditDirectory);
            window.ShowDialog();
        }

        private ManualClassificationWindow(IReadOnlyList<DesktopLayoutItem> pending, string auditDirectory)
        {
            Title = Strings.ManualGroupingTitle;
            Width = 620;
            Height = Math.Min(720, 220 + (pending.Count * 34));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA));

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var hint = new TextBlock
            {
                Text = Strings.ManualGroupingHint,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x3C, 0x40, 0x43)),
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(hint);
            Grid.SetRow(hint, 0);

            var headings = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            headings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            headings.Children.Add(Heading(Strings.ManualGroupingColumnItem, 0));
            headings.Children.Add(Heading(Strings.ManualGroupingColumnGroup, 1));
            root.Children.Add(headings);
            Grid.SetRow(headings, 1);

            var list = new StackPanel();
            foreach (DesktopLayoutItem item in pending)
                list.Children.Add(BuildRow(item));
            var scroller = new ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            root.Children.Add(scroller);
            Grid.SetRow(scroller, 2);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var cancel = new Button
            {
                Content = Strings.BtnCancel,
                MinWidth = 96,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                IsCancel = true
            };
            cancel.Click += (_, _) => Close();
            var apply = new Button
            {
                Content = Strings.BtnApply,
                MinWidth = 96,
                Height = 32,
                IsDefault = true
            };
            apply.Click += (_, _) => ApplyChanges(auditDirectory);
            buttons.Children.Add(cancel);
            buttons.Children.Add(apply);
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 3);

            Content = root;
        }

        private static TextBlock Heading(string text, int column)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x63, 0x68))
            };
            Grid.SetColumn(block, column);
            return block;
        }

        private UIElement BuildRow(DesktopLayoutItem item)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });

            var name = new TextBlock
            {
                Text = item.DisplayName,
                ToolTip = item.OriginalPath,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 12, 0)
            };
            var selector = new ComboBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
            foreach (NativeDesktopLayoutGroupId group in Assignable)
                selector.Items.Add(new ComboBoxItem { Content = DisplayName(group), Tag = group });
            selector.SelectedIndex = Array.IndexOf(Assignable, NativeDesktopLayoutGroupId.UNASSIGNED);

            grid.Children.Add(name);
            grid.Children.Add(selector);
            Grid.SetColumn(name, 0);
            Grid.SetColumn(selector, 1);
            _rows.Add(new Row(item, selector, NativeDesktopLayoutGroupId.UNASSIGNED));
            return grid;
        }

        private void ApplyChanges(string auditDirectory)
        {
            var changed = new List<DesktopLayoutItem>();
            foreach (Row row in _rows)
            {
                if (row.Selector.SelectedItem is not ComboBoxItem selected ||
                    selected.Tag is not NativeDesktopLayoutGroupId group ||
                    group == row.Original)
                    continue;
                row.Item.SetManualGroup(group, DisplayName(group));
                changed.Add(row.Item);
            }

            if (changed.Count == 0)
            {
                Close();
                return;
            }

            new NativeDesktopLayoutMetadataStore().PersistLayoutGroups(changed);
            Close();
            try
            {
                NativeDesktopLayoutApplicationService.RefreshDualRailLayoutForHeaders(auditDirectory);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Manual grouping saved but the layout was not refreshed: {ex.Message}");
                MessageBox.Show(Strings.Get("ManualGroupingApplied", changed.Count), Strings.ManualGroupingTitle,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static string DisplayName(NativeDesktopLayoutGroupId group) => group switch
        {
            NativeDesktopLayoutGroupId.RESEARCH_STUDY => "科研 · 学习",
            NativeDesktopLayoutGroupId.AI_DEV => "AI · 开发",
            NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS => "办公 · 文档",
            NativeDesktopLayoutGroupId.DAILY_TOOLS => "日常工具",
            NativeDesktopLayoutGroupId.GAMES => "游戏",
            NativeDesktopLayoutGroupId.SYSTEM_TEMP => "系统 · 临时",
            _ => Strings.ManualGroupingKeepUnmanaged
        };

        private static NativeDesktopLayoutGroupId ParseGroup(string value) =>
            Enum.TryParse(value, true, out NativeDesktopLayoutGroupId group)
                ? group
                : NativeDesktopLayoutGroupId.UNASSIGNED;
    }
}
