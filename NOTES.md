# NOTES.md

## План и статус реализации интерактивного UI карточек замечаний (Ветка: `night/check-ui`)

### 1. Проверка окружения и предусловий (Выполнено)
- **Рабочая копия:** Подтверждено, что `Fix` и `FixItems` работают строго с временной копией в кэше (`_workPath`), оригинальный файл пользователя не модифицируется.
- **DocumentChecker:** Логика не менялась, используются методы `Check`, `Fix`, `FixItems`, структуры `IssueItem` и `CheckIssue.Items`.
- **Toast & Dialogs:** Используются существующие в проекте `Toast.Show(...)` и `Dialogs.AlertAsync(...)`.

### 2. Структура ViewModel (`Views/CheckGroupVm.cs`) (Выполнено)
- `CheckGroupVm : INotifyPropertyChanged`:
  - Представляет замечание `CheckIssue`.
  - Свойства: `Issue`, `IsExpanded`, `Items` (список `CheckItemVm`), `HasItems`, `MoreText` («…ещё K»), `Title`, `WhereText` («N из M абзацев»), `DiffText` («Сейчас: X → Будет: Y» / с «строки» для межстрочного интервала), `HasDiff`, `CanFix`, `CannotFix`, `FixAllHint` («Принять: исправит все N»), `ExpandButtonText` («Подробнее ▾» / «Свернуть ▴»).
- `CheckItemVm : INotifyPropertyChanged`:
  - Представляет строку `IssueItem`.
  - Свойства: `Item`, `Parent`, `DisplayText` (в кавычках «...» или «Раздел N» для полей страницы), `DiffSubtext` (`Actual → Expected`), `CanFix`.
- `CheckIssueFilter`:
  - Статический метод `FilterIssues(IEnumerable<CheckIssue> issues, IReadOnlySet<(IssueKind Kind, int Index, string Text)> rejected)` для чистой фильтрации отклонённых элементов и карточек без UI-зависимостей.

### 3. Разметка (`Views/CheckPage.xaml`) (Выполнено)
- Единый вертикальный `ScrollView` с `MaximumWidthRequest="950"`.
- Список замечаний привязан к `ObservableCollection<CheckGroupVm> IssueGroups`.
- Свёрнутая карточка (~90 px):
  - Строка 1: `Title` (bold) слева, `WhereText` («N из M абзацев») справа.
  - Строка 2: Крупная строка различий `DiffText` («Сейчас → Будет»).
  - Строка 3: слева подпись мелким серым шрифтом «Принять: исправит все N», справа `FlexLayout` с переносом (адаптивность под 500 px): «Подробнее ▾» (при `HasItems`), «Отклонить», «Принять» (или тег «Вручную»).
- Развёрнутая карточка:
  - Список `Items`: слева `DisplayText` (одна строка с обрезкой) и под ним мелким шрифтом `DiffSubtext`, справа кнопки «Отклонить» и «Принять» для строки.
  - При превышении лимита — строка `MoreText` («…ещё K»).
- Шапка списка «Замечания к оформлению N» и «Принять все» видны только при `HasIssues` / `HasFixableIssues`. Без файла их нет.

### 4. Поведение и команды (`Views/CheckPage.xaml.cs`) (Выполнено)
- `AcceptItemCommand`: точечный вызов `DocumentChecker.FixItems` на `_workPath` в `Task.Run` с индексом и текстом элемента, затем вызов `RecheckAsync()` (`DocumentChecker.Check`) с обновлением списка и сохранением раскрытия раскрытых карточек.
- `AcceptGroupCommand`: вызов `FixItems` по индексам всех неотклонённых элементов карточки (или `Fix` для замечаний без `Items`), затем `RecheckAsync()`.
- `AcceptAllCommand`: вызов `Fix` по всем неотклонённым исправимым типам, затем `RecheckAsync()`.
- `RejectItemCommand` / `RejectGroupCommand`: добавление ключей `(Kind, Index, Text)` (или `(Kind, -1, Title)` для замечаний без `Items`) в `HashSet`, повторная фильтрация, тост «Скрыто, вернётся при повторной проверке». Сброс набора при выборе файла, смене правила или перепроверке.
- `ToggleExpandCommand`: инвертирование `IsExpanded` у группы.
- Блокировка от гонок через `_busy` и индикация через `Loader`. Ошибки обрабатываются через `Dialogs.AlertAsync`.

### 5. Тестирование и верификация (Выполнено)
- Юнит-тесты фильтрации в `GosTek.Tests/CheckFilterTests.cs` (8 новых тестов).
- `dotnet build GosTek.sln`: 0 предупреждений, 0 ошибок.
- `dotnet test GosTek.Tests/GosTek.Tests.csproj`: 332 теста пройдено, 0 упало (было 324).
