using System;
using System.Reflection;
using System.Threading;

[assembly: AssemblyTitle("STALCRAFT Resolution Monitor")]
[assembly: AssemblyDescription("Single-instance background resolution monitor for STALCRAFT: X / STALZONE")]
[assembly: AssemblyCompany("STALCRAFT Resolution Switcher contributors")]
[assembly: AssemblyProduct("STALCRAFT Resolution Switcher")]
[assembly: AssemblyCopyright("Copyright (c) 2026 STALCRAFT Resolution Switcher contributors")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace StalcraftResolutionSwitcher
{
    internal static class MonitorProgram
    {
        private static int Main()
        {
            bool createdNew;
            using (var singleton = new Mutex(true, AppIdentity.MonitorMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return 0;
                }

                try
                {
                    using (var monitor = new ResolutionMonitor())
                    {
                        monitor.Run();
                    }
                    return 0;
                }
                catch (Exception exception)
                {
                    MonitorLog.Write("Критическая ошибка: " + exception);
                    return 2;
                }
            }
        }
    }

    internal sealed class ResolutionMonitor : IDisposable
    {
        private readonly object restoreLock = new object();
        private readonly EventWaitHandle reloadEvent;
        private readonly EventWaitHandle stopEvent;
        private AppSettings settings;
        private bool gameWasRunning;
        private bool sessionNeedsRestore;
        private bool disposed;

        public ResolutionMonitor()
        {
            reloadEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppIdentity.ReloadEventName);
            stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppIdentity.StopEventName);
            settings = SettingsStore.Load();
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        public void Run()
        {
            MonitorLog.Write("Запуск");
            RecoverIfNeeded();
            CheckGameState();

            WaitHandle[] signals = { reloadEvent, stopEvent };
            while (true)
            {
                int signal = WaitHandle.WaitAny(signals, settings.PollIntervalMs);
                if (signal == 1)
                {
                    break;
                }

                if (signal == 0)
                {
                    settings = SettingsStore.Load();
                    gameWasRunning = false;
                    MonitorLog.Write("Настройки обновлены");
                }

                CheckGameState();
            }

            MonitorLog.Write("Остановка");
            RestoreOnShutdown();
        }

        private void CheckGameState()
        {
            bool isRunning = ProcessDetector.IsAnyRunning(settings.ProcessNames);

            if (isRunning && !gameWasRunning)
            {
                gameWasRunning = true;
                string error;
                if (ResolutionManager.Apply(settings.GameWidth, settings.GameHeight, out error))
                {
                    sessionNeedsRestore = true;
                    RecoveryMarker.Create();
                    MonitorLog.Write("Игра: " + settings.GameWidth + "x" + settings.GameHeight);
                }
                else
                {
                    MonitorLog.Write("Ошибка: " + error);
                }
            }
            else if (!isRunning && gameWasRunning)
            {
                gameWasRunning = false;
                RestoreDesktop("Выход из игры");
            }
            else if (!isRunning && sessionNeedsRestore)
            {
                RestoreDesktop("Повтор");
            }
        }

        private void RecoverIfNeeded()
        {
            if (!RecoveryMarker.Exists || ProcessDetector.IsAnyRunning(settings.ProcessNames))
            {
                return;
            }
            sessionNeedsRestore = true;
            RestoreDesktop("Восстановление");
        }

        private void RestoreDesktop(string reason)
        {
            lock (restoreLock)
            {
                string error;
                if (ResolutionManager.Apply(settings.RestoreWidth, settings.RestoreHeight, out error))
                {
                    sessionNeedsRestore = false;
                    RecoveryMarker.Delete();
                    MonitorLog.Write(reason + ": " + settings.RestoreWidth + "x" + settings.RestoreHeight);
                }
                else
                {
                    sessionNeedsRestore = true;
                    MonitorLog.Write(reason + ": " + error);
                }
            }
        }

        private void RestoreOnShutdown()
        {
            lock (restoreLock)
            {
                if (!sessionNeedsRestore)
                {
                    return;
                }
                string error;
                if (ResolutionManager.Apply(settings.RestoreWidth, settings.RestoreHeight, out error))
                {
                    sessionNeedsRestore = false;
                    RecoveryMarker.Delete();
                    MonitorLog.Write("Разрешение восстановлено");
                }
                else
                {
                    MonitorLog.Write("Ошибка восстановления: " + error);
                }
            }
        }

        private void OnProcessExit(object sender, EventArgs eventArgs)
        {
            RestoreOnShutdown();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            RestoreOnShutdown();
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            reloadEvent.Dispose();
            stopEvent.Dispose();
        }
    }
}
