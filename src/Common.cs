using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace StalcraftResolutionSwitcher
{
    internal static class AppIdentity
    {
        public const string MonitorMutexName = "Local\\StalcraftResolutionMonitor.Singleton";
        public const string SettingsMutexName = "Local\\StalcraftResolutionSettings.Singleton";
        public const string ReloadEventName = "Local\\StalcraftResolutionMonitor.Reload";
        public const string StopEventName = "Local\\StalcraftResolutionMonitor.Stop";
        public const string StartupValueName = "StalcraftResolutionMonitor";
        public const string MonitorFileName = "StalcraftResolutionMonitor.exe";
    }

    internal sealed class AppSettings
    {
        public const string DefaultProcessNames = "STALZONE;stalzone;Stalzone";

        public int GameWidth = 1280;
        public int GameHeight = 720;
        public int RestoreWidth = 1920;
        public int RestoreHeight = 1080;
        public string ProcessNames = DefaultProcessNames;
        public int PollIntervalMs = 2000;
    }

    internal static class SettingsStore
    {
        private static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StalcraftResolutionSwitcher");

        public static readonly string FilePath = Path.Combine(DirectoryPath, "settings.ini");
        public static bool Exists
        {
            get { return File.Exists(FilePath); }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            if (!File.Exists(FilePath))
            {
                return settings;
            }

            try
            {
                foreach (string rawLine in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int separator = rawLine.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = rawLine.Substring(0, separator).Trim();
                    string value = rawLine.Substring(separator + 1).Trim();
                    int number;

                    switch (key.ToLowerInvariant())
                    {
                        case "gamewidth":
                            if (int.TryParse(value, out number))
                            {
                                settings.GameWidth = number;
                            }
                            break;
                        case "gameheight":
                            if (int.TryParse(value, out number))
                            {
                                settings.GameHeight = number;
                            }
                            break;
                        case "restorewidth":
                            if (int.TryParse(value, out number))
                            {
                                settings.RestoreWidth = number;
                            }
                            break;
                        case "restoreheight":
                            if (int.TryParse(value, out number))
                            {
                                settings.RestoreHeight = number;
                            }
                            break;
                        case "processnames":
                            settings.ProcessNames = value;
                            break;
                        case "pollintervalms":
                            if (int.TryParse(value, out number))
                            {
                                settings.PollIntervalMs = Math.Max(1000, Math.Min(10000, number));
                            }
                            break;
                    }
                }
            }
            catch
            {
                return new AppSettings();
            }

            if (settings.ProcessNames.Equals("stalcraftw;stalcraft;stalzone", StringComparison.OrdinalIgnoreCase))
            {
                settings.ProcessNames = AppSettings.DefaultProcessNames;
            }

            return settings;
        }

        public static void Save(AppSettings settings)
        {
            Directory.CreateDirectory(DirectoryPath);
            string[] lines =
            {
                "GameWidth=" + settings.GameWidth,
                "GameHeight=" + settings.GameHeight,
                "RestoreWidth=" + settings.RestoreWidth,
                "RestoreHeight=" + settings.RestoreHeight,
                "ProcessNames=" + settings.ProcessNames,
                "PollIntervalMs=" + settings.PollIntervalMs
            };
            File.WriteAllLines(FilePath, lines, new UTF8Encoding(false));
        }
    }

    internal static class StartupManager
    {
        private const string RegistryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        public static bool IsEnabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, false))
                {
                    return key != null && key.GetValue(AppIdentity.StartupValueName) != null;
                }
            }
        }

        public static void SetEnabled(bool enabled, string monitorPath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("Не удалось открыть автозагрузку.");
                }

                if (enabled)
                {
                    if (!File.Exists(monitorPath))
                    {
                        throw new FileNotFoundException("Не найден монитор.", monitorPath);
                    }

                    key.SetValue(
                        AppIdentity.StartupValueName,
                        "\"" + monitorPath + "\" --autostart",
                        RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(AppIdentity.StartupValueName, false);
                }
            }
        }
    }

    internal static class MonitorManager
    {
        public static string MonitorPath
        {
            get
            {
                string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return Path.Combine(directory, AppIdentity.MonitorFileName);
            }
        }

        public static bool IsRunning
        {
            get
            {
                try
                {
                    using (Mutex mutex = Mutex.OpenExisting(AppIdentity.MonitorMutexName)) return true;
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    return true;
                }
            }
        }

        public static bool EnsureStarted(out string error)
        {
            if (IsRunning)
            {
                error = null;
                return true;
            }

            if (!File.Exists(MonitorPath))
            {
                error = "Не найден " + AppIdentity.MonitorFileName + ".";
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = MonitorPath,
                    WorkingDirectory = Path.GetDirectoryName(MonitorPath),
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(startInfo);

                for (int i = 0; i < 20; i++)
                {
                    if (IsRunning)
                    {
                        error = null;
                        return true;
                    }
                    Thread.Sleep(100);
                }

                error = "Монитор не запустился.";
                return false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static void SignalReload()
        {
            SignalEvent(AppIdentity.ReloadEventName);
        }

        public static bool Stop(out string error)
        {
            if (!IsRunning)
            {
                error = null;
                return true;
            }

            if (!SignalEvent(AppIdentity.StopEventName))
            {
                error = "Монитор не принял команду.";
                return false;
            }

            for (int i = 0; i < 30; i++)
            {
                if (!IsRunning)
                {
                    error = null;
                    return true;
                }
                Thread.Sleep(100);
            }

            error = "Монитор не завершился за 3 секунды.";
            return false;
        }

        private static bool SignalEvent(string name)
        {
            try
            {
                using (var signal = EventWaitHandle.OpenExisting(name))
                {
                    return signal.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    internal static class RecoveryMarker
    {
        private static readonly string MarkerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StalcraftResolutionSwitcher",
            "resolution-active.flag");

        public static bool Exists
        {
            get { return File.Exists(MarkerPath); }
        }

        public static void Create()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath));
            File.WriteAllText(MarkerPath, DateTime.UtcNow.ToString("O"), Encoding.ASCII);
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(MarkerPath))
                {
                    File.Delete(MarkerPath);
                }
            }
            catch
            {
            }
        }
    }

    internal static class MonitorLog
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StalcraftResolutionSwitcher",
            "monitor.log");

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine;
                File.AppendAllText(LogPath, line, new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }

    internal static class ProcessDetector
    {
        private const uint SnapshotProcesses = 0x00000002;
        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        public static bool IsAnyRunning(string configuredNames)
        {
            var wanted = new HashSet<string>(ParseNames(configuredNames), StringComparer.OrdinalIgnoreCase);
            if (wanted.Count == 0)
            {
                return false;
            }

            IntPtr snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
            if (snapshot == InvalidHandleValue)
            {
                return false;
            }

            try
            {
                var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry32)) };
                if (!Process32First(snapshot, ref entry))
                {
                    return false;
                }

                do
                {
                    if (wanted.Contains(NormalizeName(entry.ExecutableFile)))
                    {
                        return true;
                    }
                }
                while (Process32Next(snapshot, ref entry));
                return false;
            }
            finally
            {
                CloseHandle(snapshot);
            }
        }

        public static List<string> ParseNames(string configuredNames)
        {
            return (configuredNames ?? string.Empty)
                .Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeName)
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeName(string name)
        {
            string result = (name ?? string.Empty).Trim();
            return result.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? result.Substring(0, result.Length - 4)
                : result;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct ProcessEntry32
        {
            public uint Size;
            public uint Usage;
            public uint ProcessId;
            public IntPtr DefaultHeapId;
            public uint ModuleId;
            public uint Threads;
            public uint ParentProcessId;
            public int PriorityClassBase;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string ExecutableFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }

    internal sealed class ResolutionChoice
    {
        public int Width { get; private set; }
        public int Height { get; private set; }

        public ResolutionChoice(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public override string ToString()
        {
            return Width + "x" + Height;
        }
    }

    internal static class ResolutionManager
    {
        private const int EnumCurrentSettings = -1;
        private const int ChangeSuccessful = 0;
        private const uint TestOnly = 0x00000002;

        public static List<ResolutionChoice> GetResolutionChoices()
        {
            return EnumerateModes()
                .Where(m => m.Width >= 800 && m.Height >= 600)
                .GroupBy(m => new { m.Width, m.Height })
                .Select(group => new ResolutionChoice(group.Key.Width, group.Key.Height))
                .OrderBy(choice => choice.Width)
                .ThenBy(choice => choice.Height)
                .ToList();
        }

        public static bool IsAvailable(int width, int height)
        {
            return EnumerateModes().Any(mode => mode.Width == width && mode.Height == height);
        }

        public static bool Apply(int width, int height, out string error)
        {
            DisplayMode target;
            if (!TryChooseMode(width, height, out target, out error))
            {
                return false;
            }

            DevMode nativeMode = target.Native;
            int testResult = ChangeDisplaySettingsEx(null, ref nativeMode, IntPtr.Zero, TestOnly, IntPtr.Zero);
            if (testResult != ChangeSuccessful)
            {
                error = DescribeResult(testResult);
                return false;
            }

            int applyResult = ChangeDisplaySettingsEx(null, ref nativeMode, IntPtr.Zero, 0, IntPtr.Zero);
            if (applyResult != ChangeSuccessful)
            {
                error = DescribeResult(applyResult);
                return false;
            }

            error = null;
            return true;
        }

        public static bool Test(int width, int height, out string error)
        {
            DisplayMode target;
            if (!TryChooseMode(width, height, out target, out error))
            {
                return false;
            }

            DevMode nativeMode = target.Native;
            int result = ChangeDisplaySettingsEx(null, ref nativeMode, IntPtr.Zero, TestOnly, IntPtr.Zero);
            error = result == ChangeSuccessful ? null : DescribeResult(result);
            return result == ChangeSuccessful;
        }

        public static bool TryGetCurrentMode(out DisplayMode mode)
        {
            DevMode native = CreateDevMode();
            if (!EnumDisplaySettings(null, EnumCurrentSettings, ref native))
            {
                mode = null;
                return false;
            }

            mode = new DisplayMode(native);
            return true;
        }

        private static bool TryChooseMode(int width, int height, out DisplayMode target, out string error)
        {
            DisplayMode current;
            if (!TryGetCurrentMode(out current))
            {
                target = null;
                error = "Windows не сообщил текущее разрешение.";
                return false;
            }

            target = EnumerateModes()
                .Where(mode => mode.Width == width && mode.Height == height)
                .OrderBy(mode => Math.Abs(mode.Frequency - current.Frequency))
                .ThenByDescending(mode => mode.Frequency)
                .FirstOrDefault();

            error = target == null ? "Режим " + width + "x" + height + " не поддерживается основным монитором." : null;
            return target != null;
        }

        private static List<DisplayMode> EnumerateModes()
        {
            var modes = new List<DisplayMode>();
            for (int index = 0; ; index++)
            {
                DevMode native = CreateDevMode();
                if (!EnumDisplaySettings(null, index, ref native))
                {
                    break;
                }

                if (native.BitsPerPel >= 32)
                {
                    modes.Add(new DisplayMode(native));
                }
            }
            return modes;
        }

        private static DevMode CreateDevMode()
        {
            var mode = new DevMode();
            mode.Size = (short)Marshal.SizeOf(typeof(DevMode));
            return mode;
        }

        private static string DescribeResult(int code)
        {
            switch (code)
            {
                case 1: return "Windows требует перезапуск.";
                case -1: return "драйвер дисплея отклонил изменение.";
                case -2: return "режим не поддерживается.";
                case -3: return "не удалось записать параметры дисплея.";
                case -4: return "переданы неверные параметры.";
                case -5: return "неверные флаги смены режима.";
                case -6: return "режим не поддерживается при нескольких дисплеях.";
                default: return "код ошибки Windows: " + code + ".";
            }
        }

        internal sealed class DisplayMode
        {
            public readonly int Width;
            public readonly int Height;
            public readonly int Frequency;
            public readonly DevMode Native;
            public DisplayMode(DevMode native)
            {
                Native = native;
                Width = native.PelsWidth;
                Height = native.PelsHeight;
                Frequency = native.DisplayFrequency;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            public short SpecVersion;
            public short DriverVersion;
            public short Size;
            public short DriverExtra;
            public int Fields;
            public int PositionX;
            public int PositionY;
            public int DisplayOrientation;
            public int DisplayFixedOutput;
            public short Color;
            public short Duplex;
            public short YResolution;
            public short TTOption;
            public short Collate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string FormName;
            public short LogPixels;
            public int BitsPerPel;
            public int PelsWidth;
            public int PelsHeight;
            public int DisplayFlags;
            public int DisplayFrequency;
            public int ICMMethod;
            public int ICMIntent;
            public int MediaType;
            public int DitherType;
            public int Reserved1;
            public int Reserved2;
            public int PanningWidth;
            public int PanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNumber, ref DevMode mode);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int ChangeDisplaySettingsEx(
            string deviceName,
            ref DevMode mode,
            IntPtr windowHandle,
            uint flags,
            IntPtr parameter);
    }
}
