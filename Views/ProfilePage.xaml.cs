using System.Text.RegularExpressions;

namespace GosTek.Views;

// =====================================================================
//  ПРАВИЛА ПРОФИЛЯ (общие для страницы профиля и плашки на главной)
// =====================================================================

public enum ProfileField { FullName, OrgName, Unp, Address, Phone, Passport, Account }

public static class ProfileRules
{
    // Порядок полей = порядок значений в Summarize / ToValues
    public static readonly ProfileField[] All = Enum.GetValues<ProfileField>();

    // Missing — для фразы «Не хватает …», Check — для фразы «Проверьте: …»
    private static readonly Dictionary<ProfileField, (string Missing, string Check)> Names = new() {
        [ProfileField.FullName] = ("ФИО", "ФИО"),
        [ProfileField.OrgName] = ("наименования", "наименование"),
        [ProfileField.Unp] = ("УНП", "УНП"),
        [ProfileField.Address] = ("юридического адреса", "юридический адрес"),
        [ProfileField.Phone] = ("телефона", "телефон"),
        [ProfileField.Passport] = ("паспорта", "паспорт"),
        [ProfileField.Account] = ("расчётного счёта", "расчётный счёт"),
    };

    public static string DisplayName(ProfileField field) => Names[field].Check;

    // Кириллические буквы, похожие на латинские (в серии паспорта)
    private static readonly Dictionary<char, char> CyrToLat = new() {
        ['А'] = 'A',
        ['В'] = 'B',
        ['Е'] = 'E',
        ['К'] = 'K',
        ['М'] = 'M',
        ['Н'] = 'H',
        ['О'] = 'O',
        ['Р'] = 'P',
        ['С'] = 'C',
        ['Т'] = 'T',
        ['Х'] = 'X',
    };

    private static readonly Regex NameWord = new(@"^\p{L}[\p{L}\-']*$");
    private static readonly Regex PhoneChars = new(@"^\+?[\d\s()\-]+$");
    private static readonly Regex PassportFormat = new(@"^[A-Z]{2}\d{7}$");
    private static readonly Regex IbanFormat = new(@"^BY\d{2}[A-Z0-9]{4}\d{4}[A-Z0-9]{16}$");

    // ===== Проверка одного поля =====
    // null  = поле пустое или заполнено верно
    // текст = ошибка, которую покажем под полем
    public static string? Validate(ProfileField field, string? raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Length == 0)
            return null;

        return field switch {
            ProfileField.FullName => ValidateFullName(v),
            ProfileField.OrgName => ValidateOrgName(v),
            ProfileField.Unp => ValidateUnp(v),
            ProfileField.Address => ValidateAddress(v),
            ProfileField.Phone => ValidatePhone(v),
            ProfileField.Passport => ValidatePassport(v),
            ProfileField.Account => ValidateAccount(v),
            _ => null,
        };
    }

    private static string? ValidateFullName(string v)
    {
        var words = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // 1. Слов должно быть ровно три
        if (words.Length < 3)
            return "Введите фамилию, имя и отчество полностью";
        if (words.Length > 3)
            return "Нужны только фамилия, имя и отчество";

        // 2. Каждое слово только буквы, дефис, апостроф
        if (words.Any(w => !NameWord.IsMatch(w)))
            return "Только буквы и дефис, без сокращений";

        // 3. Каждое слово минимум 3 символа
        if (words.Any(w => w.Length < 3))
            return "Каждое слово — минимум 3 буквы";

        return null;
    }

    private static string? ValidateOrgName(string v)
    {
        if (v.Length < 3)
            return "Минимум 3 символа";
        if (!v.Any(char.IsLetter))
            return "Наименование должно содержать буквы";

        return null;
    }

    private static string? ValidateUnp(string v)
    {
        if (!v.All(char.IsAsciiDigit))
            return "УНП состоит только из цифр";
        if (v.Length != 9)
            return $"УНП состоит из 9 цифр (введено {v.Length})";

        return null;
    }

    private static string? ValidateAddress(string v)
    {
        if (v.Length < 8)
            return "Слишком короткий адрес";
        if (!v.Any(char.IsLetter))
            return "Укажите город и улицу";
        if (!v.Any(char.IsAsciiDigit))
            return "Укажите номер дома";

        return null;
    }

    private static string? ValidatePhone(string v)
    {
        const string format = "Формат: +375 29 000-00-00";

        if (!PhoneChars.IsMatch(v))
            return "Допустимы цифры, пробелы, +, ( ) и -";

        var digits = new string(v.Where(char.IsAsciiDigit).ToArray());

        bool by375 = digits.StartsWith("375") && digits.Length == 12;
        bool by80 = digits.StartsWith("80") && digits.Length == 11;

        return by375 || by80 ? null : format;
    }

    private static string? ValidatePassport(string v)
    {
        return PassportFormat.IsMatch(NormalizePassport(v))
            ? null
            : "Формат: 2 буквы и 7 цифр, например KN 1234567";
    }

    private static string? ValidateAccount(string v)
    {
        var iban = NormalizeAccount(v);

        if (iban.Length != 28)
            return $"Счёт (IBAN) — 28 символов (введено {iban.Length})";
        if (!iban.StartsWith("BY"))
            return "Счёт должен начинаться с BY";
        if (!IbanFormat.IsMatch(iban))
            return "Неверный формат счёта";
        if (!IbanChecksumOk(iban))
            return "Неверная контрольная сумма счёта";

        return null;
    }

    // Контрольная сумма IBAN (mod 97)
    private static bool IbanChecksumOk(string iban)
    {
        var rearranged = iban[4..] + iban[..4];
        int rem = 0;

        foreach (char c in rearranged) {
            if (char.IsAsciiDigit(c))
                rem = (rem * 10 + (c - '0')) % 97;
            else
                rem = (rem * 100 + (c - 'A' + 10)) % 97;
        }

        return rem == 1;
    }

    // ===== Приведение значения к виду для сохранения =====
    public static string Normalize(ProfileField field, string? raw)
    {
        var v = (raw ?? "").Trim();

        return field switch {
            ProfileField.Passport => FormatPassport(v),
            ProfileField.Account => NormalizeAccount(v),
            _ => v,
        };
    }

    private static string NormalizePassport(string v)
    {
        var chars = v.Replace(" ", "").ToUpperInvariant()
                     .Select(c => CyrToLat.TryGetValue(c, out var lat) ? lat : c);
        return new string(chars.ToArray());
    }

    // KN1234567 -> KN 1234567 (если формат неверный, оставляем как введено)
    private static string FormatPassport(string v)
    {
        var n = NormalizePassport(v);
        return PassportFormat.IsMatch(n) ? n[..2] + " " + n[2..] : v;
    }

    private static string NormalizeAccount(string v)
        => v.Replace(" ", "").ToUpperInvariant();

    // ===== Сводка для плашки «Мой профиль» (профиль и главная) =====
    public static string?[] ToValues(ProfileData p) => new string?[] {
        p.FullName, p.OrgName, p.Unp, p.Address, p.Phone, p.Passport, p.Account,
    };

    public static (int Percent, double Progress, string Hint) Summarize(IReadOnlyList<string?> values)
    {
        var missing = new List<string>();
        var check = new List<string>();

        for (int i = 0; i < All.Length; i++) {
            var field = All[i];
            var value = i < values.Count ? values[i] : null;

            if (string.IsNullOrWhiteSpace(value))
                missing.Add(Names[field].Missing);
            else if (Validate(field, value) is not null)
                check.Add(Names[field].Check);
        }

        int filled = All.Length - missing.Count - check.Count;
        double progress = (double)filled / All.Length;
        int percent = (int)Math.Round(progress * 100);

        var parts = new List<string>();
        if (missing.Count > 0)
            parts.Add("Не хватает " + string.Join(", ", missing) + ".");
        if (check.Count > 0)
            parts.Add("Проверьте: " + string.Join(", ", check) + ".");
        if (parts.Count == 0)
            parts.Add("Все данные заполнены.");

        return (percent, progress, string.Join(" ", parts));
    }
}

// =====================================================================
//  СТРАНИЦА ПРОФИЛЯ
// =====================================================================

public partial class ProfilePage : ContentPage
{
    private static readonly Color ErrorColor = Color.FromArgb("#EF4444");

    // Поле: само поле ввода, рамка вокруг него и красная подпись с ошибкой
    private sealed record FieldUi(ProfileField Field, Entry Entry, Border Box, Label Error, SolidColorBrush NormalStroke);

    // Порядок = порядок в enum ProfileField (по нему считается прогресс)
    private readonly List<FieldUi> _fields;

    public ProfilePage()
    {
        InitializeComponent();

        _fields = new() {
            CreateField(ProfileField.FullName, FullNameEntry),
            CreateField(ProfileField.OrgName,  OrgNameEntry),
            CreateField(ProfileField.Unp,      UnpEntry),
            CreateField(ProfileField.Address,  AddressEntry),
            CreateField(ProfileField.Phone,    PhoneEntry),
            CreateField(ProfileField.Passport, PassportEntry),
            CreateField(ProfileField.Account,  AccountEntry),
        };

        // Проверка и прогресс пересчитываются при вводе; Enter переводит на следующее поле
        for (int i = 0; i < _fields.Count; i++) {
            var field = _fields[i];
            var next = i + 1 < _fields.Count ? _fields[i + 1].Entry : null;

            field.Entry.TextChanged += (_, _) => {
                ApplyValidation(field);
                UpdateProgress(animated: true);
            };

            if (next is not null) {
                field.Entry.ReturnType = ReturnType.Next;
                field.Entry.Completed += (_, _) => next.Focus();
            } else {
                field.Entry.ReturnType = ReturnType.Done;
            }
        }
    }

    // Под рамкой поля добавляем скрытую красную подпись для текста ошибки
    private static FieldUi CreateField(ProfileField field, Entry entry)
    {
        var box = (Border)entry.Parent;
        var stack = (VerticalStackLayout)box.Parent;

        // Brush, чей Color подписан на ресурс "CardStroke" —
        // при смене темы цвет сам обновится.
        var normalStroke = new SolidColorBrush();
        normalStroke.SetDynamicResource(SolidColorBrush.ColorProperty, "CardStroke");
        box.Stroke = normalStroke;

        var error = new Label {
            FontSize = 12,
            TextColor = ErrorColor,
            IsVisible = false,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        stack.Children.Add(error);

        return new FieldUi(field, entry, box, error, normalStroke);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SyncToggleVisual(animated: false);   // подхватить тему, выбранную на другой странице
        BottomTabs.ResetHover();             // сбросить «залипшее» наведение таббара

        await LoadAsync();
    }

    // ===== Переключатель темы =====
    private void OnThemeToggleTapped(object? sender, TappedEventArgs e)
    {
        ThemeService.Toggle();
        SyncToggleVisual(animated: true);
    }

    private void SyncToggleVisual(bool animated)
    {
        bool dark = ThemeService.IsDark;
        ThemeIcon.Text = dark ? "🌙" : "☀️";

        double target = dark ? 0 : 18;

        if (animated)
            _ = ToggleThumb.TranslateToAsync(target, 0, 150, Easing.CubicInOut);
        else
            ToggleThumb.TranslationX = target;
    }

    // ===== Загрузка =====
    private async Task LoadAsync()
    {
        var p = await ProfileStorage.LoadAsync();

        FullNameEntry.Text = p.FullName;
        OrgNameEntry.Text = p.OrgName;
        UnpEntry.Text = p.Unp;
        AddressEntry.Text = p.Address;
        PhoneEntry.Text = p.Phone;
        PassportEntry.Text = p.Passport;
        AccountEntry.Text = p.Account;

        foreach (var f in _fields)
            ApplyValidation(f);

        UpdateProgress(animated: false);
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

        string Get(ProfileField field)
            => ProfileRules.Normalize(field, _fields.First(f => f.Field == field).Entry.Text);

        var profile = new ProfileData(
            FullName: Get(ProfileField.FullName),
            OrgName: Get(ProfileField.OrgName),
            Unp: Get(ProfileField.Unp),
            Address: Get(ProfileField.Address),
            Phone: Get(ProfileField.Phone),
            Passport: Get(ProfileField.Passport),
            Account: Get(ProfileField.Account));

        try {
            await ProfileStorage.SaveAsync(profile);

            // Показываем значения в том виде, в котором они сохранились
            PassportEntry.Text = profile.Passport;
            AccountEntry.Text = profile.Account;

            await DisplayAlertAsync("✅ Готово", "Профиль сохранён на этом устройстве.", "Понятно");
        } catch (Exception ex) {
            await DisplayAlertAsync("Не удалось сохранить", ex.Message, "Понятно");
        }
    }

    private async void OnPassportScanTapped(object? sender, TappedEventArgs e)
    {
        await Stubs.ShowAsync("passport");
    }

    // ===== Проверка поля: красная рамка + подпись =====
    // Возвращает true, если ошибки нет (поле пустое или заполнено верно)
    private bool ApplyValidation(FieldUi f)
    {
        string? error = ProfileRules.Validate(f.Field, f.Entry.Text);

        if (error is null) {
            f.Box.Stroke = f.NormalStroke;   // ← вернуть нормальный brush
            f.Box.StrokeThickness = 1;
            f.Error.IsVisible = false;
            return true;
        }

        f.Box.Stroke = new SolidColorBrush(ErrorColor);   // красный — временно
        f.Box.StrokeThickness = 1.5;
        f.Error.Text = error;
        f.Error.IsVisible = true;
        return false;
    }

    // ===== Прогресс заполнения =====
    // Засчитываются только заполненные И верные поля
    private void UpdateProgress(bool animated)
    {
        var summary = ProfileRules.Summarize(_fields.Select(f => f.Entry.Text).ToList());

        ProgressTitle.Text = $"Заполнено на {summary.Percent}%";
        ProgressHint.Text = summary.Hint;

        if (animated)
            _ = FillProgress.ProgressTo(summary.Progress, 250, Easing.CubicOut);
        else
            FillProgress.Progress = summary.Progress;
    }
}