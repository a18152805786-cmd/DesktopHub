using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// A minimal Fluent group label that floats over the Desktop wallpaper.
    /// It is a left-aligned text chip followed by a hairline rule - deliberately
    /// not a filled bar - and it is the drag handle for the whole icon group.
    /// The window never animates and never repaints on a timer.
    /// </summary>
    internal sealed class DesktopGroupHeaderWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private static readonly Brush ChipRest = Frozen(Color.FromArgb(0x30, 0x10, 0x10, 0x12));
        private static readonly Brush ChipHover = Frozen(Color.FromArgb(0x5A, 0x10, 0x10, 0x12));
        private static readonly Brush ChipDrag = Frozen(Color.FromArgb(0x7A, 0x10, 0x10, 0x12));
        private static readonly Brush ChipBorderRest = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        private static readonly Brush ChipBorderActive = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

        private readonly Border _chip;
        private readonly NativeDesktopLayoutGroupId _groupId;
        // Where the plan puts the header, in the physical pixels the desktop grid is measured in.
        private readonly int _homeLeft;
        private readonly int _homeTop;
        private readonly int _width;
        private readonly int _height;
        // DPI of the desktop the plan was measured on; it decides the DIP conversion.
        private readonly int _desktopDpi;

        private bool _dragging;
        private Point _dragOriginDevice;
        private int _previewDeltaColumns;
        private int _previewDeltaRows;

        /// <summary>Raised on drop with the grid delta the user dragged the group by.</summary>
        public event Action<NativeDesktopLayoutGroupId, int, int>? GroupDropped;

        /// <summary>Raised when the user picks a command from the header context menu.</summary>
        public event Action<NativeDesktopLayoutGroupId, DesktopGroupHeaderCommand>? CommandInvoked;

        public NativeDesktopLayoutGroupId GroupId => _groupId;

        public bool Locked { get; set; }

        public bool HasManualAnchor { get; set; }

        public bool HasManualSize { get; set; }

        /// <summary>Raised on hover so the group's whole zone can reveal itself.</summary>
        public event Action<NativeDesktopLayoutGroupId, bool>? HoverChanged;

        /// <summary>Set by the manager so the tooltip can state the zone's real capacity.</summary>
        public void SetZoneSummary(int itemCount, int columns, int rows)
        {
            ToolTip = Desktop_Frames.Localization.Strings.Get(
                "GroupHeaderZoneTooltip", Title, itemCount, columns, rows);
        }

        public DesktopGroupHeaderWindow(NativeDesktopLayoutGroupId groupId, string title,
            int left, int top, int width, int height, int desktopDpi)
        {
            _groupId = groupId;
            _homeLeft = left;
            _homeTop = top;
            _width = width;
            _height = height;
            _desktopDpi = desktopDpi;
            Title = title;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Topmost = false;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            PlaceAt(0, 0, 0, 0);

            _chip = BuildChip(title);
            Content = BuildContent(_chip);
            ContextMenu = BuildContextMenu();
            Cursor = Cursors.SizeAll;

            MouseEnter += (_, _) =>
            {
                if (!_dragging) _chip.Background = ChipHover;
                HoverChanged?.Invoke(_groupId, true);
            };
            MouseLeave += (_, _) =>
            {
                if (!_dragging) _chip.Background = ChipRest;
                HoverChanged?.Invoke(_groupId, false);
            };
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
            SourceInitialized += OnSourceInitialized;
        }

        private static Border BuildChip(string title)
        {
            var text = new TextBlock
            {
                Text = title,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                // A static shadow, rendered once, keeps the label readable over
                // both light and dark wallpaper without a filled bar behind it.
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 4,
                    ShadowDepth = 0,
                    Opacity = 0.55,
                    RenderingBias = RenderingBias.Quality
                }
            };

            return new Border
            {
                Background = ChipRest,
                CornerRadius = new CornerRadius(5),
                BorderBrush = ChipBorderRest,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 1, 10, 2),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = text
            };
        }

        private static UIElement BuildContent(Border chip)
        {
            var rule = new Border
            {
                Height = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 2, 0),
                Background = Frozen(new LinearGradientBrush(
                    Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF),
                    Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
                    new Point(0, 0.5), new Point(1, 0.5)))
            };

            var grid = new Grid { Background = Brushes.Transparent };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(chip);
            grid.Children.Add(rule);
            Grid.SetColumn(chip, 0);
            Grid.SetColumn(rule, 1);
            return grid;
        }

        private ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();
            menu.Opened += (_, _) => RefreshContextMenu(menu);
            menu.Items.Add(NewMenuItem(Desktop_Frames.Localization.Strings.GroupHeaderEditLayout,
                DesktopGroupHeaderCommand.ToggleEditMode));
            menu.Items.Add(new Separator());
            menu.Items.Add(NewMenuItem(Desktop_Frames.Localization.Strings.GroupHeaderResetPosition,
                DesktopGroupHeaderCommand.ResetAutoPosition));
            menu.Items.Add(NewMenuItem(Desktop_Frames.Localization.Strings.GroupHeaderResetSize,
                DesktopGroupHeaderCommand.ResetAutoSize));
            menu.Items.Add(NewMenuItem(Desktop_Frames.Localization.Strings.GroupHeaderLockPosition,
                DesktopGroupHeaderCommand.ToggleLock));
            menu.Items.Add(new Separator());
            menu.Items.Add(NewMenuItem(Desktop_Frames.Localization.Strings.GroupHeaderRefreshLayout,
                DesktopGroupHeaderCommand.RefreshLayout));
            if (_groupId == NativeDesktopLayoutGroupId.UNASSIGNED)
            {
                menu.Items.Add(NewMenuItem(Desktop_Frames.Localization.Strings.GroupHeaderTriageUnassigned,
                    DesktopGroupHeaderCommand.TriageUnassigned));
            }
            return menu;
        }

        private MenuItem NewMenuItem(string header, DesktopGroupHeaderCommand command)
        {
            var item = new MenuItem { Header = header, Tag = command };
            item.Click += (_, _) => CommandInvoked?.Invoke(_groupId, command);
            return item;
        }

        private void RefreshContextMenu(ContextMenu menu)
        {
            foreach (object entry in menu.Items)
            {
                if (entry is not MenuItem item || item.Tag is not DesktopGroupHeaderCommand command) continue;
                switch (command)
                {
                    case DesktopGroupHeaderCommand.ResetAutoPosition:
                        item.IsEnabled = HasManualAnchor;
                        break;
                    case DesktopGroupHeaderCommand.ResetAutoSize:
                        item.IsEnabled = HasManualSize;
                        break;
                    case DesktopGroupHeaderCommand.ToggleEditMode:
                        item.Header = DesktopGroupHeaderManager.EditMode
                            ? Desktop_Frames.Localization.Strings.GroupHeaderExitEditLayout
                            : Desktop_Frames.Localization.Strings.GroupHeaderEditLayout;
                        break;
                    case DesktopGroupHeaderCommand.ToggleLock:
                        item.Header = Locked
                            ? Desktop_Frames.Localization.Strings.GroupHeaderUnlockGroup
                            : Desktop_Frames.Localization.Strings.GroupHeaderLockGroup;
                        // A group can be locked whether or not it was ever moved:
                        // locking is about guarding it from now on.
                        item.IsEnabled = true;
                        break;
                }
            }
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Locked)
            {
                Cursor = Cursors.No;
                return;
            }
            _dragging = true;
            _previewDeltaColumns = 0;
            _previewDeltaRows = 0;
            _dragOriginDevice = GetCursorDevicePoint();
            _chip.Background = ChipDrag;
            _chip.BorderBrush = ChipBorderActive;
            CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            (int spacingX, int spacingY) = GetGridSpacing();
            Point now = GetCursorDevicePoint();
            // The preview snaps cell by cell. Explorer is never called while the
            // pointer moves; only this one lightweight window is repositioned.
            int deltaColumns = (int)Math.Round((now.X - _dragOriginDevice.X) / spacingX);
            int deltaRows = (int)Math.Round((now.Y - _dragOriginDevice.Y) / spacingY);
            if (deltaColumns == _previewDeltaColumns && deltaRows == _previewDeltaRows) return;
            _previewDeltaColumns = deltaColumns;
            _previewDeltaRows = deltaRows;
            PlaceAt(deltaColumns, deltaRows, spacingX, spacingY);
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging)
            {
                Cursor = Cursors.SizeAll;
                return;
            }
            _dragging = false;
            ReleaseMouseCapture();
            _chip.Background = IsMouseOver ? ChipHover : ChipRest;
            _chip.BorderBrush = ChipBorderRest;
            e.Handled = true;
            if (_previewDeltaColumns == 0 && _previewDeltaRows == 0)
            {
                SnapHome();
                return;
            }
            GroupDropped?.Invoke(_groupId, _previewDeltaColumns, _previewDeltaRows);
        }

        /// <summary>Returns the header to the position the plan says it belongs at.</summary>
        public void SnapHome()
        {
            PlaceAt(0, 0, 0, 0);
            _previewDeltaColumns = 0;
            _previewDeltaRows = 0;
        }

        /// <summary>
        /// Positions the header from plan pixels, offset by a grid delta while it
        /// is dragged. WPF places windows in DIPs, so every pixel is divided by
        /// the desktop's scale; at 100% that division is by one.
        /// </summary>
        private void PlaceAt(int deltaColumns, int deltaRows, int spacingX, int spacingY)
        {
            double scale = DesktopChromeDpi.ScaleFor(_desktopDpi);
            Left = (_homeLeft + (deltaColumns * spacingX)) / scale;
            Top = (_homeTop + (deltaRows * spacingY)) / scale;
            Width = Math.Max(80, _width / scale);
            Height = Math.Max(DesktopLayoutSpacing.HeaderHeight, _height / scale);
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            if (!_dragging) PlaceAt(0, 0, 0, 0);
        }

        private static (int SpacingX, int SpacingY) GetGridSpacing()
        {
            try
            {
                (int spacingX, int spacingY, _, _) = DesktopGroupDragService.GetGridMetrics();
                return (Math.Max(1, spacingX), Math.Max(1, spacingY));
            }
            catch
            {
                return (75, 100);
            }
        }

        /// <summary>Pointer position in device pixels, the unit the desktop grid spacing is measured in.</summary>
        private static Point GetCursorDevicePoint() => DesktopChromeDpi.CursorPosition();

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int style = GetWindowLong(handle, GWL_EXSTYLE);
            // NOACTIVATE keeps focus with whatever the user was doing; TRANSPARENT
            // is deliberately absent so the header can be grabbed and dragged.
            SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            // The window now knows its real DPI; place it again from the plan pixels.
            PlaceAt(0, 0, 0, 0);
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Brush Frozen(LinearGradientBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }

    internal enum DesktopGroupHeaderCommand
    {
        ResetAutoPosition,
        ResetAutoSize,
        ToggleLock,
        ToggleEditMode,
        RefreshLayout,
        TriageUnassigned
    }
}
