using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// Event-driven access to the native Desktop ListView. This service only
    /// reads and sets visual item coordinates. It does not modify filesystem,
    /// registry, Explorer settings, or Desktop icon visibility.
    /// </summary>
    public sealed class NativeDesktopIconLayoutService
    {
        private const int GwlStyle = -16;
        private const long LvsAutoArrange = 0x0100;
        private const int LvmFirst = 0x1000;
        private const int LvmGetItemCount = LvmFirst + 4;
        private const int LvmGetItemPosition = LvmFirst + 16;
        private const int LvmSetItemPosition32 = LvmFirst + 49;
        private const int LvmGetItemSpacing = LvmFirst + 51;
        private const int LvmGetExtendedListViewStyle = LvmFirst + 55;
        private const int LvmGetItemTextW = LvmFirst + 115;
        private const long LvsExSnapToGrid = 0x00080000;
        private const uint LvifText = 0x0001;
        private const uint ProcessVmOperation = 0x0008;
        private const uint ProcessVmRead = 0x0010;
        private const uint ProcessVmWrite = 0x0020;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint MemCommit = 0x1000;
        private const uint MemReserve = 0x2000;
        private const uint MemRelease = 0x8000;
        private const uint PageReadWrite = 0x04;
        private const uint SmtoAbortIfHung = 0x0002;
        private const uint MessageTimeoutMs = 2000;
        private const int TextCapacity = 1024;

        public NativeDesktopListViewState GetListViewState()
        {
            IntPtr listView = RequireDesktopListView();
            long styles = GetWindowStyle(listView);
            long extendedStyles = Send(listView, LvmGetExtendedListViewStyle, IntPtr.Zero, IntPtr.Zero).ToInt64();
            // FALSE requests spacing for the normal icon view used by the Desktop.
            long spacing = Send(listView, LvmGetItemSpacing, IntPtr.Zero, IntPtr.Zero).ToInt64();
            return new NativeDesktopListViewState
            {
                Handle = listView,
                AutoArrangeEnabled = (styles & LvsAutoArrange) != 0,
                AlignToGridEnabled = (extendedStyles & LvsExSnapToGrid) != 0,
                GridSpacingX = unchecked((ushort)(spacing & 0xffff)),
                GridSpacingY = unchecked((ushort)((spacing >> 16) & 0xffff))
            };
        }

        public NativeDesktopSafeGrid GetSafeDesktopGrid()
        {
            IntPtr listView = RequireDesktopListView();
            long spacing = Send(listView, LvmGetItemSpacing, IntPtr.Zero, IntPtr.Zero).ToInt64();
            int spacingX = Math.Max(1, (int)unchecked((ushort)(spacing & 0xffff)));
            int spacingY = Math.Max(1, (int)unchecked((ushort)((spacing >> 16) & 0xffff)));
            if (!GetClientRect(listView, out NativeRect client) || client.Right <= 0 || client.Bottom <= 0)
                throw new InvalidOperationException("Desktop ListView client rect was not readable.");

            int rightMargin = Math.Max(1, (int)Math.Ceiling(spacingX * 0.25));
            int bottomMargin = Math.Max(1, (int)Math.Ceiling(spacingY * 0.25));
            int safeColumns = Math.Max(1, (client.Right - rightMargin) / spacingX);
            int safeRows = Math.Max(1, (client.Bottom - bottomMargin) / spacingY);
            return new NativeDesktopSafeGrid
            {
                ClientWidth = client.Right,
                ClientHeight = client.Bottom,
                IconSpacingX = spacingX,
                IconSpacingY = spacingY,
                SafeColumns = safeColumns,
                SafeRows = safeRows,
                RightSafetyMargin = rightMargin,
                BottomSafetyMargin = bottomMargin
            };
        }

        public IReadOnlyList<NativeDesktopIconState> EnumerateDesktopIcons()
        {
            IntPtr listView = RequireDesktopListView();
            var resolver = new NativeDesktopIconIdentityResolver();
            int count = Send(listView, LvmGetItemCount, IntPtr.Zero, IntPtr.Zero).ToInt32();
            var icons = new List<NativeDesktopIconState>(Math.Max(count, 0));

            using var process = new ExplorerProcessMemory(listView);
            for (int index = 0; index < count; index++)
            {
                string displayName = GetItemText(listView, process, index);
                NativePoint point = GetItemPosition(listView, process, index);
                var identity = resolver.Resolve(displayName);
                icons.Add(new NativeDesktopIconState
                {
                    RuntimeIndex = index,
                    DisplayName = displayName,
                    Identity = identity.Identity,
                    OriginalPath = identity.OriginalPath,
                    IdentityStatus = identity.Status,
                    X = point.X,
                    Y = point.Y
                });
            }
            return icons;
        }

        public NativeDesktopLayoutSnapshot CaptureSnapshot()
        {
            NativeDesktopListViewState state = GetListViewState();
            return new NativeDesktopLayoutSnapshot
            {
                AutoArrangeEnabled = state.AutoArrangeEnabled,
                AlignToGridEnabled = state.AlignToGridEnabled,
                GridSpacingX = state.GridSpacingX,
                GridSpacingY = state.GridSpacingY,
                Icons = EnumerateDesktopIcons().Select(icon => new NativeDesktopIconSnapshotItem
                {
                    Identity = icon.Identity,
                    OriginalPath = icon.OriginalPath,
                    DisplayName = icon.DisplayName,
                    IdentityStatus = icon.IdentityStatus,
                    X = icon.X,
                    Y = icon.Y
                }).ToList()
            };
        }

        public bool TrySetPosition(string originalPath, int x, int y,
            out NativeDesktopIconState? updated, out string reason)
        {
            updated = null;
            reason = string.Empty;
            string normalized = NormalizePath(originalPath);
            NativeDesktopListViewState state = GetListViewState();
            if (state.AutoArrangeEnabled)
            {
                reason = "AUTO_ARRANGE_ENABLED";
                return false;
            }

            NativeDesktopIconState[] matches = EnumerateDesktopIcons()
                .Where(icon => icon.IdentityStatus == NativeIconIdentityStatus.StablePath &&
                    string.Equals(NormalizePath(icon.OriginalPath), normalized, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
            {
                reason = matches.Length == 0 ? "ICON_NOT_FOUND" : "AMBIGUOUS_ICON_IDENTITY";
                return false;
            }

            NativeDesktopIconState match = matches[0];
            IntPtr listView = state.Handle;
            using var process = new ExplorerProcessMemory(listView);
            SetItemPosition(listView, process, match.RuntimeIndex, x, y);

            NativePoint actual = GetItemPosition(listView, process, match.RuntimeIndex);
            updated = new NativeDesktopIconState
            {
                RuntimeIndex = match.RuntimeIndex,
                DisplayName = match.DisplayName,
                Identity = match.Identity,
                OriginalPath = match.OriginalPath,
                IdentityStatus = match.IdentityStatus,
                X = actual.X,
                Y = actual.Y
            };
            return true;
        }

        public NativeDesktopRestoreResult Restore(NativeDesktopLayoutSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var result = new NativeDesktopRestoreResult();
            NativeDesktopListViewState state = GetListViewState();
            if (state.AutoArrangeEnabled)
            {
                result.Failed = snapshot.Icons.Count(icon =>
                    icon.IdentityStatus == NativeIconIdentityStatus.StablePath);
                result.Errors.Add("AUTO_ARRANGE_ENABLED");
                return result;
            }

            Dictionary<string, NativeDesktopIconState[]> current = EnumerateDesktopIcons()
                .Where(icon => icon.IdentityStatus == NativeIconIdentityStatus.StablePath)
                .GroupBy(icon => NormalizePath(icon.OriginalPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

            IntPtr listView = state.Handle;
            using var process = new ExplorerProcessMemory(listView);
            foreach (NativeDesktopIconSnapshotItem item in snapshot.Icons)
            {
                if (item.IdentityStatus == NativeIconIdentityStatus.UnmanagedShellItem)
                {
                    result.SkippedUnmanaged++;
                    continue;
                }
                if (item.IdentityStatus == NativeIconIdentityStatus.AmbiguousDisplayName)
                {
                    result.SkippedAmbiguous++;
                    continue;
                }

                string path = NormalizePath(item.OriginalPath);
                if (!current.TryGetValue(path, out NativeDesktopIconState[]? matches) || matches.Length == 0)
                {
                    result.SkippedMissing++;
                    continue;
                }
                if (matches.Length != 1)
                {
                    result.SkippedAmbiguous++;
                    continue;
                }

                try
                {
                    SetItemPosition(listView, process, matches[0].RuntimeIndex, item.X, item.Y);
                    result.Restored++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"{item.DisplayName}: {ex.Message}");
                }
            }
            return result;
        }

        /// <summary>Read-only identity preflight used before a restore or a production layout apply.</summary>
        public NativeDesktopSnapshotValidationResult ValidateSnapshot(NativeDesktopLayoutSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            NativeDesktopListViewState state = GetListViewState();
            var result = new NativeDesktopSnapshotValidationResult { AutoArrangeEnabled = state.AutoArrangeEnabled };
            Dictionary<string, int> current = EnumerateDesktopIcons()
                .Where(icon => icon.IdentityStatus == NativeIconIdentityStatus.StablePath)
                .GroupBy(icon => NormalizePath(icon.OriginalPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (NativeDesktopIconSnapshotItem item in snapshot.Icons)
            {
                if (item.IdentityStatus != NativeIconIdentityStatus.StablePath)
                {
                    result.SkippedUnmanaged++;
                    continue;
                }
                if (!current.TryGetValue(NormalizePath(item.OriginalPath), out int count))
                    result.Missing++;
                else if (count != 1)
                    result.Ambiguous++;
                else
                    result.CanResolve++;
            }
            return result;
        }

        public static void SaveSnapshot(NativeDesktopLayoutSnapshot snapshot, string path)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Snapshot path is required.", nameof(path));
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            using FileStream stream = new(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using StreamWriter writer = new(stream, new UTF8Encoding(false));
            writer.Write(JsonConvert.SerializeObject(snapshot, Formatting.Indented));
        }

        public static NativeDesktopLayoutSnapshot LoadSnapshot(string path)
        {
            string json = File.ReadAllText(Path.GetFullPath(path), Encoding.UTF8);
            return JsonConvert.DeserializeObject<NativeDesktopLayoutSnapshot>(json)
                ?? throw new InvalidDataException("Native Desktop layout snapshot is empty or invalid.");
        }

        private static string GetItemText(IntPtr listView, ExplorerProcessMemory process, int index)
        {
            int characterBytes = TextCapacity * sizeof(char);
            using RemoteBuffer remoteText = process.Allocate(characterBytes);
            using RemoteBuffer remoteItem = process.Allocate(Marshal.SizeOf<NativeLvItem>());
            var item = new NativeLvItem
            {
                Mask = LvifText,
                Item = index,
                SubItem = 0,
                Text = remoteText.Address,
                TextCapacity = TextCapacity
            };
            remoteItem.WriteStructure(item);
            Send(listView, LvmGetItemTextW, new IntPtr(index), remoteItem.Address);
            byte[] bytes = remoteText.ReadBytes(characterBytes);
            string value = Encoding.Unicode.GetString(bytes);
            int terminator = value.IndexOf('\0');
            return terminator >= 0 ? value[..terminator] : value;
        }

        private static NativePoint GetItemPosition(IntPtr listView, ExplorerProcessMemory process, int index)
        {
            using RemoteBuffer point = process.Allocate(Marshal.SizeOf<NativePoint>());
            IntPtr result = Send(listView, LvmGetItemPosition, new IntPtr(index), point.Address);
            if (result == IntPtr.Zero)
                throw new Win32Exception($"LVM_GETITEMPOSITION failed for index {index}.");
            return point.ReadStructure<NativePoint>();
        }

        private static void SetItemPosition(IntPtr listView, ExplorerProcessMemory process, int index, int x, int y)
        {
            using RemoteBuffer point = process.Allocate(Marshal.SizeOf<NativePoint>());
            point.WriteStructure(new NativePoint { X = x, Y = y });
            Send(listView, LvmSetItemPosition32, new IntPtr(index), point.Address);
        }

        private static IntPtr RequireDesktopListView()
        {
            IntPtr listView = DesktopIconManager.GetDesktopListView();
            return listView != IntPtr.Zero
                ? listView
                : throw new InvalidOperationException("Windows Desktop SysListView32 was not found.");
        }

        private static IntPtr Send(IntPtr handle, int message, IntPtr wParam, IntPtr lParam)
        {
            IntPtr status = SendMessageTimeout(handle, message, wParam, lParam,
                SmtoAbortIfHung, MessageTimeoutMs, out IntPtr result);
            if (status == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Desktop ListView message 0x{message:X} failed or timed out.");
            return result;
        }

        private static long GetWindowStyle(IntPtr handle) => IntPtr.Size == 8
            ? GetWindowLongPtr64(handle, GwlStyle).ToInt64()
            : GetWindowLong32(handle, GwlStyle);

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path?.Trim() ?? string.Empty;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeLvItem
        {
            public uint Mask;
            public int Item;
            public int SubItem;
            public uint State;
            public uint StateMask;
            public IntPtr Text;
            public int TextCapacity;
            public int Image;
            public IntPtr Param;
            public int Indent;
            public int GroupId;
            public uint ColumnCount;
            public IntPtr Columns;
            public IntPtr ColumnFormats;
            public int Group;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        private sealed class ExplorerProcessMemory : IDisposable
        {
            public IntPtr Handle { get; }

            public ExplorerProcessMemory(IntPtr window)
            {
                GetWindowThreadProcessId(window, out uint processId);
                if (processId == 0) throw new Win32Exception("Could not resolve the Explorer process for the Desktop ListView.");
                Handle = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite |
                    ProcessQueryLimitedInformation, false, processId);
                if (Handle == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the Explorer process for ListView messages.");
            }

            public RemoteBuffer Allocate(int size) => new(Handle, size);

            public void Dispose()
            {
                if (Handle != IntPtr.Zero) CloseHandle(Handle);
            }
        }

        private sealed class RemoteBuffer : IDisposable
        {
            private readonly IntPtr _process;
            private readonly int _size;
            public IntPtr Address { get; }

            public RemoteBuffer(IntPtr process, int size)
            {
                _process = process;
                _size = size;
                Address = VirtualAllocEx(process, IntPtr.Zero, new UIntPtr((uint)size),
                    MemCommit | MemReserve, PageReadWrite);
                if (Address == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx failed for Desktop ListView message data.");
            }

            public void WriteStructure<T>(T value) where T : struct
            {
                int size = Marshal.SizeOf<T>();
                IntPtr local = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(value, local, false);
                    byte[] bytes = new byte[size];
                    Marshal.Copy(local, bytes, 0, size);
                    WriteBytes(bytes);
                }
                finally
                {
                    Marshal.FreeHGlobal(local);
                }
            }

            public T ReadStructure<T>() where T : struct
            {
                int size = Marshal.SizeOf<T>();
                byte[] bytes = ReadBytes(size);
                IntPtr local = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.Copy(bytes, 0, local, size);
                    return Marshal.PtrToStructure<T>(local);
                }
                finally
                {
                    Marshal.FreeHGlobal(local);
                }
            }

            public byte[] ReadBytes(int count)
            {
                if (count < 0 || count > _size) throw new ArgumentOutOfRangeException(nameof(count));
                byte[] bytes = new byte[count];
                if (!ReadProcessMemory(_process, Address, bytes, count, out IntPtr read) || read.ToInt64() != count)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "ReadProcessMemory failed for Desktop ListView message data.");
                return bytes;
            }

            private void WriteBytes(byte[] bytes)
            {
                if (bytes.Length > _size) throw new ArgumentOutOfRangeException(nameof(bytes));
                if (!WriteProcessMemory(_process, Address, bytes, bytes.Length, out IntPtr written) ||
                    written.ToInt64() != bytes.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory failed for Desktop ListView message data.");
            }

            public void Dispose()
            {
                if (Address != IntPtr.Zero) VirtualFreeEx(_process, Address, UIntPtr.Zero, MemRelease);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rectangle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int index);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size,
            uint allocationType, uint protection);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint freeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr baseAddress,
            [Out] byte[] buffer, int size, out IntPtr bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr process, IntPtr baseAddress,
            byte[] buffer, int size, out IntPtr bytesWritten);
    }
}
