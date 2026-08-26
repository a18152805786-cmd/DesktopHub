using Microsoft.Win32;
using System;
using System.Windows;

namespace Desktop_Frames
{
    internal static class SearchVisualStyleHelper
    {
        private static bool _paletteInitialized;

        public static void EnsurePalette()
        {
            if (_paletteInitialized || Application.Current == null) return;
            _paletteInitialized = true;

            if (SystemParameters.HighContrast)
            {
                SetAlias("DesktopHubSearchBackgroundBrush", SystemColors.WindowBrush);
                SetAlias("DesktopHubSearchBorderBrush", SystemColors.WindowTextBrush);
                SetAlias("DesktopHubSearchTextBrush", SystemColors.WindowTextBrush);
                SetAlias("DesktopHubSearchMutedTextBrush", SystemColors.GrayTextBrush);
                SetAlias("DesktopHubSearchHoverBrush", SystemColors.ControlBrush);
                SetAlias("DesktopHubSearchSelectedBrush", SystemColors.HighlightBrush);
                SetAlias("DesktopHubSearchSelectedTextBrush", SystemColors.HighlightTextBrush);
                return;
            }

            string palette = IsSystemLightTheme() ? "DesktopHubSearchLight" : "DesktopHubSearchDark";
            foreach (string suffix in new[]
            {
                "BackgroundBrush", "BorderBrush", "TextBrush", "MutedTextBrush",
                "HoverBrush", "SelectedBrush", "SelectedTextBrush"
            })
            {
                object? source = Application.Current.TryFindResource(palette + suffix);
                if (source != null) SetAlias("DesktopHubSearch" + suffix, source);
            }
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
            catch { return false; }
        }

        private static void SetAlias(string key, object value)
        {
            if (Application.Current != null) Application.Current.Resources[key] = value;
        }
    }
}
