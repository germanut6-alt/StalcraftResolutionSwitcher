using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;

[assembly: AssemblyTitle("STALCRAFT Resolution Settings")]
[assembly: AssemblyDescription("Terminal settings for STALCRAFT Resolution Monitor")]
[assembly: AssemblyCompany("STALCRAFT Resolution Switcher contributors")]
[assembly: AssemblyProduct("STALCRAFT Resolution Switcher")]
[assembly: AssemblyCopyright("Copyright (c) 2026 STALCRAFT Resolution Switcher contributors")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

namespace StalcraftResolutionSwitcher
{
    internal static class SettingsProgram
    {
        private static int Main()
        {
            bool createdNew;
            using (var singleton = new Mutex(true, AppIdentity.SettingsMutexName, out createdNew))
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.InputEncoding = Encoding.UTF8;
                Console.Title = "STALCRAFT Resolution Settings";

                if (!createdNew)
                {
                    Console.WriteLine("Окно настроек уже открыто.");
                    Console.WriteLine("Нажмите любую клавишу для выхода...");
                    Console.ReadKey(true);
                    return 0;
                }

                var console = new SettingsConsole();
                return console.Run();
            }
        }
    }

    internal sealed class SettingsConsole
    {
        private AppSettings settings;
        private string lastEvent = "Готово.";

        public int Run()
        {
            settings = SettingsStore.Load();
            if (!SettingsStore.Exists)
            {
                RunFirstSetup();
            }

            string startError;
            if (!MonitorManager.EnsureStarted(out startError))
            {
                lastEvent = "Монитор не запущен: " + startError;
            }
            else
            {
                MonitorManager.SignalReload();
            }

            while (true)
            {
                Render();
                ConsoleKey key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.D0
                    || key == ConsoleKey.NumPad0
                    || key == ConsoleKey.Q
                    || key == ConsoleKey.Escape)
                {
                    return 0;
                }

                switch (key)
                {
                    case ConsoleKey.D1:
                    case ConsoleKey.NumPad1:
                        ChangeGameResolution();
                        break;
                    case ConsoleKey.D2:
                    case ConsoleKey.NumPad2:
                        ChangeRestoreResolution();
                        break;
                    case ConsoleKey.D3:
                    case ConsoleKey.NumPad3:
                        ChangeProcessNames();
                        break;
                    case ConsoleKey.D4:
                    case ConsoleKey.NumPad4:
                        ChangePollInterval();
                        break;
                    case ConsoleKey.D5:
                    case ConsoleKey.NumPad5:
                        ToggleAutoStart();
                        break;
                    case ConsoleKey.D6:
                    case ConsoleKey.NumPad6:
                        RestoreDesktopNow();
                        break;
                    case ConsoleKey.D7:
                    case ConsoleKey.NumPad7:
                        ToggleMonitor();
                        break;
                    case ConsoleKey.T:
                        TestConfiguredModes();
                        break;
                    case ConsoleKey.R:
                        lastEvent = ProcessDetector.IsAnyRunning(settings.ProcessNames)
                            ? "Игра обнаружена."
                            : "Игра не запущена.";
                        break;
                }
            }
        }

        private void RunFirstSetup()
        {
            SafeClear();
            WriteHeader();
            Console.WriteLine("Первый запуск. Enter оставляет текущее значение.");

            ResolutionChoice game = PromptResolution(
                "Разрешение во время игры",
                settings.GameWidth,
                settings.GameHeight);
            settings.GameWidth = game.Width;
            settings.GameHeight = game.Height;

            ResolutionChoice restore = PromptResolution(
                "Разрешение после выхода",
                settings.RestoreWidth,
                settings.RestoreHeight);
            settings.RestoreWidth = restore.Width;
            settings.RestoreHeight = restore.Height;

            Console.WriteLine();
            Console.Write("Добавить монитор в автозагрузку? [Y/n]: ");
            string answer = (Console.ReadLine() ?? string.Empty).Trim();
            bool enableStartup = answer.Length == 0
                || answer.Equals("y", StringComparison.OrdinalIgnoreCase)
                || answer.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || answer.Equals("д", StringComparison.OrdinalIgnoreCase);

            try
            {
                StartupManager.SetEnabled(enableStartup, MonitorManager.MonitorPath);
                lastEvent = enableStartup ? "Автозагрузка включена." : "Автозагрузка выключена.";
            }
            catch (Exception exception)
            {
                lastEvent = "Ошибка автозагрузки: " + exception.Message;
            }

            SettingsStore.Save(settings);
        }

        private void ChangeGameResolution()
        {
            SafeClear();
            WriteHeader();
            ResolutionChoice choice = PromptResolution(
                "Новое разрешение во время игры",
                settings.GameWidth,
                settings.GameHeight);
            settings.GameWidth = choice.Width;
            settings.GameHeight = choice.Height;
            SaveAndNotify();
            lastEvent = "Разрешение игры сохранено: " + choice + ".";
        }

        private void ChangeRestoreResolution()
        {
            SafeClear();
            WriteHeader();
            ResolutionChoice choice = PromptResolution(
                "Новое разрешение после выхода",
                settings.RestoreWidth,
                settings.RestoreHeight);
            settings.RestoreWidth = choice.Width;
            settings.RestoreHeight = choice.Height;
            SaveAndNotify();
            lastEvent = "Разрешение возврата сохранено: " + choice + ".";
        }

        private void ChangeProcessNames()
        {
            SafeClear();
            WriteHeader();
            Console.WriteLine("Текущие процессы: " + settings.ProcessNames);
            Console.WriteLine("Укажите имена без .exe через точку с запятой.");
            Console.Write("Новое значение (Enter/0 — назад): ");
            string input = (Console.ReadLine() ?? string.Empty).Trim();
            if (!IsBackCommand(input) && ProcessDetector.ParseNames(input).Count > 0)
            {
                settings.ProcessNames = input;
                SaveAndNotify();
                lastEvent = "Список процессов обновлён.";
            }
            else
            {
                lastEvent = "Список процессов не изменён.";
            }
        }

        private void ChangePollInterval()
        {
            SafeClear();
            WriteHeader();
            Console.Write("Интервал 1–10 секунд, 0 — назад (сейчас " + settings.PollIntervalMs / 1000 + "): ");
            string input = (Console.ReadLine() ?? string.Empty).Trim();
            if (IsBackCommand(input))
            {
                lastEvent = "Без изменений.";
                return;
            }

            int seconds;
            if (int.TryParse(input, out seconds) && seconds >= 1 && seconds <= 10)
            {
                settings.PollIntervalMs = seconds * 1000;
                SaveAndNotify();
                lastEvent = "Интервал изменён на " + seconds + " сек.";
            }
            else
            {
                lastEvent = "Интервал не изменён.";
            }
        }

        private void ToggleAutoStart()
        {
            bool enable = !StartupManager.IsEnabled;
            try
            {
                StartupManager.SetEnabled(enable, MonitorManager.MonitorPath);
                SettingsStore.Save(settings);

                if (enable)
                {
                    string startError;
                    bool started = MonitorManager.EnsureStarted(out startError);
                    lastEvent = started ? "Автозагрузка включена." : "Монитор не запущен: " + startError;
                }
                else
                {
                    lastEvent = "Автозагрузка выключена.";
                }
            }
            catch (Exception exception)
            {
                lastEvent = "Не удалось изменить автозагрузку: " + exception.Message;
            }
        }

        private void ToggleMonitor()
        {
            string error;
            if (MonitorManager.IsRunning)
            {
                lastEvent = MonitorManager.Stop(out error)
                    ? "Монитор остановлен."
                    : "Ошибка остановки: " + error;
            }
            else
            {
                lastEvent = MonitorManager.EnsureStarted(out error)
                    ? "Монитор запущен."
                    : "Ошибка запуска: " + error;
            }
        }

        private void RestoreDesktopNow()
        {
            string error;
            if (ResolutionManager.Apply(settings.RestoreWidth, settings.RestoreHeight, out error))
            {
                RecoveryMarker.Delete();
                lastEvent = "Установлено "
                    + FormatResolution(settings.RestoreWidth, settings.RestoreHeight)
                    + ".";
            }
            else
            {
                lastEvent = "Не удалось вернуть разрешение: " + error;
            }
        }

        private void TestConfiguredModes()
        {
            string gameError;
            string restoreError;
            bool gameOk = ResolutionManager.Test(settings.GameWidth, settings.GameHeight, out gameError);
            bool restoreOk = ResolutionManager.Test(settings.RestoreWidth, settings.RestoreHeight, out restoreError);
            lastEvent = gameOk && restoreOk
                ? "Оба режима поддерживаются."
                : "Ошибка: " + (!gameOk ? gameError : restoreError);
        }

        private void SaveAndNotify()
        {
            SettingsStore.Save(settings);
            MonitorManager.SignalReload();
        }

        private ResolutionChoice PromptResolution(string title, int currentWidth, int currentHeight)
        {
            List<ResolutionChoice> choices = ResolutionManager.GetResolutionChoices();
            Console.WriteLine();
            Console.WriteLine(title + " (сейчас " + FormatResolution(currentWidth, currentHeight) + ")");
            PrintChoices(choices);

            while (true)
            {
                Console.Write("Введите 1280x720 (Enter/0 — назад): ");
                string input = (Console.ReadLine() ?? string.Empty).Trim();
                if (IsBackCommand(input))
                {
                    return new ResolutionChoice(currentWidth, currentHeight);
                }

                int width;
                int height;
                if (TryParseResolution(input, out width, out height) && ResolutionManager.IsAvailable(width, height))
                {
                    return new ResolutionChoice(width, height);
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Режим не поддерживается.");
                Console.ResetColor();
            }
        }

        private void Render()
        {
            settings = SettingsStore.Load();
            SafeClear();
            WriteHeader();

            Console.WriteLine(
                "  Разрешение в игре:       " + FormatResolution(settings.GameWidth, settings.GameHeight));
            Console.WriteLine(
                "  Вернуть после выхода:    " + FormatResolution(settings.RestoreWidth, settings.RestoreHeight));
            Console.WriteLine("  Процессы:                " + settings.ProcessNames);
            Console.WriteLine("  Проверка процессов:      раз в " + settings.PollIntervalMs / 1000 + " сек.");
            Console.WriteLine("  Автозагрузка:            " + (StartupManager.IsEnabled ? "включена" : "выключена"));
            Console.WriteLine("  Монитор:                 " + (MonitorManager.IsRunning ? "работает" : "остановлен"));
            Console.WriteLine(
                "  Игра сейчас:             "
                + (ProcessDetector.IsAnyRunning(settings.ProcessNames) ? "обнаружена" : "не запущена"));
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(lastEvent);
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("[1] Разрешение в игре       [2] Разрешение возврата");
            Console.WriteLine("[3] Имена процессов         [4] Интервал проверки");
            Console.WriteLine("[5] Вкл./выкл. автозагрузку [6] Вернуть разрешение сейчас");
            Console.WriteLine("[7] Запустить/остановить монитор");
            Console.WriteLine("[T] Безопасно проверить режимы");
            Console.WriteLine("[R] Обновить состояние      [0] Выход");
            Console.WriteLine();
            Console.Write("Выберите действие: ");
        }

        private static void WriteHeader()
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("============================================================");
            Console.WriteLine("          STALCRAFT RESOLUTION SETTINGS  v1.0.1");
            Console.WriteLine("============================================================");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Это окно можно закрыть — монитор останется работать.");
            Console.WriteLine();
        }

        private static void PrintChoices(IEnumerable<ResolutionChoice> choices)
        {
            int lineLength = 0;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("Доступно: ");
            foreach (ResolutionChoice choice in choices)
            {
                string item = choice + "  ";
                if (lineLength + item.Length > 66)
                {
                    Console.WriteLine();
                    Console.Write("           ");
                    lineLength = 0;
                }
                Console.Write(item);
                lineLength += item.Length;
            }
            Console.WriteLine();
            Console.ResetColor();
        }

        private static bool TryParseResolution(string input, out int width, out int height)
        {
            width = 0;
            height = 0;
            string normalized = input.ToLowerInvariant().Replace('×', 'x').Replace('*', 'x').Replace(" ", "");
            string[] parts = normalized.Split('x');
            return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height);
        }

        private static bool IsBackCommand(string input)
        {
            return string.IsNullOrWhiteSpace(input)
                || input.Equals("0", StringComparison.OrdinalIgnoreCase)
                || input.Equals("q", StringComparison.OrdinalIgnoreCase);
        }

        private static void SafeClear()
        {
            try
            {
                Console.Clear();
            }
            catch
            {
                Console.WriteLine();
            }
        }

        private static string FormatResolution(int width, int height)
        {
            return width + " × " + height;
        }
    }
}
