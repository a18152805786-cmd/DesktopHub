using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Reflection;
using Newtonsoft.Json;

namespace Desktop_Frames
{
    public static class RemoteInfoManager
    {
        // DesktopHub ships with no remote manifest. The upstream feed this once
        // pointed at is another project's channel: it served that project's
        // PayPal announcement into this window, and its Meta carries a
        // ForceKillSwitch that shuts the application down. Neither belongs in a
        // build the upstream author does not publish.
        //
        // The mechanism itself is kept and is strictly opt-in. Drop a
        // developer_repo.txt next to the executable containing an http(s) URL to
        // your own manifest and the check runs against that. With no such file
        // there is no manifest, no network call, and no announcement.
        private const string DeveloperRepoFileName = "developer_repo.txt";

        private static bool _hasChecked = false;

        public static void Initialize()
        {
            if (_hasChecked) return;
            _hasChecked = true;

            string? manifestUrl = ResolveManifestUrl();
            if (manifestUrl == null)
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    "RemoteInfoManager: no manifest configured, remote check skipped.");
                return;
            }

            // Run in background
            Task.Run(async () =>
            {
                await Task.Delay(3000);
                await PerformCheck(manifestUrl);
            });
        }

        /// <summary>
        /// Returns the operator-supplied manifest URL, or null when none is
        /// configured. There is deliberately no built-in default.
        /// </summary>
        private static string? ResolveManifestUrl()
        {
            try
            {
                string path = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, DeveloperRepoFileName);
                if (!System.IO.File.Exists(path)) return null;

                string rawContent = System.IO.File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(rawContent) &&
                    (rawContent.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     rawContent.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    return rawContent;
                }

                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"RemoteInfoManager: {DeveloperRepoFileName} is empty or malformed, remote check skipped.");
                return null;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"RemoteInfoManager: failed to read {DeveloperRepoFileName}: {ex.Message}. Remote check skipped.");
                return null;
            }
        }

        private static async Task PerformCheck(string targetUrl)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopHub"); // Identify app
                    client.Timeout = TimeSpan.FromSeconds(10);

                    // Add Cache Buster
                    string urlWithNoCache = $"{targetUrl}?t={DateTime.Now.Ticks}";

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"RemoteInfoManager: Downloading manifest from {urlWithNoCache}");

                    string json = await client.GetStringAsync(urlWithNoCache);

                    if (string.IsNullOrWhiteSpace(json)) return;

                    var manifest = JsonConvert.DeserializeObject<RemoteManifest>(json);
                    if (manifest == null) return;

                    // 1. Process Updates & Killswitch
                    ProcessMeta(manifest.Meta);

                    // 2. Process Settings (Silent)
                    if (manifest.Settings != null)
                    {
                        if (manifest.Settings.EnableBackgroundLogging)
                            SettingsManager.EnableBackgroundValidationLogging = true;
                    }

                    // 3. Process Announcements (UI)
                    if (manifest.Announcements != null && manifest.Announcements.Count > 0)
                    {
                        foreach (var msg in manifest.Announcements)
                        {
                            if (ShouldShowMessage(msg))
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    // SHOW THE NOTIFICATION WINDOW
                                    NotificationFormManager.Show(msg);
                                });
                                break; // Show only one announcement per session
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Fail silently on network errors
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"RemoteInfoManager Error: {ex.Message}");
            }
        }

        private static void ProcessMeta(RemoteMeta meta)
        {
            if (meta == null) return;

            if (meta.ForceKillSwitch)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
                return;
            }

            // VERSION CHECK (4-Part Support: Major.Minor.Build.Revision)
            Version currentVer = Assembly.GetEntryAssembly().GetName().Version;

            if (Version.TryParse(meta.LatestVersion, out Version remoteVer))
            {
                // This comparison checks version
                if (remoteVer > currentVer)
                {
                    // Construct a fake "Announcement" for the update so we can reuse the UI
                    var updateMsg = new RemoteAnnouncement
                    {
                        Id = $"UPDATE_{remoteVer}", // Unique ID ensures they see it once/max count
                        Title = Strings.UpdateAvailable,
                        Body = $"A new version ({remoteVer}) is available.\nYou are currently using {currentVer}.",
                        Type = meta.CriticalUpdate ? "Alert" : "Info",
                        Link = !string.IsNullOrEmpty(meta.DownloadUrl) ? meta.DownloadUrl : "https://github.com/a18152805786-cmd/DesktopHub/releases",
                        CanUserDismiss = !meta.CriticalUpdate,
                        MaxDisplayCount = meta.CriticalUpdate ? 50 : 3 // Nag more for critical
                    };

                    if (ShouldShowMessage(updateMsg))
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            NotificationFormManager.Show(updateMsg);
                        });
                    }
                }
            }
        }

        private static bool ShouldShowMessage(RemoteAnnouncement msg)
        {
            if (string.IsNullOrEmpty(msg.Id)) return false;

            // 1. Check Dismissal
            if (RegistryHelper.IsMessageDismissed(msg.Id)) return false;

            // 2. Check Max Count
            int currentCount = RegistryHelper.GetMessageDisplayCount(msg.Id);
            if (currentCount >= msg.MaxDisplayCount) return false;

            // 3. Check Version Targeting
            Version currentVer = Assembly.GetEntryAssembly().GetName().Version;

            if (Version.TryParse(msg.TargetVersionMin, out Version minVer) && currentVer < minVer) return false;
            if (Version.TryParse(msg.TargetVersionMax, out Version maxVer) && currentVer > maxVer) return false;

            return true;
        }
    }
}