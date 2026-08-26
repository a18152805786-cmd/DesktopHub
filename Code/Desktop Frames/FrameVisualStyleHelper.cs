using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Desktop_Frames
{
    /// <summary>
    /// Applies the global Desktop Hub Frame V1 visual language to the existing
    /// dynamically-built Frame tree. It deliberately owns presentation only:
    /// no Frame data, drag/drop, activation, profile, or persistence behavior.
    /// </summary>
    internal static class FrameVisualStyleHelper
    {
        private const double FallbackHeaderHeight = 32.0;
        private const double FallbackCustomColorOpacity = 0.58;
        private const string StartupDefaultTitleEnglish = "New Frame - Drop your shortcuts here";
        private const string StartupDefaultTitleChinese = "新建分组 - 将快捷方式拖到这里";

        private static bool _paletteInitialized;

        public static double HeaderHeight => GetDoubleResource("DesktopHubFrameHeaderHeight", FallbackHeaderHeight);
        public static double CollapsedHeight => HeaderHeight;

        public static void ApplySurface(Border surface, bool sharpCorners, Brush? customBorderBrush = null)
        {
            EnsurePalette();
            ApplyStyle(surface, "DesktopHubFrameSurfaceStyle");

            surface.ClearValue(Border.BorderThicknessProperty);
            surface.ClearValue(Border.BorderBrushProperty);
            surface.ClearValue(Border.CornerRadiusProperty);

            if (sharpCorners)
            {
                surface.CornerRadius = new CornerRadius(0);
            }

            if (customBorderBrush != null)
            {
                surface.BorderBrush = customBorderBrush;
            }
        }

        public static void ApplySurfaceColor(Border surface, string? customColorName)
        {
            EnsurePalette();

            bool isDefault = string.IsNullOrWhiteSpace(customColorName)
                || string.Equals(customColorName, "Default", StringComparison.OrdinalIgnoreCase);

            if (isDefault)
            {
                surface.ClearValue(Border.BackgroundProperty);
                return;
            }

            Color customColor = Utility.GetColorFromName(customColorName!);
            if (customColor == Colors.Transparent)
            {
                surface.ClearValue(Border.BackgroundProperty);
                return;
            }

            surface.Background = new SolidColorBrush(customColor)
            {
                Opacity = GetDoubleResource("DesktopHubFrameCustomColorOpacity", FallbackCustomColorOpacity)
            };
        }

        public static void ApplyWindowPresentation(NonActivatingWindow window)
        {
            window.UseLayoutRounding = true;
            window.SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(window, TextRenderingMode.Auto);
        }

        public static void ApplyHeader(Grid header)
        {
            EnsurePalette();
            ApplyStyle(header, "DesktopHubFrameHeaderStyle");
        }

        public static void ApplyTitle(Label title, TextBox titleEditor, string? persistedTitle, bool boldTitle)
        {
            EnsurePalette();
            ApplyStyle(title, "DesktopHubFrameTitleStyle");
            ApplyStyle(titleEditor, "DesktopHubFrameTitleEditorStyle");

            string originalTitle = persistedTitle ?? string.Empty;
            title.Content = IsBuiltInDefaultTitle(originalTitle) ? Localization.Strings.MenuNewFrame : originalTitle;
            title.ToolTip = IsBuiltInDefaultTitle(originalTitle) ? Localization.Strings.FrameNewTitle : originalTitle;

            if (boldTitle)
            {
                title.FontWeight = FontWeights.Bold;
            }
            else
            {
                title.ClearValue(Control.FontWeightProperty);
            }
        }

        public static void ApplyHeaderControl(TextBlock control)
        {
            EnsurePalette();
            control.BeginAnimation(UIElement.OpacityProperty, null);
            control.ClearValue(UIElement.OpacityProperty);
            control.ClearValue(UIElement.IsHitTestVisibleProperty);
            ApplyStyle(control, "DesktopHubFrameHeaderControlStyle");
        }

        public static void ApplyLockState(TextBlock lockControl, bool isLocked)
        {
            ApplyHeaderControl(lockControl);
            lockControl.Foreground = GetBrushResource(
                isLocked ? "DesktopHubFrameLockedBrush" : "DesktopHubFrameControlBrush",
                isLocked ? Brushes.CornflowerBlue : Brushes.White);
        }

        public static bool IsBuiltInDefaultTitle(string? title)
        {
            if (string.IsNullOrEmpty(title)) return false;

            return string.Equals(title, StartupDefaultTitleEnglish, StringComparison.Ordinal)
                || string.Equals(title, StartupDefaultTitleChinese, StringComparison.Ordinal)
                || string.Equals(title, Localization.Strings.FrameNewTitle, StringComparison.Ordinal);
        }

        private static void EnsurePalette()
        {
            if (_paletteInitialized || Application.Current == null) return;

            _paletteInitialized = true;
            if (SystemParameters.HighContrast)
            {
                SetAlias("DesktopHubFrameBackgroundBrush", SystemColors.WindowBrush);
                SetAlias("DesktopHubFrameHoverBackgroundBrush", SystemColors.ControlBrush);
                SetAlias("DesktopHubFrameBorderBrush", SystemColors.WindowTextBrush);
                SetAlias("DesktopHubFrameHoverBorderBrush", SystemColors.HighlightBrush);
                SetAlias("DesktopHubFrameHeaderBrush", SystemColors.WindowBrush);
                SetAlias("DesktopHubFrameHoverHeaderBrush", SystemColors.ControlBrush);
                SetAlias("DesktopHubFrameTitleBrush", SystemColors.WindowTextBrush);
                SetAlias("DesktopHubFrameControlBrush", SystemColors.WindowTextBrush);
                SetAlias("DesktopHubFrameLockedBrush", SystemColors.HighlightBrush);
                return;
            }

            string palette = IsSystemLightTheme() ? "DesktopHubFrameLight" : "DesktopHubFrameDark";
            SetAliasFromResource("DesktopHubFrameBackgroundBrush", palette + "BackgroundBrush");
            SetAliasFromResource("DesktopHubFrameHoverBackgroundBrush", palette + "HoverBackgroundBrush");
            SetAliasFromResource("DesktopHubFrameBorderBrush", palette + "BorderBrush");
            SetAliasFromResource("DesktopHubFrameHoverBorderBrush", palette + "HoverBorderBrush");
            SetAliasFromResource("DesktopHubFrameHeaderBrush", palette + "HeaderBrush");
            SetAliasFromResource("DesktopHubFrameHoverHeaderBrush", palette + "HoverHeaderBrush");
            SetAliasFromResource("DesktopHubFrameTitleBrush", palette + "TitleBrush");
            SetAliasFromResource("DesktopHubFrameControlBrush", palette + "ControlBrush");
            SetAliasFromResource("DesktopHubFrameLockedBrush", palette + "LockedBrush");
        }

        private static bool IsSystemLightTheme()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                object? value = key?.GetValue("AppsUseLightTheme");
                return value != null && Convert.ToInt32(value) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static void SetAliasFromResource(string alias, string sourceKey)
        {
            object? source = Application.Current?.TryFindResource(sourceKey);
            if (source != null) SetAlias(alias, source);
        }

        private static void SetAlias(string key, object value)
        {
            if (Application.Current != null)
            {
                Application.Current.Resources[key] = value;
            }
        }

        private static void ApplyStyle(FrameworkElement element, string key)
        {
            if (Application.Current?.TryFindResource(key) is Style style)
            {
                element.Style = style;
            }
        }

        private static Brush GetBrushResource(string key, Brush fallback)
        {
            return Application.Current?.TryFindResource(key) as Brush ?? fallback;
        }

        private static double GetDoubleResource(string key, double fallback)
        {
            object? value = Application.Current?.TryFindResource(key);
            return value is double number ? number : fallback;
        }
    }
}
