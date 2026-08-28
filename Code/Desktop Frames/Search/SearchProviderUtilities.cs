using System;
using System.IO;

namespace Desktop_Frames.Search
{
    internal static class SearchProviderUtilities
    {
        public static bool ShouldSkip(string path)
        {
            try
            {
                if (string.Equals(Path.GetFileName(path), "desktop.ini", StringComparison.OrdinalIgnoreCase))
                    return true;

                FileAttributes attributes = File.GetAttributes(path);
                return (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
            }
            catch
            {
                return true;
            }
        }

        public static SearchResult FromPath(string path, string provider, bool frameItem = false)
        {
            string fullPath = NormalizePath(path);
            bool isFolder = Directory.Exists(fullPath);
            string extension = isFolder ? string.Empty : Path.GetExtension(fullPath);
            SearchResultType type = frameItem
                ? SearchResultType.FrameItem
                : isFolder
                    ? SearchResultType.Folder
                    : extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                        ? SearchResultType.Shortcut
                        : extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
                            ? SearchResultType.Url
                            : SearchResultType.File;

            string targetPath = string.Empty;
            string arguments = string.Empty;
            string uri = string.Empty;

            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
            {
                targetPath = Utility.GetShortcutTarget(fullPath) ?? string.Empty;
                arguments = Utility.GetShortcutArguments(fullPath) ?? string.Empty;
            }
            else if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
            {
                uri = ReadInternetShortcutUri(fullPath);
            }

            var result = new SearchResult
            {
                DisplayName = isFolder ? Path.GetFileName(fullPath) : Path.GetFileNameWithoutExtension(fullPath),
                FullPathOrUri = string.IsNullOrEmpty(uri) ? fullPath : uri,
                ResultType = type,
                CategoryKey = CategoryKeyFor(fullPath, isFolder, frameItem),
                SourceProvider = provider,
                IconSource = fullPath,
                Subtitle = FriendlyPath(fullPath),
                LaunchTarget = fullPath,
                OriginalPath = fullPath,
                TargetPath = targetPath,
                LaunchArguments = arguments,
                Uri = uri,
                IsFolder = isFolder
            };

            AddAlias(result, "DisplayName", result.DisplayName);
            AddAlias(result, "FileName", Path.GetFileName(fullPath));
            AddAlias(result, "FileNameWithoutExtension", Path.GetFileNameWithoutExtension(fullPath));
            AddAlias(result, "TargetPath", targetPath);
            AddAlias(result, "TargetFileName", Path.GetFileName(targetPath));
            AddAlias(result, "TargetFileNameWithoutExtension", Path.GetFileNameWithoutExtension(targetPath));
            AddAlias(result, "Arguments", arguments);
            AddAlias(result, "URI", uri);
            return result;
        }

        public static void AddAlias(SearchResult result, string field, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (result.SearchAliases.Exists(alias =>
                    alias.Field.Equals(field, StringComparison.OrdinalIgnoreCase) &&
                    alias.Value.Equals(value, StringComparison.CurrentCultureIgnoreCase))) return;
            result.SearchAliases.Add(new SearchAlias(field, value));
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return path.Trim(); }
        }

        public static string FriendlyPath(string path)
        {
            string normalized = NormalizePath(path);
            string desktop = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            if (normalized.Equals(desktop, StringComparison.OrdinalIgnoreCase)) return "Desktop";
            if (normalized.StartsWith(desktop + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return "Desktop" + normalized.Substring(desktop.Length);
            return normalized;
        }

        public static string CategoryKeyFor(string path, bool isFolder, bool frameItem)
        {
            if (frameItem) return "Search_CategoryFrameItem";
            if (isFolder) return "Search_CategoryFolder";

            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".doc" or ".docx" or ".txt" or ".rtf" or ".md" => "Search_CategoryDocument",
                ".ppt" or ".pptx" or ".pps" or ".ppsx" => "Search_CategoryPpt",
                ".pdf" => "Search_CategoryPdf",
                ".xls" or ".xlsx" or ".xlsm" or ".csv" or ".dta" => "Search_CategoryData",
                ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" => "Search_CategoryImage",
                ".zip" or ".rar" or ".7z" => "Search_CategoryArchive",
                ".msi" or ".msix" => "Search_CategoryInstaller",
                ".lnk" => "Search_CategoryShortcut",
                ".url" => "Search_CategoryUrl",
                _ => "Search_CategoryDesktop"
            };
        }

        private static string ReadInternetShortcutUri(string path)
        {
            try
            {
                foreach (string line in File.ReadLines(path))
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(4).Trim();
                }
            }
            catch { }
            return string.Empty;
        }
    }
}
