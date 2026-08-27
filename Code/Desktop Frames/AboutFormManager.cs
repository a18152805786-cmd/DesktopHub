using Desktop_Frames.Localization;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Desktop_Frames
{
    /// <summary>
    /// Manages all modern WPF forms with proper DPI scaling
    /// Progressively replaces Windows Forms dialogs
    /// </summary>
    public static class AboutFormManager
    {
        /// <summary>
        /// Shows the modern About form with DPI scaling
        /// </summary>
        public static void ShowAboutForm()
        {
            try
            {
                var aboutWindow = new Window
                {
                    Title = Strings.AboutTitle,
                    Width = 480,
                    Height = 505,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.None,
                    Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                    AllowsTransparency = true
                };

                // Set icon from executable
                try
                {
                    aboutWindow.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName).Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions()
                    );
                }
                catch { } // Ignore icon loading errors

                // Main container with white background and shadow
                Border mainBorder = new Border
                {
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(0),
                    Margin = new Thickness(8),
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        Direction = 270,
                        ShadowDepth = 2,
                        BlurRadius = 8,
                        Opacity = 0.1
                    }
                };

                // Root grid layout
                Grid rootGrid = new Grid();
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

                // HEADER: Logo, Title, Version, Close Button
                CreateHeader(rootGrid);

                // CONTENT: Scrollable content area
                CreateContent(rootGrid);

                mainBorder.Child = rootGrid;

                aboutWindow.Content = mainBorder;


                // Make window draggable ONLY by header to avoid button click conflicts
                // Get the header from the root grid for dragging
                var headerElement = rootGrid.Children.OfType<Border>().FirstOrDefault();
                if (headerElement != null)
                {
                    headerElement.MouseLeftButtonDown += (s, e) =>
                    {
                        if (e.LeftButton == MouseButtonState.Pressed)
                        {
                            try
                            {
                                aboutWindow.DragMove();
                            }
                            catch { } // Ignore DragMove exceptions
                        }
                    };
                }

                aboutWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error showing About form: {ex.Message}");
                MessageBox.Show($"Error showing About form: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void CreateHeader(Grid rootGrid)
        {
            Border headerBorder = new Border
            {
                Background = Brushes.White,
                Padding = new Thickness(16, 16, 16, 0),
                Height = 90
            };

            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Logo
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Title area
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Close button

            // DesktopHub word mark. A plain glyph drawn in code keeps the
            // window free of any third-party logo asset.
            Border logoPlaceholder = new Border
            {
                Width = 64,
                Height = 64,
                Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x51, 0x8B)),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 16, 0),
                Child = new TextBlock
                {
                    Text = "DH",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 26,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            // Title and Version area
            StackPanel titleArea = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };

            TextBlock titleText = new TextBlock
            {
                Text = "DesktopHub",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 22, // Your improved font size
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                Margin = new Thickness(0, 0, 0, 4)
            };

            TextBlock versionText = new TextBlock
            {
                Text = Strings.Get("AboutVersion", Assembly.GetExecutingAssembly().GetName().Version),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14, // Your improved font size
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104))
            };

            titleArea.Children.Add(titleText);
            titleArea.Children.Add(versionText);

            // Close Button
            Button closeButton = new Button
            {
                Content = "✕",
                Width = 32,
                Height = 32,
                FontSize = 22, // Your improved font size
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -8, 0, 0)
            };

            closeButton.MouseEnter += (s, e) => closeButton.Background = new SolidColorBrush(Color.FromRgb(241, 243, 244));
            closeButton.MouseLeave += (s, e) => closeButton.Background = Brushes.Transparent;
            closeButton.Click += (s, e) => ((Window)((FrameworkElement)s).TemplatedParent ?? Window.GetWindow((FrameworkElement)s)).Close();

            headerGrid.Children.Add(logoPlaceholder);
            headerGrid.Children.Add(titleArea);
            headerGrid.Children.Add(closeButton);
            Grid.SetColumn(titleArea, 1);
            Grid.SetColumn(closeButton, 2);

            headerBorder.Child = headerGrid;
            Grid.SetRow(headerBorder, 0);
            rootGrid.Children.Add(headerBorder);
        }

        private static void CreateContent(Grid rootGrid)
        {
            ScrollViewer scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(20, 0, 20, 20)
            };

            StackPanel contentStack = new StackPanel();

            // About Section
            CreateSection(contentStack, Strings.AboutSectionAbout, Strings.AboutTagline,
                Strings.AboutBody, 20);

            // Project Section: source code only. No donation or third-party
            // promotion belongs in this window.
            CreateProjectSection(contentStack);

            // Upstream attribution and licence, kept as a secondary section.
            CreateLicenseSection(contentStack);

            scrollViewer.Content = contentStack;
            Grid.SetRow(scrollViewer, 1);
            rootGrid.Children.Add(scrollViewer);
        }

        private static void CreateSection(StackPanel parent, string title, string subtitle, string content, double bottomMargin)
        {
            StackPanel section = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, bottomMargin)
            };

            // Title
            TextBlock titleBlock = new TextBlock
            {
                Text = title,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 18, // Your improved font size
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                Margin = new Thickness(0, 0, 0, subtitle != null ? 8 : 12)
            };
            section.Children.Add(titleBlock);

            // Subtitle (optional)
            if (!string.IsNullOrEmpty(subtitle))
            {
                TextBlock subtitleBlock = new TextBlock
                {
                    Text = subtitle,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 14, // Your improved font size
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(60, 64, 67)),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                section.Children.Add(subtitleBlock);
            }

            // Content
            TextBlock contentBlock = new TextBlock
            {
                Text = content,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14, // Your improved font size
                Foreground = new SolidColorBrush(Color.FromRgb(60, 64, 67)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 16
            };
            section.Children.Add(contentBlock);

            parent.Children.Add(section);
        }

        private static void CreateProjectSection(StackPanel parent)
        {
            StackPanel section = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 20)
            };

            TextBlock titleBlock = new TextBlock
            {
                Text = Strings.AboutSectionProject,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                Margin = new Thickness(0, 0, 0, 12)
            };
            section.Children.Add(titleBlock);

            Button githubButton = new Button
            {
                Content = Strings.AboutVisitGitHub,
                Height = 36,
                Padding = new Thickness(16, 0, 16, 0),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x51, 0x8B)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            githubButton.MouseEnter += (s, e) => githubButton.Background = new SolidColorBrush(Color.FromRgb(0x17, 0x3E, 0x6B));
            githubButton.MouseLeave += (s, e) => githubButton.Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x51, 0x8B));
            githubButton.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/a18152805786-cmd/DesktopHub",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error opening GitHub link: {ex.Message}");
                }
            };

            section.Children.Add(githubButton);
            parent.Children.Add(section);
        }

        private static void CreateLicenseSection(StackPanel parent)
        {
            Border separator = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                Margin = new Thickness(0, 10, 0, 15)
            };
            parent.Children.Add(separator);

            TextBlock sectionTitle = new TextBlock
            {
                Text = Strings.AboutSectionCredits,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 64, 67)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            parent.Children.Add(sectionTitle);

            // Upstream attribution stays. DesktopHub is a modification of an MIT
            // licensed project and says so.
            TextBlock attribution = new TextBlock
            {
                Text = Strings.AboutCreditsBody,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 16,
                Margin = new Thickness(0, 0, 0, 12)
            };
            parent.Children.Add(attribution);

            TextBlock licenseBlock = new TextBlock
            {
                Text = Strings.AboutLicense,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                Margin = new Thickness(0, 0, 0, 6)
            };
            parent.Children.Add(licenseBlock);
        }

    }
}