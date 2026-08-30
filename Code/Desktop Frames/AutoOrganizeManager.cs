using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Desktop_Frames.Localization;

namespace Desktop_Frames
{
    public enum RuleConflictAction { Rename, Overwrite, Skip }

    public class OrganizeRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string Extensions { get; set; } = "";
        public string NameStartsWith { get; set; } = "";
        public string NameContains { get; set; } = "";
        public string TargetFolderPath { get; set; }
        public RuleConflictAction ConflictAction { get; set; } = RuleConflictAction.Rename;
        public bool AutoCreateFrame { get; set; } = false;
        public bool IsEnabled { get; set; } = true;
        public int Priority { get; set; } = 0;
        public DateTime? LastRun { get; set; } // NEW: Tracks last execution
    }

    public static class AutoOrganizeManager
    {
        private static FileSystemWatcher _watcher;
        private static readonly ConcurrentDictionary<string, byte> _queuedFiles = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _queueLock = new();
        private static CancellationTokenSource _queueCancellation = new();
        private const int FileEventDebounceMilliseconds = 1000;
        private const int StabilityCheckDelayMilliseconds = 2000;
        private const int MaxStabilityChecks = 5;
        private static readonly HashSet<string> TemporaryExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".tmp", ".temp", ".crdownload", ".part", ".partial", ".download", ".opdownload", ".aria2"
        };
        public static List<OrganizeRule> Rules = new List<OrganizeRule>(); // Expose to the UI
        private static string _rulesFilePath => ProfileManager.GetProfileFilePath("auto_organize.json");
        private static readonly string _desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        public static void Initialize()
        {
            LoadRules();
            if (SettingsManager.EnableAutoOrganize) Start();
        }

        public static void LoadRules()
        {
            try
            {
                if (File.Exists(_rulesFilePath))
                {
                    string json = File.ReadAllText(_rulesFilePath);
                    Rules = JsonConvert.DeserializeObject<List<OrganizeRule>>(json) ?? new List<OrganizeRule>();
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error loading organize rules: {ex.Message}");
            }
        }

        public static void SaveRules()
        {
            try
            {
                File.WriteAllText(_rulesFilePath, JsonConvert.SerializeObject(Rules, Formatting.Indented));
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error saving organize rules: {ex.Message}");
            }
        }

        public static void Start()
        {
            if (_watcher != null) return;

            try
            {
                _watcher = new FileSystemWatcher(_desktopPath)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };

                // FileSystemWatcher may emit Created/Changed/Renamed several times for one file.
                // Queue through one short debounce window so one path cannot be moved twice.
                _watcher.Created += OnFileEvent;
                _watcher.Changed += OnFileEvent;
                _watcher.Renamed += OnFileRenamed;

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Auto-Organize engine started.");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Failed to start Auto-Organize: {ex.Message}");
            }
        }

        public static void Stop()
        {
            lock (_queueLock)
            {
                _queueCancellation.Cancel();
                _queueCancellation = new CancellationTokenSource();
                _queuedFiles.Clear();
            }

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Auto-Organize engine stopped.");
            }
        }

        private static void OnFileEvent(object sender, FileSystemEventArgs e) => QueueFile(e.FullPath);

        private static void OnFileRenamed(object sender, RenamedEventArgs e) => QueueFile(e.FullPath);

        private static void QueueFile(string filePath)
        {
            if (!SettingsManager.EnableAutoOrganize || string.IsNullOrWhiteSpace(filePath)) return;

            string fullPath;
            try { fullPath = Path.GetFullPath(filePath); }
            catch { return; }

            CancellationToken cancellationToken;
            lock (_queueLock)
            {
                if (!_queuedFiles.TryAdd(fullPath, 0)) return;
                cancellationToken = _queueCancellation.Token;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(FileEventDebounceMilliseconds, cancellationToken);
                    if (!cancellationToken.IsCancellationRequested)
                        await ProcessFileAsync(fullPath, cancellationToken);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Auto-Organize queue failed for {fullPath}: {ex.Message}");
                }
                finally
                {
                    _queuedFiles.TryRemove(fullPath, out _);
                }
            });
        }
  
        // FIX: Added 'silent' parameter so the engine can sweep seamlessly in the background
        public static void ProcessDesktopNow(bool silent = false)
        {
            if (Rules == null || Rules.Count == 0) return;

            Task.Run(async () =>
            {
                var files = Directory.GetFiles(_desktopPath);
                foreach (var file in files)
                {
                    await ProcessFileAsync(file);
                }

                if (!silent)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.MsgDesktopOrganized, Strings.DlgSuccess);
                    });
                }
            });
        }

        private static async Task ProcessFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested || !File.Exists(filePath) || !SettingsManager.EnableAutoOrganize) return;

            string fileName = Path.GetFileName(filePath);
            string ext = Path.GetExtension(filePath).ToLower();

            // Ignore virtual items, temporary downloads, desktop shell metadata, and
            // hidden/system files. If attributes cannot be read, fail closed.
            if (IsIgnoredFile(filePath, fileName, ext)) return;

            // Sort rules by priority (lower number = higher priority)
            var activeRules = Rules.Where(r => r.IsEnabled).OrderBy(r => r.Priority).ToList();

            foreach (var rule in activeRules)
            {
                if (DoesFileMatchRule(fileName, rule))
                {
                    await ExecuteMoveAsync(filePath, rule, cancellationToken);
                    break; // File processed, stop checking rules
                }
            }
        }

        private static bool IsIgnoredFile(string filePath, string fileName, string extension)
        {
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase)) return true;
            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)) return true;
            if (TemporaryExtensions.Contains(extension)) return true;

            try
            {
                var attributes = File.GetAttributes(filePath);
                return (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
            }
            catch
            {
                return true;
            }
        }

        private static bool DoesFileMatchRule(string fileName, OrganizeRule rule)
        {
            // 1. Check Extensions
            if (!string.IsNullOrWhiteSpace(rule.Extensions) && !rule.Extensions.Contains("*.*") && rule.Extensions.Trim() != "*")
            {
                string fileExt = Path.GetExtension(fileName).ToLower();
                var allowedExts = rule.Extensions.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(e => e.Trim().Replace("*", "").ToLower());

                // If the file extension is NOT in the allowed list, reject it
                if (!allowedExts.Contains(fileExt)) return false;
            }

            // 2. Check a strict filename prefix (AND condition)
            if (!string.IsNullOrWhiteSpace(rule.NameStartsWith) &&
                !fileName.StartsWith(rule.NameStartsWith, StringComparison.OrdinalIgnoreCase))
                return false;

            // 3. Check Name Contains (AND condition)
            if (!string.IsNullOrWhiteSpace(rule.NameContains))
            {
                // If the filename does NOT contain the required text, reject it
                if (fileName.IndexOf(rule.NameContains, StringComparison.OrdinalIgnoreCase) == -1)
                    return false;
            }

            // If it survived both filters, it is a perfect match!
            return true;
        }

        internal static bool AreSameDirectory(string? firstPath, string? secondPath)
        {
            if (string.IsNullOrWhiteSpace(firstPath) || string.IsNullOrWhiteSpace(secondPath)) return false;

            try
            {
                string firstDirectory = Path.GetFullPath(firstPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string secondDirectory = Path.GetFullPath(secondPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(firstDirectory, secondDirectory, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static async Task ExecuteMoveAsync(string sourcePath, OrganizeRule rule, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested || !SettingsManager.EnableAutoOrganize) return;

            string? sourceDirectory;
            try { sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath)); }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Auto-Organize skipped {sourcePath}: invalid source path ({ex.Message}).");
                return;
            }

            // Moving back into the watched source folder would rename the file and
            // enqueue our own rename event indefinitely. Skip before any disk change.
            if (AreSameDirectory(sourceDirectory, rule.TargetFolderPath))
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Auto-Organize skipped {Path.GetFileName(sourcePath)}: source and target folders are the same.");
                return;
            }

            if (!Directory.Exists(rule.TargetFolderPath))
            {
                try { Directory.CreateDirectory(rule.TargetFolderPath); }
                catch { return; }
            }

            // Wait for two consecutive unchanged length/write-time observations and
            // a safe read open. This prevents moving a file while it is still written.
            if (!await WaitForFileStabilityAsync(sourcePath, cancellationToken))
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Auto-Organize skipped {Path.GetFileName(sourcePath)}: File was active, locked, or unstable.");
                return;
            }

            if (cancellationToken.IsCancellationRequested || !SettingsManager.EnableAutoOrganize) return;

            string fileName = Path.GetFileName(sourcePath);
            string destPath = Path.Combine(rule.TargetFolderPath, fileName);

            try
            {
                // --- CONFLICT PROTOCOL ---
                if (File.Exists(destPath))
                {
                    if (rule.ConflictAction == RuleConflictAction.Rename)
                    {
                        string nameOnly = Path.GetFileNameWithoutExtension(fileName);
                        string ext = Path.GetExtension(fileName);
                        int count = 1;

                        while (File.Exists(destPath))
                        {
                            destPath = Path.Combine(rule.TargetFolderPath, $"{nameOnly} ({count}){ext}");
                            count++;
                        }
                    }
                    else
                    {
                        // Overwrite is intentionally fail-closed for old rules and old JSON.
                        // The existing destination and source are both preserved.
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Auto-Organize skipped {fileName}: destination already exists and overwrite is disabled.");
                        return;
                    }
                }

                // Execute Physical Move
                if (cancellationToken.IsCancellationRequested || !SettingsManager.EnableAutoOrganize) return;
                File.Move(sourcePath, destPath, overwrite: false);
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Auto-Organize moved {fileName} to {rule.TargetFolderPath}");

                // --- SUCCESS TRACKING & NOTIFICATIONS ---
                rule.LastRun = DateTime.Now;
                SaveRules(); // Save the new timestamp

                if (SettingsManager.EnableAutoOrganizeNotifications)
                {
                    SmartToast.Show($"Rule: {rule.Name} Executed", $"Moved '{fileName}' to {new DirectoryInfo(rule.TargetFolderPath).Name}");
                }


                // --- AUTO-CREATE FRAME PROTOCOL ---
                if (rule.AutoCreateFrame)
                {
                    Application.Current.Dispatcher.Invoke(() => CheckAndCreatePortalFrame(rule.TargetFolderPath));
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Auto-Organize failed to move {fileName}: {ex.Message}");
            }
        }

        public static void CheckAndCreatePortalFrame(string targetFolder)
        {
            // Prevent duplicate frames: Check if a portal frame already points to this folder
            var existingFrames = Framemanager.GetFrameData();
            foreach (var f in existingFrames)
            {
                try
                {
                    string fType = null, fPath = null;
                    if (f is Newtonsoft.Json.Linq.JObject jObj)
                    {
                        fType = jObj["ItemsType"]?.ToString();
                        fPath = jObj["Path"]?.ToString(); // FIX: Changed from PortalFolderPath to Path
                    }
                    else
                    {
                        fType = f.GetType().GetProperty("ItemsType")?.GetValue(f)?.ToString() ?? f.ItemsType?.ToString();
                        fPath = f.GetType().GetProperty("Path")?.GetValue(f)?.ToString() ?? f.Path?.ToString(); // FIX
                    }

                    if (fType == "Portal" && string.Equals(fPath, targetFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        return; // Frame already exists!
                    }
                }
                catch { }
            }

            // Command Framemanager to generate a new Portal Frame using the exact required schema
            dynamic newFrame = new Newtonsoft.Json.Linq.JObject();
            newFrame.Id = Guid.NewGuid().ToString();
            newFrame.Title = new DirectoryInfo(targetFolder).Name; // FIX: Changed from Name to Title
            newFrame.X = 100.0; // FIX: Changed from Left to X
            newFrame.Y = 100.0; // FIX: Changed from Top to Y
            newFrame.Width = 350.0;
            newFrame.Height = 250.0;
            newFrame.ItemsType = "Portal";
            newFrame.Path = targetFolder; // FIX: Changed from PortalFolderPath to Path
            newFrame.Items = ""; // Portal expects empty string for Items

            // FIX: Initialize ALL frame properties with defaults to match JSON structure exactly
            newFrame.IsLocked = "false";
            newFrame.IsHidden = "false";
            newFrame.CustomColor = null;
            newFrame.CustomLaunchEffect = null;
            newFrame.IsRolled = "false";
            newFrame.UnrolledHeight = 250.0;
            newFrame.TextColor = null;
            newFrame.BoldTitleText = "false";
            newFrame.TitleTextColor = null;
            newFrame.DisableTextShadow = "false";
            newFrame.IconSize = "Medium";
            newFrame.GrayscaleIcons = "false";
            newFrame.IconSpacing = 5;
            newFrame.TitleTextSize = "Medium";
            newFrame.FrameBorderColor = null;
            newFrame.FrameBorderThickness = 2;

            // TABS FEATURE requirements
            newFrame.TabsEnabled = "false";
            newFrame.CurrentTab = 0;
            newFrame.Tabs = new Newtonsoft.Json.Linq.JArray();

            // 1. Add to the central data manager
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                FrameDataManager.FrameData.Add(newFrame);

                // 2. Pass a TargetChecker instance to the creation engine
                Framemanager.CreateFrame(newFrame, new TargetChecker(1000));

                // 3. Save using the correct data manager method
                FrameDataManager.SaveFrameData();
            });

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Auto-Organize spawned a new Portal Frame for {targetFolder}");
        }

        private static async Task<bool> WaitForFileStabilityAsync(string filePath, CancellationToken cancellationToken)
        {
            long previousLength = -1;
            DateTime previousWriteTimeUtc = DateTime.MinValue;

            for (int attempt = 0; attempt < MaxStabilityChecks; attempt++)
            {
                if (cancellationToken.IsCancellationRequested || !SettingsManager.EnableAutoOrganize || !File.Exists(filePath))
                    return false;

                try
                {
                    var info = new FileInfo(filePath);
                    long currentLength = info.Length;
                    DateTime currentWriteTimeUtc = info.LastWriteTimeUtc;
                    bool unchanged = currentLength == previousLength && currentWriteTimeUtc == previousWriteTimeUtc;
                    previousLength = currentLength;
                    previousWriteTimeUtc = currentWriteTimeUtc;

                    if (unchanged)
                    {
                        using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            return true;
                    }
                }
                catch (IOException)
                {
                    // The file is still being written or otherwise unavailable.
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }

                try { await Task.Delay(StabilityCheckDelayMilliseconds, cancellationToken); }
                catch (OperationCanceledException) { return false; }
            }

            return false;
        }


        public static void Pause()
        {
            if (_watcher != null) _watcher.EnableRaisingEvents = false;
        }

        public static void Resume()
        {
            if (!SettingsManager.EnableAutoOrganize) return;

            if (_watcher == null)
            {
                Start();
            }
            else
            {
                _watcher.EnableRaisingEvents = true;
            }

            // Silently sweep the desktop to catch any files that landed while we were paused!
            ProcessDesktopNow(true);
        }
    }
}
