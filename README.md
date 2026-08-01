# STALCRAFT Resolution Switcher

Меняет разрешение основного монитора при запуске STALCRAFT и возвращает его после выхода из игры.

## Скачать

Откройте **Releases**, скачайте ZIP и распакуйте оба EXE в одну папку. Компилировать проект не нужно.

## Файлы

- `StalcraftResolutionMonitor.exe` — работает в фоне и следит за игрой.
- `StalcraftResolutionSettings.exe` — открывает настройки в терминале.

Монитор запускается только в одном экземпляре. В автозагрузку добавляется одна запись без дубликатов.

## Использование

1. Запустите `StalcraftResolutionSettings.exe`.
2. Выберите разрешение для игры и разрешение возврата.
3. Включите автозагрузку пунктом `5`.
4. Закройте настройки. Монитор продолжит работать в фоне.

По умолчанию отслеживаются `stalcraftw.exe`, `stalcraft.exe` и `stalzone.exe`.

Настройки находятся в `%LOCALAPPDATA%\StalcraftResolutionSwitcher\settings.ini`.

## Сборка

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Готовые файлы появятся в `dist`.

## Лицензия

MIT
