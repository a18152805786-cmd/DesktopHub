using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Desktop_Frames.NativeDesktop
{
    internal static class DesktopGroupHeaderManager
    {
        private static readonly List<DesktopGroupHeaderWindow> Windows = new();
        private static readonly object SyncRoot = new();

        public static int WindowCount
        {
            get { lock (SyncRoot) return Windows.Count(w => w.IsLoaded); }
        }

        public static void Show(DesktopLayoutPlan plan)
        {
            CloseAll();
            foreach (DesktopLayoutGroup group in plan.Groups.Where(g => g.PlannedItemCount > 0 && g.HeaderWidth > 0))
            {
                var window = new DesktopGroupHeaderWindow(
                    group.GroupDisplayName,
                    group.HeaderLeft,
                    group.HeaderTop,
                    group.HeaderWidth,
                    group.HeaderHeight);
                lock (SyncRoot) Windows.Add(window);
                window.Show();
            }
        }

        public static void CloseAll()
        {
            lock (SyncRoot)
            {
                foreach (DesktopGroupHeaderWindow window in Windows.Where(w => w.IsLoaded).ToList())
                    window.Close();
                Windows.Clear();
            }
        }

        public static void Save(DesktopLayoutPlan plan, string path)
        {
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, JsonConvert.SerializeObject(plan, Formatting.Indented), new UTF8Encoding(false));
        }

        public static bool RestoreOnStartup(string path)
        {
            if (!File.Exists(path)) return false;
            DesktopLayoutPlan? plan = JsonConvert.DeserializeObject<DesktopLayoutPlan>(File.ReadAllText(path, Encoding.UTF8));
            if (plan == null || plan.LayoutMode != NativeDesktopLayoutMode.DualRail.ToString()) return false;
            Show(plan);
            return true;
        }
    }
}
