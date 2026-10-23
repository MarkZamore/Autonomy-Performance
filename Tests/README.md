# Актуальные проверки

Запускайте из корня репозитория после сборки через Windows PowerShell 5.1. Для WPF-проверок нужен параметр `-STA`. Локальные EXE, изображения и отчёты создаются в исключённой из Git папке `Tests/Artifacts`.

## Регрессия без применения системных параметров

- `VerifyAppCategories.ps1` — оформление категорий, действия программ и экспорт. Установка программ подставляется для проверки интерфейса; реальные возможности проверяет `VerifyProgramAvailability.ps1`.
- `VerifyPreviewActions.ps1` — строки предпросмотра, удаление, клавиатура, прокрутка и пороги перетаскивания, включая дробные DIP.
- `VerifyArmouryMode.ps1`, `VerifyArmouryCrateControl.ps1` — режимы Armoury Crate, доступность, ручной профиль и экспорт без выполнения режимов.
- `VerifyLatestSettings.ps1`, `VerifySelectionControls.ps1`, `VerifyBrightnessEditor.ps1`, `VerifyPresets.ps1` — текущие настройки и их связь с конфигурацией.
- `VerifyAdaptivePresets.ps1`, `VerifyPresetDisplayCapabilities.ps1`, `VerifyMultiDisplayConfig.ps1`, `VerifyMultiDisplayRunner.ps1` — синтетические возможности, совместимость и порядок независимых действий.
- `VerifyExport.ps1`, `VerifyMinimalExport.ps1`, `VerifySaveState.ps1`, `VerifyNativeDialogs.ps1` — PE-ресурсы, сохранение и параметры штатных диалогов.
- `VerifyBrokerPreferences.ps1`, `VerifyTaskbarPreferences.ps1`, `VerifyNightLightPreferences.ps1`, `VerifyEnergySaverPreferences.ps1` — изолированные адаптеры действующих предпочтений Windows.

Пример:

```powershell
powershell.exe -NoProfile -STA -File .\Tests\VerifyAppCategories.ps1
powershell.exe -NoProfile -STA -File .\Tests\VerifyPreviewActions.ps1
powershell.exe -NoProfile -STA -File .\Tests\VerifyArmouryMode.ps1
```

Остальные специализированные проверки отображения, анимации, меню «Пуск» и переносимости сохранены. Скрипты реального применения параметров (`VerifyFunctional`, `VerifyAdditionalFunctional`, `VerifyDisplayFeatures`, `VerifyNativeModes`, `VerifyMultiDisplayNative`, проверки Wallpaper Engine) выполняются отдельно: они не входят в безопасную автоматическую регрессию и могут менять состояние компьютера.

## Удалённые проверки

| Файлы | Причина и актуальная замена |
|---|---|
| `VerifyNewSettings.ps1` | Дублировал единственный вызов `VerifyLatestSettings.ps1`; документация теперь ссылается прямо на него. |
| `VerifyRevision8.ps1` | Проверял прежний CheckBox яркости и старые шаблоны. Текущие контролы проверяют `VerifyBrightnessEditor`, `VerifySelectionControls`, `VerifyPresets`. |
| `VerifyDisplay.ps1` | Опирался на прежние глобальные поля дисплея. Адресные настройки проверяют `VerifyMultiDisplayConfig`, `VerifyDisplayHotplug`, `VerifyRefreshSelection`. |
| `VerifyDialogs.ps1`, `DialogCancellation.cs`, `DialogSave.cs` | Старый набор диалогов и устаревшие ожидания подсказок. Действующие диалоги и сохранение покрывают `VerifyNativeDialogs`, `VerifySaveState`, `VerifyStartMenuOption`. |
| `VerifyTheme.ps1` | Использовал прямое восстановление реестра и удалённый внутренний метод уведомления. Текущий брокер и оформление проверяют `VerifyBrokerPreferences`, `VerifyWindowsTheme`. |

Исторические упоминания удалённых файлов в `RESULTS.md` сохранены как журнал прошлых выпусков.
