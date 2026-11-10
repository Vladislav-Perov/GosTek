/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Локальное маскирование персональных данных перед отправкой в облачный ИИ
 * СОДЕРЖИМОЕ: PrivacyMasker
 * ЗАВИСИМОСТИ: System.Text.RegularExpressions, GosTek.ProfileData
 * ==============================================================================
 */
using System.Text.RegularExpressions;
using GosTek;

namespace GosTek.Services;

/// <summary>
/// Сервис локального маскирования персональных данных (ПДн) граждан и реквизитов РБ.
/// Заменяет паспортные данные, телефоны, банковские счета IBAN, УНП и ФИО
/// на безопасные нумерованные плейсхолдеры ([ФИО_1], [ПАСПОРТ_1]),
/// выполняет нечеткий поиск опечаток (расстояние Левенштейна) по профилю
/// и выявляет подозрительные одиночные имена.
/// </summary>
public static class PrivacyMasker
{
    public const string MaskPassport = "[ПАСПОРТ]";
    public const string MaskPhone = "[ТЕЛЕФОН]";
    public const string MaskAccount = "[СЧЕТ]";
    public const string MaskUnp = "[УНП]";
    public const string MaskFullName = "[ФИО]";

    // 1. Номер счета IBAN (BY + 2 цифры + 4 знака кода банка + 4 знака + 16 знаков счета = 28 символов)
    private static readonly Regex IbanPattern = new(
        @"\bBY\d{2}[\s]?[A-Za-z0-9]{4}(?:[\s]?[A-Za-z0-9]{4}){5}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 2. Номера телефонов РБ (+375... или 80... с кодами и разделителями)
    private static readonly Regex PhonePattern = new(
        @"(?:\+375|80)[\s\-]?(?:\(\d{2}\)|\d{2})?[\s\-]?\d{3}[\s\-]?\d{2}[\s\-]?\d{2}\b",
        RegexOptions.Compiled);

    // 3. Паспорта РБ (2 буквы латиницы или кириллицы + пробел/дефис + 7 цифр)
    private static readonly Regex PassportPattern = new(
        @"\b[A-Za-zА-Яа-яЁё]{2}[\s\-]?[0-9]{7}\b",
        RegexOptions.Compiled);

    // 4. УНП РБ (строго 9 цифр подряд отдельным словом)
    private static readonly Regex UnpPattern = new(
        @"\b\d{9}\b",
        RegexOptions.Compiled);

    // 5. ФИО с отчеством (наиболее специфичный паттерн: Иванов Иван Иванович, Петров-Водкин Кузьма Сергеевич)
    private static readonly Regex FullNameWithPatronymicPattern = new(
        @"\b[А-ЯЁ][а-яё]+(?:-[А-ЯЁа-яё][а-яё]+)?\s+[А-ЯЁ][а-яё]+(?:-[А-ЯЁа-яё][а-яё]+)?\s+[А-ЯЁ][а-яё]*(?:вич|вна|ич|чна|кызы|оглы)\b",
        RegexOptions.Compiled);

    // 6. Общие ФИО (три последовательных слова с заглавной буквы)
    private static readonly Regex FullNameGeneralPattern = new(
        @"\b[А-ЯЁ][а-яё]+(?:-[А-ЯЁа-яё][а-яё]+)?\s+[А-ЯЁ][а-яё]+(?:-[А-ЯЁа-яё][а-яё]+)?\s+[А-ЯЁ][а-яё]+\b",
        RegexOptions.Compiled);

    // 7. Популярные имена для выявления подозрительных одиночных имён без фамилии
    private static readonly HashSet<string> CommonFirstNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Александр", "Алексей", "Андрей", "Антон", "Артем", "Артём", "Артур", "Богдан", "Вадим",
        "Валентин", "Валерий", "Василий", "Виктор", "Виталий", "Владислав", "Владимир", "Вячеслав",
        "Георгий", "Глеб", "Григорий", "Даниил", "Данил", "Данила", "Денис", "Дмитрий", "Евгений",
        "Егор", "Иван", "Игорь", "Илья", "Кирилл", "Константин", "Лев", "Леонид", "Максим",
        "Матвей", "Михаил", "Никита", "Николай", "Олег", "Павел", "Петр", "Пётр", "Роман",
        "Руслан", "Сергей", "Станислав", "Степан", "Тимофей", "Тимур", "Федор", "Фёдор", "Юрий",
        "Ярослав", "Алеся", "Алена", "Алёна", "Алиса", "Анастасия", "Анна", "Валерия", "Варвара",
        "Вера", "Вероника", "Виктория", "Дарья", "Диана", "Екатерина", "Елена", "Елизавета",
        "Жанна", "Инна", "Ирина", "Карина", "Кристина", "Ксения", "Лариса", "Любовь", "Людмила",
        "Марина", "Мария", "Надежда", "Наталья", "Нелли", "Нина", "Оксана", "Олеся", "Ольга",
        "Полина", "Светлана", "Снежана", "София", "Софья", "Тамара", "Татьяна", "Ульяна", "Юлия", "Яна"
    };

    /// <summary>
    /// Вычисляет редакционное расстояние Левенштейна между двумя строками.
    /// </summary>
    public static int LevenshteinDistance(string? s, string? t)
    {
        if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int n = s.Length;
        int m = t.Length;
        var d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = char.ToLowerInvariant(s[i - 1]) == char.ToLowerInvariant(t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    /// <summary>
    /// Маскирует персональные данные в тексте. По умолчанию не нумерует для обратной совместимости.
    /// </summary>
    public static string Mask(string? text, bool numbered = false)
    {
        return Mask(text, out _, out _, profile: null, numbered: numbered);
    }

    /// <summary>
    /// Маскирует персональные данные и сохраняет словарь извлеченных оригинальных значений.
    /// </summary>
    public static string Mask(string? text, out Dictionary<string, string> extractedValues)
    {
        return Mask(text, out extractedValues, out _, profile: null, numbered: false);
    }

    /// <summary>
    /// Маскирует персональные данные с поддержкой нумерованных плейсхолдеров и данных профиля.
    /// </summary>
    public static string Mask(
        string? text,
        out Dictionary<string, string> extractedValues,
        ProfileData? profile,
        bool numbered = true)
    {
        return Mask(text, out extractedValues, out _, profile, numbered);
    }

    /// <summary>
    /// Полный метод маскирования: нумерованные плейсхолдеры, нечеткий поиск опечаток по профилю (Левенштейн)
    /// и выявление подозрительных одиночных имён без фамилии.
    /// </summary>
    public static string Mask(
        string? text,
        out Dictionary<string, string> extractedValues,
        out List<string> suspiciousNames,
        ProfileData? profile = null,
        bool numbered = false)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var suspicious = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            extractedValues = values;
            suspiciousNames = suspicious;
            return string.Empty;
        }

        int accountCount = 0;
        int phoneCount = 0;
        int passportCount = 0;
        int unpCount = 0;
        int fioCount = 0;

        string GetPlaceholder(string prefix, ref int counter)
        {
            counter++;
            return numbered ? $"[{prefix}_{counter}]" : $"[{prefix}]";
        }

        // Шаг 1. Маскируем банковские счета IBAN
        var result = IbanPattern.Replace(text, match =>
        {
            var ph = GetPlaceholder("СЧЕТ", ref accountCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskAccount))
                values[MaskAccount] = match.Value;
            return ph;
        });

        // Шаг 2. Маскируем телефонные номера (+375 / 80)
        result = PhonePattern.Replace(result, match =>
        {
            var ph = GetPlaceholder("ТЕЛЕФОН", ref phoneCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskPhone))
                values[MaskPhone] = match.Value;
            return ph;
        });

        // Шаг 3. Маскируем паспорта (2 буквы + 7 цифр)
        result = PassportPattern.Replace(result, match =>
        {
            var ph = GetPlaceholder("ПАСПОРТ", ref passportCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskPassport))
                values[MaskPassport] = match.Value;
            return ph;
        });

        // Шаг 4. Маскируем УНП (9 цифр)
        result = UnpPattern.Replace(result, match =>
        {
            var ph = GetPlaceholder("УНП", ref unpCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskUnp))
                values[MaskUnp] = match.Value;
            return ph;
        });

        // Шаг 5. Маскируем ФИО с характерными отчествами (наиболее специфичные)
        result = FullNameWithPatronymicPattern.Replace(result, match =>
        {
            var ph = GetPlaceholder("ФИО", ref fioCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskFullName))
                values[MaskFullName] = match.Value;
            return ph;
        });

        // Шаг 6. Маскируем любые оставшиеся три слова с заглавной буквы
        result = FullNameGeneralPattern.Replace(result, match =>
        {
            var ph = GetPlaceholder("ФИО", ref fioCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskFullName))
                values[MaskFullName] = match.Value;
            return ph;
        });

        // Шаг 7. Нечеткий поиск опечаток по профилю пользователя (расстояние Левенштейна)
        if (profile != null && !string.IsNullOrWhiteSpace(profile.FullName))
        {
            var profileTokens = profile.FullName
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 4)
                .ToList();

            if (profileTokens.Count > 0)
            {
                var wordMatches = Regex.Matches(result, @"\b\p{L}{4,}\b");
                foreach (Match m in wordMatches)
                {
                    var word = m.Value;
                    foreach (var pToken in profileTokens)
                    {
                        int maxDistance = pToken.Length <= 6 ? 1 : 2;
                        int dist = LevenshteinDistance(word, pToken);

                        if (dist <= maxDistance && !string.Equals(word, pToken, StringComparison.OrdinalIgnoreCase))
                        {
                            // Найдена опечатка в имени/фамилии из профиля
                            var ph = GetPlaceholder("ФИО", ref fioCount);
                            values[ph] = word;
                            if (!values.ContainsKey(MaskFullName))
                                values[MaskFullName] = word;

                            // Заменяем конкретное вхождение слова
                            var regexWord = new Regex(@"\b" + Regex.Escape(word) + @"\b");
                            result = regexWord.Replace(result, ph, 1);
                            break;
                        }
                    }
                }
            }
        }

        // Шаг 8. Одиночные подозрительные имена без фамилии
        var singleWords = Regex.Matches(result, @"\b[А-ЯЁ][а-яё]{2,}\b");
        var profileName = profile?.FullName ?? "";

        foreach (Match m in singleWords)
        {
            var candidateName = m.Value;
            if (CommonFirstNames.Contains(candidateName))
            {
                // Если этого имени нет в профиле пользователя — это подозрительное имя третьего лица
                if (!profileName.Contains(candidateName, StringComparison.OrdinalIgnoreCase))
                {
                    suspicious.Add(candidateName);
                    var ph = GetPlaceholder("ФИО", ref fioCount);
                    values[ph] = candidateName;
                    if (!values.ContainsKey(MaskFullName))
                        values[MaskFullName] = candidateName;

                    var regexWord = new Regex(@"\b" + Regex.Escape(candidateName) + @"\b");
                    result = regexWord.Replace(result, ph, 1);
                }
            }
        }

        extractedValues = values;
        suspiciousNames = suspicious;
        return result;
    }
}
