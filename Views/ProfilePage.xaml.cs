/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Экран редактирования реквизитов организации и гражданина с динамической валидацией и пользовательскими полями
 * СОДЕРЖИМОЕ: ProfilePage
 * ЗАВИСИМОСТИ: GosTek.Views.ProfileRules, GosTek.Views.ProfileStorage, GosTek.Resources.Styles.Icons
 * ==============================================================================
 */
using GosTek.Resources.Styles;
using GosTek.Services;
using GosTek.Views.Controls;

namespace GosTek.Views;

public partial class ProfilePage : ContentPage
{
    // Описание поля формы: поля создаются из этой таблицы
    private sealed record FieldSpec(
        ProfileField Field,
        string Label,
        string Placeholder,
        Keyboard Keyboard,
        int MaxLength = int.MaxValue);

    // Порядок = порядок на экране и в enum ProfileField
    private static readonly FieldSpec[] Specs =
    {
        new(ProfileField.FullName, "ФИО",               "Фамилия Имя Отчество",         Keyboard.Text),
        new(ProfileField.OrgName,  "Наименование",      "Например, ИП Иванов И. И.",    Keyboard.Text),
        new(ProfileField.Unp,      "УНП",               "9 цифр",                       Keyboard.Numeric,   MaxLength: 9),
        new(ProfileField.Address,  "Юридический адрес", "Город, улица, дом, квартира",  Keyboard.Text),
        new(ProfileField.Phone,    "Телефон",           "+375 29 000-00-00",            Keyboard.Telephone, MaxLength: 20),
        new(ProfileField.Passport, "Паспорт",           "Серия и номер",                Keyboard.Text,      MaxLength: 12),
        new(ProfileField.Account,  "Расчётный счёт",    "Введите номер счёта",          Keyboard.Text,      MaxLength: 34),
    };

    // Поле на экране: поле ввода, рамка вокруг него и красная подпись с ошибкой
    private sealed record FieldUi(ProfileField Field, Entry Entry, Border Box, Label Error);
    private sealed record CustomFieldUi(string Name, Entry Entry, Border Box, View Container);

    private readonly List<FieldUi> _fields = new();
    private readonly List<CustomFieldUi> _customFields = new();

    // Снимок формы на момент загрузки/сохранения: по нему видно несохранённые правки
    private string _baseline = "";
    private bool _leaving;
    private bool _isUpdatingFields;

    private string Snapshot() =>
        string.Join("\u001f", _fields.Select(f => f.Entry.Text ?? "")) + "\u001e" +
        string.Join("\u001f", _customFields.Select(c => $"{c.Name}:{c.Entry.Text}"));

    private bool HasUnsaved => Snapshot() != _baseline;

    public ProfilePage()
    {
        InitializeComponent();

        foreach (var spec in Specs)
            FieldsHost.Children.Add(CreateField(spec));

        // Проверка и прогресс пересчитываются при вводе; Enter переводит на следующее поле
        for (int i = 0; i < _fields.Count; i++) {
            var field = _fields[i];

            field.Entry.TextChanged += (_, _) => {
                if (_isUpdatingFields)
                    return;

                if (field.Field == ProfileField.FullName)
                {
                    var text = field.Entry.Text;
                    var titleCased = SmartInputHelper.ToTitleCase(text);
                    if (text != titleCased)
                    {
                        int cursor = field.Entry.CursorPosition;
                        _isUpdatingFields = true;
                        field.Entry.Text = titleCased;
                        field.Entry.CursorPosition = Math.Min(cursor, titleCased.Length);
                        _isUpdatingFields = false;
                    }
                }

                ProfileRules.ForgetAccepted(field.Field);
                ApplyValidation(field);
                UpdateIgnoreUi();
                UpdateProgress(animated: true);
            };

            if (i + 1 < _fields.Count) {
                var next = _fields[i + 1].Entry;
                field.Entry.ReturnType = ReturnType.Next;
                field.Entry.Completed += (_, _) => next.Focus();
            } else {
                field.Entry.ReturnType = ReturnType.Done;
            }
        }
    }

    // Подпись, рамка с полем ввода и скрытая красная подпись для текста ошибки
    private VerticalStackLayout CreateField(FieldSpec spec)
    {
        var label = new Label { Text = spec.Label, StyleClass = new[] { "FieldLabel" } };

        var entry = new Entry {
            Placeholder = spec.Placeholder,
            Keyboard = spec.Keyboard,
            MaxLength = spec.MaxLength,
            StyleClass = new[] { "FieldEntry" },
        };

        var box = new Border { Content = entry, StyleClass = new[] { "FieldBox" } };

        var error = new Label {
            FontSize = 12,
            TextColor = ThemeService.ErrorColor,
            IsVisible = false,
            LineBreakMode = LineBreakMode.WordWrap,
        };

        _fields.Add(new FieldUi(spec.Field, entry, box, error));

        var stack = new VerticalStackLayout { Spacing = 6 };
        stack.Children.Add(label);
        stack.Children.Add(box);
        stack.Children.Add(error);
        return stack;
    }

    // Создание строки для пользовательского динамического поля (CustomField)
    private View CreateCustomFieldUi(string fieldName, string? initialValue)
    {
        var label = new Label
        {
            Text = fieldName,
            StyleClass = new[] { "FieldLabel" },
            VerticalOptions = LayoutOptions.Center,
        };

        var trashPath = Icons.CreatePath("trash", size: 14, strokeColorResource: "TextSecondary", strokeThickness: 1.8);
        trashPath.VerticalOptions = LayoutOptions.Center;
        trashPath.HorizontalOptions = LayoutOptions.Center;

        var deleteBtn = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(6, 4),
            Content = trashPath,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
        };

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        headerGrid.Children.Add(label);
        Grid.SetColumn(label, 0);
        headerGrid.Children.Add(deleteBtn);
        Grid.SetColumn(deleteBtn, 1);

        var entry = new Entry
        {
            Placeholder = $"Значение для «{fieldName}»",
            Text = initialValue ?? "",
            StyleClass = new[] { "FieldEntry" },
        };

        entry.TextChanged += (_, _) =>
        {
            if (!_isUpdatingFields)
                UpdateProgress(animated: true);
        };

        var box = new Border
        {
            Content = entry,
            StyleClass = new[] { "FieldBox" }
        };

        var stack = new VerticalStackLayout { Spacing = 6 };
        stack.Children.Add(headerGrid);
        stack.Children.Add(box);

        var customUi = new CustomFieldUi(fieldName, entry, box, stack);
        _customFields.Add(customUi);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            HapticHelper.Click();
            _customFields.Remove(customUi);
            CustomFieldsHost.Children.Remove(stack);
        };
        deleteBtn.GestureRecognizers.Add(tap);

        return stack;
    }

    public async void OnAddCustomFieldTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        var name = await DisplayPromptAsync(
            "Новое поле профиля",
            "Введите название реквизита (например: Должность, Отдел, Номер договора):",
            "Добавить",
            "Отмена",
            placeholder: "Название поля",
            maxLength: 60);

        if (string.IsNullOrWhiteSpace(name))
            return;

        name = name.Trim();

        if (Specs.Any(s => string.Equals(s.Label, name, StringComparison.OrdinalIgnoreCase)) ||
            _customFields.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            await Dialogs.AlertAsync("Поле уже существует", $"Поле с названием «{name}» уже есть в профиле.", DialogKind.Warning);
            return;
        }

        var view = CreateCustomFieldUi(name, "");
        CustomFieldsHost.Children.Add(view);
        Toast.Show($"Поле «{name}» добавлено", "plus");

        var created = _customFields.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        created?.Entry.Focus();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        BottomTabs.ResetHover();   // сбросить «залипшее» наведение таббара

        if (Shell.Current is { } shell)
            shell.Navigating += OnShellNavigating;

        await LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        if (Shell.Current is { } shell)
            shell.Navigating -= OnShellNavigating;
    }

    // ===== Выход с несохранёнными данными =====
    private async Task<bool> AskLeaveAsync()
    {
        var choice = await Dialogs.ChoiceAsync(
            "Есть несохранённые данные", "Сохранить изменения, прежде чем выйти?", DialogKind.Question,
            new DialogButton("Сохранить и выйти", DialogButtonStyle.Primary),
            new DialogButton("Выйти без сохранения", DialogButtonStyle.DangerOutline),
            new DialogButton("Остаться"));

        if (choice == 0)
            return await TrySaveAsync(silent: true);

        return choice == 1;   // 2 или закрытие окна: остаёмся
    }

    private async void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        if (_leaving || !e.CanCancel || !HasUnsaved)
            return;

        if (e.Target?.Location.OriginalString.Contains("ProfilePage") == true)
            return;

        var deferral = e.GetDeferral();
        try {
            if (!await AskLeaveAsync())
                e.Cancel();
        } finally {
            deferral.Complete();
        }
    }

    // «Назад» на Android
    protected override bool OnBackButtonPressed()
    {
        if (!HasUnsaved)
            return base.OnBackButtonPressed();

        _ = BackAsync();
        return true;
    }

    private async Task BackAsync()
    {
        if (!await AskLeaveAsync())
            return;

        _leaving = true;
        try { await Shell.Current.GoToAsync("//MainPage"); }
        finally { _leaving = false; }
    }

    // ===== «Игнорировать ошибки» =====
    // Галочка видна только при ошибках; при её включении кнопка меняет подпись,
    // а перед сохранением спрашиваем подтверждение
    private void UpdateIgnoreUi()
    {
        bool hasErrors = _fields.Any(f => f.Error.IsVisible);

        IgnoreRow.IsVisible = hasErrors;
        if (!hasErrors)
            IgnoreCheck.IsChecked = false;

        SaveLabel.Text = IgnoreCheck.IsChecked ? "Сохранить с ошибками" : "Сохранить";
    }

    private void OnIgnoreChanged(object? sender, CheckedChangedEventArgs e) => UpdateIgnoreUi();

    private void OnIgnoreLabelTapped(object? sender, TappedEventArgs e) => IgnoreCheck.IsChecked = !IgnoreCheck.IsChecked;

    // ===== Загрузка =====
    private async Task LoadAsync()
    {
        var (profile, status) = await ProfileStorage.LoadWithStatusAsync();

        _isUpdatingFields = true;
        try {
            foreach (var f in _fields)
                f.Entry.Text = ProfileRules.Get(profile, f.Field);

            _customFields.Clear();
            CustomFieldsHost.Children.Clear();
            if (profile.CustomFields != null)
            {
                foreach (var (k, v) in profile.CustomFields)
                {
                    var view = CreateCustomFieldUi(k, v);
                    CustomFieldsHost.Children.Add(view);
                }
            }
        } finally {
            _isUpdatingFields = false;
        }

        foreach (var f in _fields)
            ApplyValidation(f);

        UpdateIgnoreUi();
        UpdateProgress(animated: true);
        _baseline = Snapshot();

        // Не показываем пустую форму молча: иначе «Сохранить» тихо затрёт старые данные
        if (status == ProfileLoadStatus.Failed)
            await Dialogs.AlertAsync("Профиль не прочитан",
                "Не удалось прочитать сохранённый профиль: возможно, данные повреждены или перенесены с другого устройства. " +
                "Если сохранить новые данные, старые будут заменены.",
                DialogKind.Warning);
    }

    // Значения из формы. normalize = true: в виде для сохранения (паспорт, счёт)
    private ProfileData ReadForm(bool normalize)
    {
        string Get(ProfileField field)
        {
            var text = _fields.First(f => f.Field == field).Entry.Text;
            return normalize ? ProfileRules.Normalize(field, text) : text ?? "";
        }

        var customDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _customFields)
        {
            var val = c.Entry.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(val))
                customDict[c.Name] = val;
        }

        return new ProfileData(
            FullName: Get(ProfileField.FullName),
            OrgName: Get(ProfileField.OrgName),
            Unp: Get(ProfileField.Unp),
            Address: Get(ProfileField.Address),
            Phone: Get(ProfileField.Phone),
            Passport: Get(ProfileField.Passport),
            Account: Get(ProfileField.Account),
            CustomFields: customDict);
    }

    // ===== Сохранение =====
    private async void OnSaveTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        await TrySaveAsync(silent: false);
    }

    // true — профиль сохранён. silent: без сообщения «Готово» (при выходе со страницы)
    private async Task<bool> TrySaveAsync(bool silent)
    {
        foreach (var f in _fields)
            f.Entry.Unfocus();

        foreach (var c in _customFields)
            c.Entry.Unfocus();

        // Проверяем все поля; незаполненные пропускаем, неверные подсвечиваем
        var invalid = _fields.Where(f => !ApplyValidation(f)).ToList();
        UpdateIgnoreUi();

        if (invalid.Count > 0) {
            var names = string.Join(", ", invalid.Select(f => ProfileRules.DisplayName(f.Field)));

            if (!IgnoreCheck.IsChecked) {
                await Dialogs.AlertAsync("Проверьте данные",
                    "Исправьте поля, выделенные красным: " + names + ".\n" +
                    "Или отметьте «Игнорировать ошибки», чтобы сохранить как есть.",
                    DialogKind.Warning);

                invalid[0].Entry.Focus();
                return false;
            }

            if (!await Dialogs.ConfirmAsync("Сохранить с ошибками?",
                    "Есть ошибки в полях: " + names + ". Документы могут заполниться неверно.",
                    "Сохранить"))
                return false;
        }

        try {
            var profile = ReadForm(normalize: true);
            await ProfileStorage.SaveAsync(profile);
            ProfileRules.SetAccepted(profile, invalid.Select(f => f.Field));

            _isUpdatingFields = true;
            try {
                // Показываем значения в том виде, в котором они сохранились
                foreach (var f in _fields)
                    f.Entry.Text = ProfileRules.Get(profile, f.Field);
            } finally {
                _isUpdatingFields = false;
            }

            // Принятые с ошибкой поля больше не красные, галочка прячется, процент пересчитан
            foreach (var f in _fields)
                ApplyValidation(f);

            IgnoreCheck.IsChecked = false;
            UpdateIgnoreUi();
            UpdateProgress(animated: false);
            _baseline = Snapshot();

            if (!silent)
            {
                HapticHelper.Success();
                Toast.Show("Профиль сохранён", "user");
            }

            return true;
        } catch (Exception ex) {
            await Dialogs.AlertAsync("Не удалось сохранить", ex.Message, DialogKind.Danger);
            return false;
        }
    }

    // Кнопка «Заполнить по фото паспорта»
    private async void OnPassportScanTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//ScanPage?mode=passport");
    }

    // ===== Проверка поля: красная рамка + подпись =====
    // Возвращает true, если ошибки нет (поле пустое или заполнено верно). Сама подсветка — в FieldValidationHelper
    private static bool ApplyValidation(FieldUi f)
        => FieldValidationHelper.Apply(f.Box, f.Error, ProfileRules.Validate(f.Field, f.Entry.Text));

    // ===== Прогресс заполнения =====
    private void UpdateProgress(bool animated)
    {
        var summary = ProfileRules.Summarize(ReadForm(normalize: false));
        ProgressHint.Text = summary.Hint;

        if (animated)
        {
            this.AbortAnimation("ProfilePercentCounter");

            int startPercent = 0;
            if (int.TryParse(ProgressBadgeLabel.Text?.TrimEnd('%'), out int parsed))
                startPercent = parsed;

            int targetPercent = summary.Percent;

            _ = FillProgress.ProgressTo(summary.Progress, 1000, Easing.CubicInOut);

            if (startPercent != targetPercent)
            {
                var anim = new Animation(v =>
                {
                    int current = (int)Math.Round(v);
                    ProgressTitle.Text = $"Заполнено на {current}%";
                    ProgressBadgeLabel.Text = $"{current}%";
                }, startPercent, targetPercent, Easing.CubicInOut);

                anim.Commit(this, "ProfilePercentCounter", 16, 1000, Easing.CubicInOut, (_, _) =>
                {
                    ProgressTitle.Text = $"Заполнено на {targetPercent}%";
                    ProgressBadgeLabel.Text = $"{targetPercent}%";
                });
            }
            else
            {
                ProgressTitle.Text = $"Заполнено на {targetPercent}%";
                ProgressBadgeLabel.Text = $"{targetPercent}%";
            }
        }
        else
        {
            this.AbortAnimation("ProfilePercentCounter");
            FillProgress.Progress = summary.Progress;
            ProgressTitle.Text = $"Заполнено на {summary.Percent}%";
            ProgressBadgeLabel.Text = $"{summary.Percent}%";
        }
    }
}
