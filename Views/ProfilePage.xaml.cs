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

    private readonly List<FieldUi> _fields = new();

    // Снимок формы на момент загрузки/сохранения: по нему видно несохранённые правки
    private string _baseline = "";
    private bool _leaving;

    private string Snapshot() => string.Join("\u001f", _fields.Select(f => f.Entry.Text ?? ""));
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

        var labelRow = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        labelRow.Children.Add(label);

        // У поля «Паспорт» — ссылка на скан вместо ручного ввода
        if (spec.Field == ProfileField.Passport)
            labelRow.Children.Add(CreateScanLink());

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
        stack.Children.Add(labelRow);
        stack.Children.Add(box);
        stack.Children.Add(error);
        return stack;
    }

    // Ссылка «📷 Сканировать» рядом с подписью поля «Паспорт»
    private View CreateScanLink()
    {
        var text = new Label { Text = "📷 Сканировать", FontSize = 12, FontAttributes = FontAttributes.Bold };
        text.SetDynamicResource(Label.TextColorProperty, "BrandGreen");

        var wrapper = new HorizontalStackLayout { VerticalOptions = LayoutOptions.Center };
        wrapper.Children.Add(text);

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnPassportScanTapped;
        wrapper.GestureRecognizers.Add(tap);

        return wrapper;
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

        foreach (var f in _fields)
            f.Entry.Text = ProfileRules.Get(profile, f.Field);

        foreach (var f in _fields)
            ApplyValidation(f);

        UpdateIgnoreUi();
        UpdateProgress(animated: false);
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

        return new ProfileData(
            FullName: Get(ProfileField.FullName),
            OrgName: Get(ProfileField.OrgName),
            Unp: Get(ProfileField.Unp),
            Address: Get(ProfileField.Address),
            Phone: Get(ProfileField.Phone),
            Passport: Get(ProfileField.Passport),
            Account: Get(ProfileField.Account));
    }

    // ===== Сохранение =====
    private async void OnSaveTapped(object? sender, TappedEventArgs e) => await TrySaveAsync(silent: false);

    // true — профиль сохранён. silent: без сообщения «Готово» (при выходе со страницы)
    private async Task<bool> TrySaveAsync(bool silent)
    {
        foreach (var f in _fields)
            f.Entry.Unfocus();

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

            // Показываем значения в том виде, в котором они сохранились
            foreach (var f in _fields)
                f.Entry.Text = ProfileRules.Get(profile, f.Field);

            // Принятые с ошибкой поля больше не красные, галочка прячется, процент пересчитан
            foreach (var f in _fields)
                ApplyValidation(f);

            IgnoreCheck.IsChecked = false;
            UpdateIgnoreUi();
            UpdateProgress(animated: false);
            _baseline = Snapshot();

            if (!silent)
                await Dialogs.AlertAsync("Готово", "Профиль сохранён на этом устройстве.", DialogKind.Success);

            return true;
        } catch (Exception ex) {
            await Dialogs.AlertAsync("Не удалось сохранить", ex.Message, DialogKind.Danger);
            return false;
        }
    }

    private async void OnPassportScanTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//ScanPage?mode=passport");
    }

    // ===== Проверка поля: красная рамка + подпись =====
    // Возвращает true, если ошибки нет (поле пустое или заполнено верно)
    private static bool ApplyValidation(FieldUi f)
    {
        string? error = ProfileRules.Validate(f.Field, f.Entry.Text);

        if (error is null) {
            // Вернуть рамку из стиля FieldBox (цвет подхватывает тема)
            f.Box.ClearValue(Border.StrokeProperty);
            f.Box.ClearValue(Border.StrokeThicknessProperty);
        } else {
            f.Box.Stroke = new SolidColorBrush(ThemeService.ErrorColor);
            f.Box.StrokeThickness = 1.5;
            f.Error.Text = error;
        }

        f.Error.IsVisible = error is not null;
        return error is null;
    }

    // ===== Прогресс заполнения =====
    private void UpdateProgress(bool animated)
    {
        var summary = ProfileRules.Summarize(ReadForm(normalize: false));

        ProgressTitle.Text = $"Заполнено на {summary.Percent}%";
        ProgressHint.Text = summary.Hint;

        if (animated)
            _ = FillProgress.ProgressTo(summary.Progress, 250, Easing.CubicOut);
        else
            FillProgress.Progress = summary.Progress;
    }
}
