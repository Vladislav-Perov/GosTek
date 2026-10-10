using System.Text.RegularExpressions;

namespace GosTek.Views;

public enum ProfileField { FullName, OrgName, Unp, Address, Phone, Passport, Account }

/// <summary>Правила профиля: проверка, приведение к виду для сохранения и сводка (страница профиля и плашка на главной).</summary>
public static class ProfileRules
{
    // Порядок полей = порядок на странице профиля
    private static readonly ProfileField[] All = Enum.GetValues<ProfileField>();

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

    public static string Get(ProfileData p, ProfileField field) => field switch {
        ProfileField.FullName => p.FullName,
        ProfileField.OrgName => p.OrgName,
        ProfileField.Unp => p.Unp,
        ProfileField.Address => p.Address,
        ProfileField.Phone => p.Phone,
        ProfileField.Passport => p.Passport,
        ProfileField.Account => p.Account,
        _ => "",
    };

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
        if (!PhoneChars.IsMatch(v))
            return "Допустимы цифры, пробелы, +, ( ) и -";

        var digits = new string(v.Where(char.IsAsciiDigit).ToArray());

        bool by375 = digits.StartsWith("375") && digits.Length == 12;
        bool by80 = digits.StartsWith("80") && digits.Length == 11;

        return by375 || by80 ? null : "Формат: +375 29 000-00-00";
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

    // ===== Сводка: процент заполнения и подсказка =====
    // Засчитываются только заполненные И верные поля
    public static (int Percent, double Progress, string Hint) Summarize(ProfileData p)
    {
        var missing = new List<string>();
        var check = new List<string>();

        foreach (var field in All) {
            var value = Get(p, field);

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
