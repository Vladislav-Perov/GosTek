/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Локальное маскирование персональных данных перед отправкой в облачный ИИ
 * СОДЕРЖИМОЕ: PrivacyMasker
 * ЗАВИСИМОСТИ: System.Text.RegularExpressions, GosTek.ProfileData, GosTek.SettingsService
 * ==============================================================================
 */
using System.Text.RegularExpressions;
using GosTek;

namespace GosTek.Services;

/// <summary>
/// Сервис локального маскирования персональных данных (ПДн) граждан и реквизитов РБ.
/// Заменяет паспортные данные, телефоны, банковские счета IBAN, УНП, белорусские адреса,
/// адреса email и ФИО во всех падежах на безопасные нумерованные плейсхолдеры ([ФИО_1], [ПАСПОРТ_1]),
/// выполняет нечеткий поиск опечаток с адаптивным расстоянием Левенштейна по профилю
/// и выявляет неоднозначные одиночные имена.
/// </summary>
public static class PrivacyMasker
{
    public const string MaskPassport = "[ПАСПОРТ]";
    public const string MaskPhone = "[ТЕЛЕФОН]";
    public const string MaskAccount = "[СЧЕТ]";
    public const string MaskUnp = "[УНП]";
    public const string MaskFullName = "[ФИО]";
    public const string MaskAddress = "[АДРЕС]";
    public const string MaskEmail = "[EMAIL]";

    /// <summary>
    /// Автоматически маскировать одиночные подозрительные имена без подтверждения (по умолчанию true).
    /// </summary>
    public static bool MaskAmbiguousSilent { get; set; } = true;

    // 1. Номер счета IBAN (BY + 2 цифры + 4 знака кода банка + 4 знака + 16 знаков счета = 28 символов)
    private static readonly Regex IbanPattern = new(
        @"\bBY\d{2}[\s]?[A-Za-z0-9]{4}(?:[\s]?[A-Za-z0-9]{4}){5}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 2. Email-адреса
    private static readonly Regex EmailPattern = new(
        @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled);

    // 3. Номера телефонов РБ (+375... или 80... с кодами и разделителями пробел, дефис, скобки)
    private static readonly Regex PhonePattern = new(
        @"(?:\+375|80)[\s\-]?(?:\(\d{2}\)|\d{2})?[\s\-]?\d{3}[\s\-]?\d{2}[\s\-]?\d{2}\b",
        RegexOptions.Compiled);

    // 4. Паспорта РБ и идентификационный личный номер гражданина:
    //    - 14-значный личный номер: цифра 1-6 + 6 цифр даты + буква + 3 цифры + 2 буквы (PB, BA...) + цифра
    //    - Серия и номер: 2 буквы латиницы/кириллицы + пробел/дефис/№ + 7 цифр (включая форматы с группировкой 3-2-2)
    private static readonly Regex PersonalIdPattern = new(
        @"\b[1-6]\d{6}[A-Za-zА-Яа-яЁё]\d{3}(?:PB|BA|BI|[A-Za-zА-Яа-яЁё]{2})\d\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PassportPattern = new(
        @"\b[A-Za-zА-Яа-яЁё]{2}[\s\-№#]*(?:[0-9]{7}|[0-9]{3}[\s\-][0-9]{2}[\s\-][0-9]{2}|[0-9]{3}[\s\-][0-9]{4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 5. УНП РБ (строго 9 цифр подряд отдельным словом)
    private static readonly Regex UnpPattern = new(
        @"\b\d{9}\b",
        RegexOptions.Compiled);

    // 6. Белорусские адреса:
    //    - Город/область/район + улица/проспект + дом + квартира/офис
    private static readonly Regex BelarusAddressPattern = new(
        @"(?:\b(?:г\.|город|г\.п\.|пгт|пос\.|аг\.|д\.|деревня|обл\.|область|р-н|район)\s+[А-ЯЁ][а-яё\-]+|\b[А-ЯЁ][а-яё\-]+\s+(?:обл\.|область|р-н|район))" +
        @"(?:[\s,]+(?:г\.|город|г\.п\.|пос\.|аг\.|деревня|д\.|р-н|район)\s+[А-ЯЁ][а-яё\-]+)*" +
        @"(?:[\s,]+(?:ул\.|улица|пр-т|просп\.|проспект|пер\.|переулок|бульв\.|б-р|шоссе|тракт|проезд)\s+[А-ЯЁ0-9][а-яё0-9\.\-\s]+)?" +
        @"(?:[\s,]+(?:д\.|дом|корп\.|корпус|к\.|стр\.|строение)\s*[0-9]+[а-яА-ЯёЁ\/\-0-9]*)*" +
        @"(?:[\s,]+(?:кв\.|квартира|оф\.|офис|комн\.|комната)\s*[0-9]+)*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    //    - Улица/проспект с домом:
    private static readonly Regex StreetAddressPattern = new(
        @"\b(?:ул\.|улица|пр-т|просп\.|проспект|пер\.|переулок|бульв\.|б-р|шоссе|тракт|проезд)\s+[А-ЯЁ][а-яё0-9\.\-]+" +
        @"(?:[\s,]+(?:д\.|дом)\s*[0-9]+[а-яА-ЯёЁ\/\-0-9]*)" +
        @"(?:[\s,]+(?:корп\.|к\.|корпус)\s*[0-9]+)*" +
        @"(?:[\s,]+(?:кв\.|квартира|оф\.|офис)\s*[0-9]+)*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 7. ФИО с характерными отчествами ВО ВСЕХ ПАДЕЖАХ (И.п., Р.п., Д.п., В.п., Т.п., П.п.)
    //    Иванов Иван Иванович, Иванова Ивана Ивановича, Иванову Ивану Ивановичу,
    //    Ивановым Иваном Ивановичем, Петровой Анне Сергеевне, Сидоровой Анной Васильевной
    private static readonly Regex FullNameWithPatronymicPattern = new(
        @"\b[А-ЯЁ][а-яё]+(?:-[А-ЯЁа-яё][а-яё]+)?\s+[А-ЯЁ][а-яё]+(?:-[А-ЯЁа-яё][а-яё]+)?\s+[А-ЯЁ][а-яё]*(?:ович(?:[ауе]|ем)?|евич(?:[ауе]|ем)?|ич(?:[ауе]|ем)?|овн(?:[аыеу]|ой)|евн(?:[аыеу]|ой)|ичн(?:[аыеу]|ой)|иничн(?:[аыеу]|ой)|кызы|оглы)\b",
        RegexOptions.Compiled);

    // 8. Общие ФИО (три последовательных слова с заглавной буквы)
    private static readonly Regex FullNameGeneralPattern = new(
        @"\b[А-ЯЁ][а-яё]+(?:-[А-ЯЁа-яё][а-яё]+)?\s+[А-ЯЁ][а-яё]+(?:-[А-ЯЁа-яё][а-яё]+)?\s+[А-ЯЁ][а-яё]+\b",
        RegexOptions.Compiled);

    // 9. Популярные имена и их падежные основы для выявления подозрительных одиночных имён без фамилии
    private static readonly HashSet<string> CommonFirstNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Александр", "Александра", "Александру", "Александром", "Александре",
        "Алексей", "Алексея", "Алексею", "Алексеем", "Алексее",
        "Андрей", "Андрея", "Андрею", "Андреем", "Андрее",
        "Антон", "Антона", "Антону", "Антоном", "Антоне",
        "Артем", "Артём", "Артема", "Артёма", "Артему", "Артёму",
        "Артур", "Артура", "Артуру",
        "Богдан", "Богдана", "Богдану",
        "Вадим", "Вадима", "Вадиму",
        "Валентин", "Валентина", "Валерий", "Валерия", "Валерию",
        "Василий", "Василия", "Василию",
        "Виктор", "Виктора", "Виктору", "Виктором",
        "Виталий", "Виталия", "Виталию",
        "Владислав", "Владислава", "Владиславу",
        "Владимир", "Владимира", "Владимиру", "Владимиром",
        "Вячеслав", "Вячеслава", "Вячеславу",
        "Георгий", "Георгия", "Глеб", "Глеба", "Григорий", "Григория",
        "Даниил", "Даниила", "Данил", "Данила", "Денис", "Дениса", "Денису",
        "Дмитрий", "Дмитрия", "Дмитрию", "Дмитрием",
        "Евгений", "Евгения", "Евгению", "Евгением",
        "Егор", "Егора", "Егору",
        "Иван", "Ивана", "Ивану", "Иваном", "Иване",
        "Игорь", "Игоря", "Игорю", "Ильей", "Илья", "Илье", "Илью",
        "Кирилл", "Кирилла", "Кириллу",
        "Константин", "Константина", "Константину",
        "Лев", "Льва", "Леонид", "Леонида",
        "Максим", "Максима", "Максиму", "Максимом",
        "Матвей", "Матвея", "Михаил", "Михаила", "Михаилу", "Михаилом",
        "Никита", "Никиты", "Никите", "Никиту",
        "Николай", "Николая", "Николаю", "Николаем",
        "Олег", "Олега", "Олегу", "Олегом",
        "Павел", "Павла", "Павлу", "Павлом",
        "Петр", "Пётр", "Петра", "Пётра", "Петру", "Пётру", "Петром",
        "Роман", "Романа", "Роману", "Романом",
        "Руслан", "Руслана", "Руслану",
        "Сергей", "Сергея", "Сергею", "Сергеем", "Сергее",
        "Станислав", "Станислава", "Степан", "Степана",
        "Тимофей", "Тимофея", "Тимур", "Тимура",
        "Федор", "Фёдор", "Федора", "Фёдора",
        "Юрий", "Юрия", "Юрию", "Юрием",
        "Ярослав", "Ярослава",
        "Алеся", "Алеси", "Алесе", "Алесю",
        "Алена", "Алёна", "Алены", "Алёны", "Алене", "Алёне",
        "Алиса", "Алисы", "Алисе",
        "Анастасия", "Анастасии", "Анастасию",
        "Анна", "Анны", "Анне", "Анну", "Анной",
        "Валерия", "Валерии", "Варвара", "Варвары",
        "Вера", "Веры", "Вере",
        "Вероника", "Вероники", "Веронике",
        "Виктория", "Виктории", "Викторию",
        "Дарья", "Дарьи", "Дарье", "Дарью",
        "Диана", "Дианы", "Диане",
        "Екатерина", "Екатерины", "Екатерине", "Екатерину", "Екатериной",
        "Елена", "Елены", "Елене", "Елену", "Еленой",
        "Елизавета", "Елизаветы", "Елизавете",
        "Жанна", "Жанны", "Инна", "Инны",
        "Ирина", "Ирины", "Ирине", "Ирину", "Ириной",
        "Карина", "Карины", "Кристина", "Кристины", "Ксения", "Ксении",
        "Лариса", "Ларисы", "Любовь", "Любови", "Людмила", "Людмилы",
        "Марина", "Марины", "Марине", "Марину",
        "Мария", "Марии", "Марию", "Марией",
        "Надежда", "Надежды", "Надежде",
        "Наталья", "Натальи", "Наталье", "Наталью",
        "Нелли", "Нина", "Нины",
        "Оксана", "Оксаны", "Олеся", "Олеси",
        "Ольга", "Ольги", "Ольге", "Ольгу", "Ольгой",
        "Полина", "Полины", "Полине",
        "Светлана", "Светланы", "Светлане", "Светлану", "Светланой",
        "Снежана", "Снежаны",
        "София", "Софии", "Софья", "Софьи",
        "Тамара", "Тамары", "Тамаре",
        "Татьяна", "Татьяны", "Татьяне", "Татьяну", "Татьяной",
        "Ульяна", "Ульяны",
        "Юлия", "Юлии", "Юлию", "Юлией",
        "Яна", "Яны", "Яне"
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
    /// Маскирует персональные данные и сохраняет словарь извлеченных оригинальных значений с возможностью нумерации.
    /// </summary>
    public static string Mask(string? text, out Dictionary<string, string> extractedValues, bool numbered)
    {
        return Mask(text, out extractedValues, out _, profile: null, numbered: numbered);
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
    /// Полный метод маскирования: нумерованные плейсхолдеры, белорусские адреса, email,
    /// ФИО во всех падежах, адаптивный нечеткий поиск опечаток (Левенштейн) по профилю
    /// и гибкая обработка спорных одиночных имён с учетом настройки SettingsService.AiMaskAmbiguousSilent.
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
        int addressCount = 0;
        int emailCount = 0;

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

        // Шаг 2. Маскируем адреса Email
        result = EmailPattern.Replace(result, match =>
        {
            var ph = GetPlaceholder("EMAIL", ref emailCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskEmail))
                values[MaskEmail] = match.Value;
            return ph;
        });

        // Шаг 3. Маскируем телефонные номера (+375 / 80)
        result = PhonePattern.Replace(result, match =>
        {
            var ph = GetPlaceholder("ТЕЛЕФОН", ref phoneCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskPhone))
                values[MaskPhone] = match.Value;
            return ph;
        });

        // Шаг 4. Маскируем идентификационный личный номер гражданина РБ (14 знаков)
        result = PersonalIdPattern.Replace(result, match =>
        {
            var ph = GetPlaceholder("ПАСПОРТ", ref passportCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskPassport))
                values[MaskPassport] = match.Value;
            return ph;
        });

        // Шаг 5. Маскируем паспорта (2 буквы + 7 цифр, различные варианты написания)
        result = PassportPattern.Replace(result, match =>
        {
            var ph = GetPlaceholder("ПАСПОРТ", ref passportCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskPassport))
                values[MaskPassport] = match.Value;
            return ph;
        });

        // Шаг 6. Маскируем УНП (9 цифр)
        result = UnpPattern.Replace(result, match =>
        {
            var ph = GetPlaceholder("УНП", ref unpCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskUnp))
                values[MaskUnp] = match.Value;
            return ph;
        });

        // Шаг 7. Маскируем белорусские адреса (г. Минск, обл., р-н, ул., пр-т, д., кв.)
        result = BelarusAddressPattern.Replace(result, match =>
        {
            if (match.Length < 6)
                return match.Value;

            var ph = GetPlaceholder("АДРЕС", ref addressCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskAddress))
                values[MaskAddress] = match.Value;
            return ph;
        });

        result = StreetAddressPattern.Replace(result, match =>
        {
            if (match.Length < 6)
                return match.Value;

            var ph = GetPlaceholder("АДРЕС", ref addressCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskAddress))
                values[MaskAddress] = match.Value;
            return ph;
        });

        // Шаг 8. Маскируем ФИО с характерными отчествами ВО ВСЕХ ПАДЕЖАХ
        result = FullNameWithPatronymicPattern.Replace(result, match =>
        {
            var ph = GetPlaceholder("ФИО", ref fioCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskFullName))
                values[MaskFullName] = match.Value;
            return ph;
        });

        // Шаг 9. Маскируем любые оставшиеся три слова с заглавной буквы
        result = FullNameGeneralPattern.Replace(result, match =>
        {
            var ph = GetPlaceholder("ФИО", ref fioCount);
            if (!values.ContainsKey(ph))
                values[ph] = match.Value;
            if (!values.ContainsKey(MaskFullName))
                values[MaskFullName] = match.Value;
            return ph;
        });

        // Шаг 10. Адаптивный нечеткий поиск по профилю пользователя (Левенштейн)
        if (profile != null)
        {
            // Маскируем адрес из профиля, если он явно встречается
            if (!string.IsNullOrWhiteSpace(profile.Address) && profile.Address.Length >= 5)
            {
                if (result.Contains(profile.Address, StringComparison.OrdinalIgnoreCase))
                {
                    var ph = GetPlaceholder("АДРЕС", ref addressCount);
                    values[ph] = profile.Address;
                    if (!values.ContainsKey(MaskAddress))
                        values[MaskAddress] = profile.Address;
                    var reg = new Regex(Regex.Escape(profile.Address), RegexOptions.IgnoreCase);
                    result = reg.Replace(result, ph, 1);
                }
            }

            // Маскируем ФИО из профиля и слова со сходством
            if (!string.IsNullOrWhiteSpace(profile.FullName))
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
                            // Адаптивный порог: 1 для коротких слов (4-5 букв), 2 для длинных (>= 6 букв)
                            int maxDistance = pToken.Length <= 5 ? 1 : 2;
                            int dist = LevenshteinDistance(word, pToken);

                            if (dist <= maxDistance && !string.Equals(word, pToken, StringComparison.OrdinalIgnoreCase))
                            {
                                // Найдена опечатка в токене имени/фамилии из профиля
                                var ph = GetPlaceholder("ФИО", ref fioCount);
                                values[ph] = word;
                                if (!values.ContainsKey(MaskFullName))
                                    values[MaskFullName] = word;

                                var regexWord = new Regex(@"\b" + Regex.Escape(word) + @"\b");
                                result = regexWord.Replace(result, ph, 1);
                                break;
                            }
                        }
                    }
                }
            }
        }

        // Шаг 11. Одиночные подозрительные имена без фамилии
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

                    // Режим пользовательского контроля: мягкая маскировка (по умолчанию)
                    // либо ожидание подтверждения пользователем
                    if (MaskAmbiguousSilent)
                    {
                        var ph = GetPlaceholder("ФИО", ref fioCount);
                        values[ph] = candidateName;
                        if (!values.ContainsKey(MaskFullName))
                            values[MaskFullName] = candidateName;

                        var regexWord = new Regex(@"\b" + Regex.Escape(candidateName) + @"\b");
                        result = regexWord.Replace(result, ph, 1);
                    }
                }
            }
        }

        extractedValues = values;
        suspiciousNames = suspicious;
        return result;
    }
}
