# NOTES.md

## Архитектура системы правил ГОСТ, конструктора и привязок

### 1. Хранение правил ГОСТ
- Пользовательские правила сериализуются в JSON-файл `custom_rules.json` через сервис `CustomRulesService`.
- Путь по умолчанию: `Path.Combine(FileSystem.AppDataDirectory, "custom_rules.json")` (с fallback во временную папку `Path.GetTempPath()`).
- В JSON хранятся объекты `CustomRuleDto`.

### 2. Выбор и автоподбор активного правила
- На вкладке «Проверка» (`CheckPage`) отображается `ChipBar RuleChips`.
- `_allRules = CustomRulesService.Instance.GetAllRules().ToList()`.
- Выбранный индекс чипа `RuleChips.SelectedIndex` определяет активное правило:
  `CurrentRules => (i >= 0 && i < _allRules.Count) ? _allRules[i] : RuleSets.MasterRule`.
- При выборе файла вызывается `CustomRulesService.Instance.MatchRuleSetForTemplate(fileName)`.

### 3. Инварианты MasterRule и предотвращение дубликатов
- Главное правило системы: `RuleSets.MasterRule` («Основной ГОСТ», Id = `RuleSets.MasterRuleId = "master-rule"`).
- Правка встроенного правила (`MasterRule`) модифицирует базовую конфигурацию по месту и сохраняет Id `master-rule`. Дубликаты и суффиксы «(копия)» не создаются.
- Удаление `MasterRule` запрещено (`DeleteRuleAsync` возвращает `false`). При попытке удаления или сброса активным правилом становится `MasterRule`.
- Создание нового правила через чип `+` или фабрику генерирует уникальный Id и дефолтные параметры ГОСТ.

### 4. Визуальный конструктор правил (`Views/RuleEditorPage.xaml`)
- Предоставляет полный интерфейс для настройки шрифтов, кегля, полей страницы, интервалов, нумерации, а также экспертные панели для подписей рисунков, таблиц и листингов кода.
- Поддерживает изолированный предпросмотр эталонного листа А4 («Было / Стало») с адаптацией ширины под экран мобильного устройства.
- На Android реализован компактный переключатель вкладок «Параметры» / «Лист А4» без зависания вложенного скролла.

### 5. Скомпилированные привязки и команды ViewModel
- Устранена первопричина предупреждений `XC0025` и `XC0009` в `Views/CheckPage.xaml`:
  - Использованы прямые скомпилированные привязки с объявлением `x:DataType="views:CheckGroupVm"` и `x:DataType="models:CheckIssue"`.
  - Команды взаимодействия с карточками (`ToggleExpandCommand`, `RejectGroupCommand`, `AcceptGroupCommand`, `RejectItemCommand`, `AcceptItemCommand`) инкапсулированы во ViewModel с фабричным конструктором `CheckGroupVm.Create(...)`.
  - Видимость карточки выбранного файла (`FileSelectedCard`) управляется синхронно в `UpdateUi()`.
- Все UI-вызовы защищены проверками `MainThread.IsMainThread`.

### 6. Статус верификации
- Набор автотестов: **370 / 370 unit-тестов xUnit пройдены (100% зелёные) + 13 тестов Node.js в `server/`**.
- Сборка Windows Release (`net10.0-windows10.0.19041.0`): 0 ошибок, 0 предупреждений (`-warnaserror`).
- Сборка Android Release (`net10.0-android`, с R8 и ILLink): 0 ошибок, 0 предупреждений (`-warnaserror`).
- Форматирование (`dotnet format`): 0 изменений.

### 7. Архитектура экспорта PDF и каталога шаблонов
- **Экспорт PDF:** QuestPDF 2026.9.1 используется строго под Windows. На Android внедрён нативный `Platforms/Android/AndroidPdfWriter.cs` (`Android.Graphics.Pdf.PdfDocument` + `StaticLayout`), исключающий тяжёлую зависимость и снижающий размер APK.
- **Виртуализация TemplatesPage:** `CollectionView` с `ItemSizingStrategy="MeasureFirstItem"`, дебаунс поиска 180 мс (`_searchDebounceCts`), фоновое обновление каталога `Task.Run(TemplateCatalog.Refresh)`, очистка теней `Border.Shadow` в `ListRow`.
- **Встроенный сидер:** `TemplateSeeder.cs` автоматически развертывает 70 эталонных шаблонов в 19 категориях с версионированием через Preferences.
