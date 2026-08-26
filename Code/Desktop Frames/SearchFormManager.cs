using Desktop_Frames.Localization;
using Desktop_Frames.Search;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Desktop_Frames
{
    /// <summary>
    /// Activating, keyboard-focused SpotSearch popup. Data collection, ranking,
    /// and deduplication live in SearchService so this class remains UI/lifecycle only.
    /// </summary>
    public class SearchFormManager : Window
    {
        private const double WindowWidth = 640;
        private const double CompactHeight = 132;
        private const double MaximumWindowHeight = 540;
        private const double ResultRowHeight = 58;

        private static SearchFormManager? _instance;

        private readonly SearchService _searchService = new SearchService();
        private readonly SearchResultIconProvider _iconProvider = new SearchResultIconProvider();
        private readonly List<SearchResult> _visibleResults = new List<SearchResult>();
        private TextBox _searchBox = null!;
        private TextBlock _watermark = null!;
        private TextBlock _emptyState = null!;
        private ListBox _resultsList = null!;
        private bool _isClosing;

        internal long LastRefreshMilliseconds { get; private set; }
        internal long LastQueryMilliseconds { get; private set; }

        public static void ToggleSearch()
        {
            if (_instance == null || !_instance.IsLoaded || _instance._isClosing)
            {
                _instance = new SearchFormManager();
                _instance.Show();
                _instance.Activate();
                _instance.FocusSearch();
            }
            else
            {
                _instance.SafeClose();
            }
        }

        public SearchFormManager()
        {
            SearchVisualStyleHelper.EnsurePalette();
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void InitializeComponent()
        {
            Title = Strings.SearchTitle;
            Width = WindowWidth;
            Height = CompactHeight;
            MaxHeight = MaximumWindowHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            Deactivated += (_, _) => SafeClose();
            PreviewKeyDown += OnSearchKeyDown;

            var surface = new Border();
            surface.SetResourceReference(FrameworkElement.StyleProperty, "DesktopHubSearchSurfaceStyle");

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

            Grid searchRow = CreateSearchRow();
            Grid.SetRow(searchRow, 0);
            layout.Children.Add(searchRow);

            var separator = new Border { BorderThickness = new Thickness(0, 0, 0, 1) };
            separator.SetResourceReference(Border.BorderBrushProperty, "DesktopHubSearchBorderBrush");
            Grid.SetRow(separator, 1);
            layout.Children.Add(separator);

            _resultsList = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 5, 0, 5),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScrollViewer.SetVerticalScrollBarVisibility(_resultsList, ScrollBarVisibility.Auto);
            Grid.SetRow(_resultsList, 2);
            layout.Children.Add(_resultsList);

            _emptyState = new TextBlock
            {
                Text = Strings.SearchScopeHint,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            _emptyState.SetResourceReference(TextBlock.ForegroundProperty, "DesktopHubSearchMutedTextBrush");
            Grid.SetRow(_emptyState, 2);
            layout.Children.Add(_emptyState);

            var footer = new TextBlock
            {
                Text = Strings.SearchKeyboardHint,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 11,
                Margin = new Thickness(14, 0, 14, 7),
                VerticalAlignment = VerticalAlignment.Bottom,
                TextAlignment = TextAlignment.Right
            };
            footer.SetResourceReference(TextBlock.ForegroundProperty, "DesktopHubSearchMutedTextBrush");
            Grid.SetRow(footer, 3);
            layout.Children.Add(footer);

            surface.Child = layout;
            Content = surface;
        }

        private Grid CreateSearchRow()
        {
            var row = new Grid { Margin = new Thickness(16, 0, 16, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var glyph = new TextBlock
            {
                Text = "\uE721",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 19,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "DesktopHubSearchMutedTextBrush");
            Grid.SetColumn(glyph, 0);
            row.Children.Add(glyph);

            var inputLayer = new Grid();
            Grid.SetColumn(inputLayer, 1);

            _watermark = new TextBlock
            {
                Text = Strings.SearchManagedPlaceholder,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            _watermark.SetResourceReference(TextBlock.ForegroundProperty, "DesktopHubSearchMutedTextBrush");

            _searchBox = new TextBox();
            _searchBox.SetResourceReference(FrameworkElement.StyleProperty, "DesktopHubSearchTextBoxStyle");
            _searchBox.TextChanged += OnSearchTextChanged;

            inputLayer.Children.Add(_watermark);
            inputLayer.Children.Add(_searchBox);
            row.Children.Add(inputLayer);
            return row;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var timer = Stopwatch.StartNew();
            _searchService.Refresh();
            timer.Stop();
            LastRefreshMilliseconds = timer.ElapsedMilliseconds;
            FocusSearch();
        }

        public void FocusSearch()
        {
            _searchBox.Focus();
            Keyboard.Focus(_searchBox);
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            _watermark.Visibility = string.IsNullOrEmpty(_searchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            var timer = Stopwatch.StartNew();
            IReadOnlyList<SearchResult> matches = _searchService.Search(_searchBox.Text);
            timer.Stop();
            LastQueryMilliseconds = timer.ElapsedMilliseconds;

            _visibleResults.Clear();
            _visibleResults.AddRange(matches);
            RenderResults();
        }

        private void RenderResults()
        {
            _resultsList.Items.Clear();
            foreach (SearchResult result in _visibleResults)
            {
                var item = new ListBoxItem { Tag = result };
                item.SetResourceReference(FrameworkElement.StyleProperty, "DesktopHubSearchResultItemStyle");
                item.Content = CreateResultRow(result);
                item.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    LaunchResult(result);
                };
                _resultsList.Items.Add(item);
            }

            bool hasQuery = !string.IsNullOrWhiteSpace(_searchBox.Text);
            bool hasResults = _visibleResults.Count > 0;
            _resultsList.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
            _emptyState.Text = hasQuery ? Strings.SearchNoResults : Strings.SearchScopeHint;
            _emptyState.Visibility = hasResults ? Visibility.Collapsed : Visibility.Visible;

            if (hasResults) _resultsList.SelectedIndex = 0;
            Height = hasResults
                ? Math.Min(MaximumWindowHeight, 95 + (_visibleResults.Count * ResultRowHeight))
                : CompactHeight;
        }

        private FrameworkElement CreateResultRow(SearchResult result)
        {
            var row = new Grid { Height = 44 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            ImageSource? icon = _iconProvider.GetIcon(result);
            if (icon != null)
            {
                var image = new Image
                {
                    Source = icon,
                    Width = 30,
                    Height = 30,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(image, 0);
                row.Children.Add(image);
            }
            else
            {
                var fallback = new TextBlock
                {
                    Text = result.IsFolder ? "\uE8B7" : "\uE8A5",
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 22,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                fallback.SetResourceReference(TextBlock.ForegroundProperty, "DesktopHubSearchMutedTextBrush");
                Grid.SetColumn(fallback, 0);
                row.Children.Add(fallback);
            }

            var text = new StackPanel { Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock
            {
                Text = result.DisplayName,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "DesktopHubSearchTextBrush");
            var subtitle = new TextBlock
            {
                Text = result.Subtitle,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 11,
                Opacity = 0.72,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };
            subtitle.SetResourceReference(TextBlock.ForegroundProperty, "DesktopHubSearchMutedTextBrush");
            text.Children.Add(name);
            text.Children.Add(subtitle);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var category = new TextBlock
            {
                Text = Strings.Get(result.CategoryKey),
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 11,
                Margin = new Thickness(8, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.75
            };
            category.SetResourceReference(TextBlock.ForegroundProperty, "DesktopHubSearchMutedTextBrush");
            Grid.SetColumn(category, 2);
            row.Children.Add(category);

            return row;
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                SafeClose();
                return;
            }

            if (_visibleResults.Count == 0) return;

            if (e.Key == Key.Down)
            {
                e.Handled = true;
                _resultsList.SelectedIndex = Math.Min(_visibleResults.Count - 1, _resultsList.SelectedIndex + 1);
                _resultsList.ScrollIntoView(_resultsList.SelectedItem);
            }
            else if (e.Key == Key.Up)
            {
                e.Handled = true;
                _resultsList.SelectedIndex = Math.Max(0, _resultsList.SelectedIndex - 1);
                _resultsList.ScrollIntoView(_resultsList.SelectedItem);
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                int index = Math.Max(0, _resultsList.SelectedIndex);
                LaunchResult(_visibleResults[index]);
            }
        }

        private void LaunchResult(SearchResult result)
        {
            try
            {
                string finalPath = ResolveLaunchPath(result);
                bool exists = File.Exists(finalPath) || Directory.Exists(finalPath);
                bool directUri = Uri.TryCreate(finalPath, UriKind.Absolute, out Uri? uri) && !uri.IsFile;

                if (!exists && !directUri)
                {
                    _visibleResults.Remove(result);
                    RenderResults();
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.SearchItemMissing, Strings.DlgLaunchError);
                    return;
                }

                var startInfo = new ProcessStartInfo { UseShellExecute = true };
                bool isFolder = Directory.Exists(finalPath) || result.IsFolder;
                bool isStoreApp = finalPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                    && Utility.IsStoreAppShortcut(finalPath);

                if (isFolder || isStoreApp)
                {
                    startInfo.FileName = "explorer.exe";
                    startInfo.Arguments = $"\"{finalPath}\"";
                }
                else
                {
                    startInfo.FileName = finalPath;
                    string extension = Path.GetExtension(finalPath);
                    if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".url", StringComparison.OrdinalIgnoreCase) &&
                        !directUri)
                    {
                        string? directory = Path.GetDirectoryName(finalPath);
                        if (!string.IsNullOrWhiteSpace(directory)) startInfo.WorkingDirectory = directory;
                    }
                }

                Process.Start(startInfo);
                SafeClose();
            }
            catch (Exception ex)
            {
                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgLaunchFailed", ex.Message), Strings.DlgError);
            }
        }

        private static string ResolveLaunchPath(SearchResult result)
        {
            string path = result.LaunchTarget;
            if (File.Exists(path) || Directory.Exists(path)) return path;
            if (!string.IsNullOrWhiteSpace(result.Uri)) return result.Uri;
            if (result.ResultType != SearchResultType.FrameItem) return path;

            string fileName = Path.GetFileName(path);
            string profileShortcuts = ProfileManager.GetProfileFilePath("Shortcuts");
            string candidate = Path.Combine(profileShortcuts, fileName);
            if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;

            try
            {
                return Directory.Exists(profileShortcuts)
                    ? Directory.GetFiles(profileShortcuts, fileName, SearchOption.AllDirectories).FirstOrDefault() ?? path
                    : path;
            }
            catch { return path; }
        }

        private void SafeClose()
        {
            if (_isClosing) return;
            _isClosing = true;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _iconProvider.Clear();
            _isClosing = true;
            if (_instance == this) _instance = null;
            base.OnClosed(e);
        }
    }
}
