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

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        BottomTabs.ResetHover();   // сбросить «залипшее» наведение таббара

        await LoadAsync();
    }

    // ===== Загрузка =====
    private async Task LoadAsync()
    {
        var (profile, status) = await ProfileStorage.LoadWithStatusAsync();

        foreach (var f in _fields)
            f.Entry.Text = ProfileRules.Get(profile, f.Field);

        foreach (var f in _fields)
            ApplyValidation(f);

        UpdateProgress(animated: false);

        // Не показываем пустую форму молча: иначе «Сохранить» тихо затрёт старые данные
        if (status == ProfileLoadStatus.Failed)
            await DisplayAlertAsync("Профиль не прочитан",
                "Не удалось прочитать сохранённый профиль: возможно, данные повреждены или перенесены с другого устройства. " +
                "Если сохранить новые данные, старые будут заменены.",
                "Понятно");
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
    private async void OnSaveTapped(object? sender, TappedEventArgs e)
    {
        foreach (var f in _fields)
            f.Entry.Unfocus();

        // Проверяем все поля; незаполненные пропускаем, неверные подсвечиваем
        var invalid = _fields.Where(f => !ApplyValidation(f)).ToList();

        if (invalid.Count > 0) {
            var names = string.Join(", ", invalid.Select(f => ProfileRules.DisplayName(f.Field)));
            await DisplayAlertAsync("Проверьте данные",
                "Исправьте поля, выделенные красным: " + names + ".",
                "Понятно");

            invalid[0].Entry.Focus();
            return;
        }

        var profile = ReadForm(normalize: true);

        try {
            await ProfileStorage.SaveAsync(profile);

            // Показываем значения в том виде, в котором они сохранились
            foreach (var f in _fields)
                f.Entry.Text = ProfileRules.Get(profile, f.Field);

            await DisplayAlertAsync("✅ Готово", "Профиль сохранён на этом устройстве.", "Понятно");
        } catch (Exception ex) {
            await DisplayAlertAsync("Не удалось сохранить", ex.Message, "Понятно");
        }
    }

    // Заглушка: сканирование паспорта камерой появится вместе с разделом «Скан»
    private async void OnPassportScanTapped(object? sender, TappedEventArgs e)
    {
        await Stubs.ShowAsync("passport");
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
