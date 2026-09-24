using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Desktop_Frames.NativeDesktop
{
    internal enum ZoneResizeEdge
    {
        None,
        Right,
        Bottom,
        BottomRight
    }

    /// <summary>
    /// The visible boundary of one group.
    ///
    /// This is the window that answers the question the user could not answer
    /// before: how far does "游戏" actually reach. It is deliberately almost
    /// invisible at rest so it never competes with the wallpaper, and it becomes
    /// obvious exactly when the user is thinking about layout - hovering the
    /// header, or in edit mode.
    ///
    /// In normal mode the window carries WS_EX_TRANSPARENT, so it is incapable
    /// of intercepting a click meant for a desktop icon. Edit mode removes that
    /// style to make the resize handles grabbable; while editing, the user is
    /// arranging rather than launching, so that is the right trade.
    /// </summary>
    internal sealed class DesktopGroupZoneWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        /// <summary>How far the outline sits outside the icon cells, in DIP.</summary>
        private const int Inset = 3;

        /// <summary>Grab thickness of the resize strips, in DIP.</summary>
        private const double HandleThickness = 10;

        private static readonly Brush EdgeIdle = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        private static readonly Brush EdgeHover = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        private static readonly Brush EdgeEdit = Frozen(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
        private static readonly Brush EdgeActive = Frozen(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        private static readonly Brush EdgeRejected = Frozen(Color.FromArgb(0xCC, 0xFF, 0x6B, 0x6B));
        private static readonly Brush HandleFill = Frozen(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
        private static readonly Brush BadgeFill = Frozen(Color.FromArgb(0x66, 0x10, 0x10, 0x12));

        private readonly NativeDesktopLayoutGroupId _groupId;
        private readonly Border _outline;
        private readonly Border _badge;
        private readonly TextBlock _badgeText;
        private readonly Border _rightHandle;
        private readonly Border _bottomHandle;
        private readonly Border _cornerHandle;

        private int _spacingX = 75;
        private int _spacingY = 100;
        private int _originLeft;
        private int _originTop;
        private int _columns;
        private int _rows;
        private int _minimumSlots;
        private int _desktopDpi;

        private bool _editMode;
        private bool _hovered;
        private bool _locked;
        private ZoneResizeEdge _resizing = ZoneResizeEdge.None;
        private Point _resizeOriginDevice;
        private int _previewColumns;
        private int _previewRows;

        /// <summary>Raised on drop with the grid size the user resized the zone to.</summary>
        public event Action<NativeDesktopLayoutGroupId, int, int>? ZoneResized;

        public NativeDesktopLayoutGroupId GroupId => _groupId;

        public DesktopGroupZoneWindow(NativeDesktopLayoutGroupId groupId)
        {
            _groupId = groupId;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Topmost = false;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            IsHitTestVisible = false;

            _outline = new Border
            {
                Background = null,
                BorderBrush = EdgeIdle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };

            _badgeText = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            };
            _badge = new Border
            {
                Background = BadgeFill,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 1, 7, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 6, 0),
                Visibility = Visibility.Collapsed,
                Child = _badgeText
            };

            _rightHandle = BuildHandle(HorizontalAlignment.Right, VerticalAlignment.Stretch,
                HandleThickness, double.NaN, Cursors.SizeWE, ZoneResizeEdge.Right);
            _bottomHandle = BuildHandle(HorizontalAlignment.Stretch, VerticalAlignment.Bottom,
                double.NaN, HandleThickness, Cursors.SizeNS, ZoneResizeEdge.Bottom);
            _cornerHandle = BuildHandle(HorizontalAlignment.Right, VerticalAlignment.Bottom,
                HandleThickness + 4, HandleThickness + 4, Cursors.SizeNWSE, ZoneResizeEdge.BottomRight);

            var root = new Grid { Background = null };
            root.Children.Add(_outline);
            root.Children.Add(_badge);
            root.Children.Add(_rightHandle);
            root.Children.Add(_bottomHandle);
            root.Children.Add(_cornerHandle);
            Content = root;

            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
            SourceInitialized += (_, _) =>
            {
                ApplyExtendedStyle();
                // The window now knows its real DPI; place it again from the plan pixels.
                ApplyBounds(_previewColumns, _previewRows);
            };
        }

        private Border BuildHandle(HorizontalAlignment horizontal, VerticalAlignment vertical,
            double width, double height, Cursor cursor, ZoneResizeEdge edge)
        {
            var handle = new Border
            {
                Background = Brushes.Transparent,
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
                Cursor = cursor,
                Visibility = Visibility.Collapsed
            };
            if (!double.IsNaN(width)) handle.Width = width;
            if (!double.IsNaN(height)) handle.Height = height;

            // A visible pip only on the corner: the edges are found by the
            // cursor changing, which is quieter than three permanent tabs.
            if (edge == ZoneResizeEdge.BottomRight)
            {
                handle.Child = new Border
                {
                    Background = HandleFill,
                    CornerRadius = new CornerRadius(2),
                    Width = 8,
                    Height = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            handle.MouseLeftButtonDown += (_, e) => BeginResize(edge, e);
            return handle;
        }

        /// <summary>Places the zone from grid coordinates. All geometry comes from the live grid.</summary>
        public void SetGeometry(int originLeft, int originTop, int columns, int rows,
            int spacingX, int spacingY, int minimumSlots, int desktopDpi)
        {
            _desktopDpi = desktopDpi;
            _originLeft = originLeft;
            _originTop = originTop;
            _columns = Math.Max(1, columns);
            _rows = Math.Max(1, rows);
            _spacingX = Math.Max(1, spacingX);
            _spacingY = Math.Max(1, spacingY);
            _minimumSlots = Math.Max(0, minimumSlots);
            _previewColumns = _columns;
            _previewRows = _rows;
            ApplyBounds(_columns, _rows);
            UpdateBadge(_columns, _rows);
        }

        private void ApplyBounds(int columns, int rows)
        {
            // Origin and spacing are the plan's physical pixels; the window is
            // placed in DIPs, and the inset is a DIP measure of its own.
            double scale = DesktopChromeDpi.ScaleFor(_desktopDpi);
            Left = (_originLeft / scale) - Inset;
            Top = (_originTop / scale) - Inset;
            Width = (columns * _spacingX / scale) + (Inset * 2);
            Height = (rows * _spacingY / scale) + (Inset * 2);
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            if (_resizing == ZoneResizeEdge.None) ApplyBounds(_columns, _rows);
        }

        private void UpdateBadge(int columns, int rows)
        {
            _badgeText.Text = $"{columns} × {rows}";
        }

        public bool Locked
        {
            get => _locked;
            set
            {
                _locked = value;
                UpdateVisualState();
            }
        }

        /// <summary>Edit mode makes the zone real: visible, measurable and grabbable.</summary>
        public bool EditMode
        {
            get => _editMode;
            set
            {
                if (_editMode == value) return;
                _editMode = value;
                IsHitTestVisible = value;
                ApplyExtendedStyle();
                UpdateVisualState();
            }
        }

        /// <summary>Called by the header so hovering the title reveals the whole zone.</summary>
        public void SetHeaderHovered(bool hovered)
        {
            _hovered = hovered;
            UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            if (_resizing != ZoneResizeEdge.None) return;
            _outline.BorderBrush = _editMode ? EdgeEdit : _hovered ? EdgeHover : EdgeIdle;
            _outline.BorderThickness = new Thickness(_editMode || _hovered ? 1.5 : 1);
            _badge.Visibility = _editMode ? Visibility.Visible : Visibility.Collapsed;

            Visibility handles = _editMode && !_locked ? Visibility.Visible : Visibility.Collapsed;
            _rightHandle.Visibility = handles;
            _bottomHandle.Visibility = handles;
            _cornerHandle.Visibility = handles;
        }

        private void BeginResize(ZoneResizeEdge edge, MouseButtonEventArgs e)
        {
            if (!_editMode || _locked) return;
            _resizing = edge;
            _previewColumns = _columns;
            _previewRows = _rows;
            _resizeOriginDevice = GetCursorDevicePoint();
            _outline.BorderBrush = EdgeActive;
            _outline.BorderThickness = new Thickness(2);
            CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_resizing == ZoneResizeEdge.None) return;
            Point now = GetCursorDevicePoint();

            // The preview snaps to whole grid cells, so the user can never end up
            // with a zone that is three and a half columns wide.
            int columns = _previewColumns;
            int rows = _previewRows;
            if (_resizing is ZoneResizeEdge.Right or ZoneResizeEdge.BottomRight)
            {
                int delta = (int)Math.Round((now.X - _resizeOriginDevice.X) / _spacingX);
                columns = Math.Max(DesktopGroupZoneService.MinimumColumns, _columns + delta);
            }
            if (_resizing is ZoneResizeEdge.Bottom or ZoneResizeEdge.BottomRight)
            {
                int delta = (int)Math.Round((now.Y - _resizeOriginDevice.Y) / _spacingY);
                rows = Math.Max(DesktopGroupZoneService.MinimumRows, _rows + delta);
            }
            if (columns == _previewColumns && rows == _previewRows) return;

            _previewColumns = columns;
            _previewRows = rows;
            ApplyBounds(columns, rows);
            UpdateBadge(columns, rows);
            // Red the moment the zone stops being able to hold its own icons, so
            // the rejection is visible before the button is released.
            bool fits = columns * rows >= _minimumSlots;
            _outline.BorderBrush = fits ? EdgeActive : EdgeRejected;
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_resizing == ZoneResizeEdge.None) return;
            _resizing = ZoneResizeEdge.None;
            ReleaseMouseCapture();
            e.Handled = true;

            int columns = _previewColumns;
            int rows = _previewRows;
            if (columns == _columns && rows == _rows)
            {
                ApplyBounds(_columns, _rows);
                UpdateBadge(_columns, _rows);
                UpdateVisualState();
                return;
            }
            ZoneResized?.Invoke(_groupId, columns, rows);
        }

        /// <summary>Puts the outline back at the size the plan says it is.</summary>
        public void SnapBack()
        {
            _previewColumns = _columns;
            _previewRows = _rows;
            _resizing = ZoneResizeEdge.None;
            ApplyBounds(_columns, _rows);
            UpdateBadge(_columns, _rows);
            UpdateVisualState();
        }

        private void ApplyExtendedStyle()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            int style = GetWindowLong(handle, GWL_EXSTYLE);
            style |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            // Outside edit mode the outline must be physically incapable of
            // swallowing a click meant for a desktop icon.
            if (_editMode) style &= ~WS_EX_TRANSPARENT;
            else style |= WS_EX_TRANSPARENT;
            SetWindowLong(handle, GWL_EXSTYLE, style);
        }

        /// <summary>Pointer position in device pixels, the unit the zone's grid spacing is measured in.</summary>
        private static Point GetCursorDevicePoint() => DesktopChromeDpi.CursorPosition();

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
