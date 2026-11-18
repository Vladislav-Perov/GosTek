# NOTES.md

## Анализ текущей системы правил и схема рефакторинга

### 1. Где хранятся пользовательские правила
- Пользовательские правила сериализуются в JSON-файл `custom_rules.json` через сервис `CustomRulesService`.
- Путь по умолчанию: `Path.Combine(FileSystem.AppDataDirectory, "custom_rules.json")` (с fallback во временную папку `Path.GetTempPath()`).
- В JSON хранятся объекты `CustomRuleDto`.

### 2. Как выбирается активное правило
- На вкладке «Проверка» (`CheckPage`) отображается `ChipBar RuleChips`.
- `_allRules = CustomRulesService.Instance.GetAllRules().ToList()`.
- Выбранный индекс чипа `RuleChips.SelectedIndex` определяет активное правило:
  `CurrentRules => (i >= 0 && i < _allRules.Count) ? _allRules[i] : RuleSets.MasterRule`.
- При выборе файла вызывается `CustomRulesService.Instance.MatchRuleSetForTemplate(fileName)`.

### 3. Где создавались «(копия)» и дубликаты
- В старой логике `RuleEditorPage.BuildRuleFromForm()`:
  `string id = _editingRule is { IsCustom: true } ? _editingRule.Id : Guid.NewGuid().ToString("N")[..8];`
  При редактировании встроенного правила (например `Universal`) создавалось новое правило со случайным `Guid` вместо модификации по месту, а в прежних версиях к имени приписывалось ` (копия)`.
- Дубликаты накапливались при каждом сохранении из-за генерации новых `Id`.

### 4. Как удалить старые дубли «(копия)» вручную
Пользователь может удалить дубликаты двумя способами:
1. **Через UI:** Открыть чип дубликата на вкладке «Проверка», нажать «Настроить», в открывшемся редакторе нажать кнопку «Удалить» и подтвердить удаление в диалоговом окне.
2. **Вручную через файл:** Открыть файл `%AppData%/custom_rules.json` (или `Android/data/.../files/custom_rules.json`) в текстовом редакторе и удалить ненужные объекты из массива JSON.

---

## План реализации требований

1. **`Models/CheckModels.cs` (`RuleSets`):**
   - Константа `public const string MasterRuleId = "master-rule";`.
   - Главное правило `MasterRule` («Основной ГОСТ», Id = `MasterRuleId`).
   - `Universal => MasterRule` (алиас для совместимости).
   - Удалить `StudyRules`, `ApplicationRules`, `ContractRules` из `RuleSets.Presets`. `Presets` содержит только `MasterRule`.

2. **`Services/CustomRulesService.cs`:**
   - Хранение `_masterRule` в сервисе. При загрузке `custom_rules.json`, если найдена запись с `Id == MasterRuleId` (или старый `universal`), её настройки загружаются в `_masterRule`.
   - `GetAllRules()` возвращает `_masterRule` первым, затем пользовательские правила по порядку создания.
   - `SaveRuleAsync(rule)`: сохраняет правки по `rule.Id`. Если `rule.Id == MasterRuleId`, обновляет `_masterRule` и сохраняет в JSON без создания копий.
   - `DeleteRuleAsync(id)`: отклоняет удаление `MasterRuleId` (и старых системных Id), возвращая `false`.
   - `NormalizeRuleId(id)`: миграция старых Id (`universal`, `study_bgtu`, `applications`, `contracts`) в `MasterRuleId`.
   - Метод `CreateNewRule()`: генерирует правило со значениями ГОСТ по умолчанию, уникальным Id и именем «Новое правило» (или «Новое правило 2» и т.д.).

3. **`Views/RuleEditorPage.xaml(.cs)`:**
   - Если редактируется главное правило: поле названия заблокировано для ввода (`IsReadOnly = true`), кнопка «Удалить» скрыта.
   - Если редактируется пользовательское правило: поле названия доступно, отображается кнопка «Удалить» с диалогом подтверждения `Dialogs.ConfirmAsync`.
   - Кнопка «Создать на основе этого» в редакторе для явного клонирования.
   - `OnSaveTapped` строго сохраняет правило с его текущим `_editingRule.Id` без суффиксов «(копия)» и без новых Id.

4. **`Views/CheckPage.xaml(.cs)`:**
   - Чипы: «Основной ГОСТ», затем пользовательские по порядку создания, последним — чип `+`.
   - Клик по `+` вызывает создание нового правила через `CreateNewRule()`, сохраняет его и открывает редактор.
   - Описание под чипами (`RuleNote`) обновляется при смене чипа.
   - При удалении активного правила активным становится «Основной ГОСТ».

5. **`GosTek.Tests/CustomRulesServiceTests.cs`:**
   - Тесты на все 6 требуемых сценариев: сохранение дважды с одним Id, сохранение/чтение MasterRule, запрет удаления MasterRule, уникальные имена и разные Id для «+», миграция старых Id, переключение на главное при удалении.
