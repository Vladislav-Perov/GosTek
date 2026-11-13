/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Парсер машиносчитываемой зоны (MRZ) паспортов РБ с контрольными суммами 7-3-1
 * СОДЕРЖИМОЕ: MrzParserService, MrzParseResult
 * ЗАВИСИМОСТИ: System, System.Text.RegularExpressions, GosTek.Services.MrzSanitizer, GosTek.Services.TransliterationHelper
 * ==============================================================================
 */
using System.Globalization;
using System.Text.RegularExpressions;

namespace GosTek.Services;

/// <summary>
/// Результат парсинга паспорта РБ (MRZ + визуальная зона).
/// </summary>
public sealed record MrzParseResult(
    string? Surname,
    string? Name,
    string? SurnameCyrillic,
    string? NameCyrillic,
    string? PassportNumber,
    string? PersonalNumber,
    DateTime? BirthDate,
    string? BirthDateString,
    string? Sex,
    DateTime? ExpiryDate,
    string? ExpiryDateString,
    bool Success = true
);

/// <summary>
/// Сервис парсинга MRZ и данных паспортов РБ с автоисправлением оптических искажений.
/// </summary>
public static class MrzParserService
{
    private static readonly int[] Weights = { 7, 3, 1 };

    // Регулярное выражение для поиска личного номера РБ при сбитых индексах
    private static readonly Regex PersonalNumberFallbackRegex =
        new(@"[0-9OIQ]{7}[A-Z][0-9OIQ]{3}[A-Z]{2}[0-9OIQ]", RegexOptions.Compiled);

    // Поиск даты в формате ДД ММ ГГГГ или ДД.ММ.ГГГГ
    private static readonly Regex VisualDateRegex =
        new(@"\b(\d{2})[\s\.\-](\d{2})[\s\.\-](\d{4})\b", RegexOptions.Compiled);

    // Поиск фамилии и имени латиницей (например SAYAPIN ANDRE1)
    private static readonly Regex LatinNameRegex =
        new(@"\b([A-Z]{3,})\s+([A-Z0-9]{3,})\b", RegexOptions.Compiled);

    /// <summary>
    /// Вычисляет контрольную цифру по алгоритму ICAO 9303 (веса 7-3-1).
    /// Символ '&lt;' считается как 0.
    /// </summary>
    public static int CalculateCheckDigit(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        int sum = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = char.ToUpperInvariant(value[i]);
            int charValue;

            if (c >= '0' && c <= '9')
                charValue = c - '0';
            else if (c >= 'A' && c <= 'Z')
                charValue = c - 'A' + 10;
            else
                charValue = 0; // '<' и пробелы = 0

            sum += charValue * Weights[i % 3];
        }

        return sum % 10;
    }

    /// <summary>
    /// Проверяет контрольную цифру. При несовпадении предпринимает до 3 попыток автозамены гомоглифов.
    /// Если сумма не сошлась — не возвращает null, возвращает false.
    /// </summary>
    public static bool VerifyCheckDigit(string value, char checkChar, out string correctedValue)
    {
        correctedValue = value;
        int expected = CalculateCheckDigit(value);
        int actual = char.IsDigit(checkChar) ? (checkChar - '0') : CalculateCheckDigit(checkChar.ToString());

        if (expected == actual)
            return true;

        // Попытки автозамены распространённых гомоглифов (до 3 вариантов)
        char[][] substitutions =
        {
            new[] { 'O', '0' },
            new[] { 'I', '1' },
            new[] { 'Z', '2' },
            new[] { 'B', '8' },
            new[] { 'S', '5' }
        };

        int attempts = 0;
        foreach (var sub in substitutions)
        {
            if (value.Contains(sub[0]))
            {
                var candidate = value.Replace(sub[0], sub[1]);
                attempts++;
                if (CalculateCheckDigit(candidate) == actual)
                {
                    correctedValue = candidate;
                    return true;
                }
            }
            if (attempts >= 3)
                break;
        }

        return false;
    }

    /// <summary>
    /// Основной метод парсинга паспорта РБ из OCR-текста.
    /// </summary>
    public static MrzParseResult Parse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new MrzParseResult(null, null, null, null, null, null, null, null, null, null, null, false);

        string? surname = null;
        string? name = null;
        string? passportNumber = null;
        string? personalNumber = null;
        DateTime? birthDate = null;
        string? birthDateStr = null;
        string? sex = null;
        DateTime? expiryDate = null;
        string? expiryDateStr = null;

        // 1. Поиск MRZ
        var (line1, line2) = FindMrzLines(rawText);

        // 2. Обработка первой строки MRZ (если найдена)
        if (!string.IsNullOrEmpty(line1))
        {
            string cleanLine1 = MrzSanitizer.Sanitize(line1);
            ParseLine1(cleanLine1, ref surname, ref name);
        }

        // 3. Обработка второй строки MRZ (если найдена)
        if (!string.IsNullOrEmpty(line2))
        {
            string cleanLine2 = MrzSanitizer.Sanitize(line2);
            ParseLine2(cleanLine2, rawText, ref passportNumber, ref personalNumber, ref birthDate, ref birthDateStr, ref sex, ref expiryDate, ref expiryDateStr);
        }

        // 4. Доизвлечение личного номера при сбоях (Fallback)
        if (string.IsNullOrEmpty(personalNumber))
        {
            personalNumber = ExtractPersonalNumberFallback(rawText);
        }

        // 5. Доизвлечение номера паспорта из текста, если в MRZ не найден или частичный
        if (string.IsNullOrEmpty(passportNumber))
        {
            passportNumber = ExtractPassportNumberFallback(rawText);
        }

        // 6. Доизвлечение ФИО из визуальной зоны, если MRZ не дал результатов
        if (string.IsNullOrEmpty(surname) || string.IsNullOrEmpty(name))
        {
            ExtractVisualNames(rawText, ref surname, ref name);
        }

        // 7. Доизвлечение даты рождения из визуальной зоны или личного номера
        if (!birthDate.HasValue)
        {
            ExtractBirthDate(rawText, personalNumber, ref birthDate, ref birthDateStr);
        }

        // 8. Транслитерация фамилии и имени
        string? surnameCyr = !string.IsNullOrEmpty(surname) ? TransliterationHelper.TransliterateSurname(surname) : null;
        string? nameCyr = !string.IsNullOrEmpty(name) ? TransliterationHelper.TransliterateName(name) : null;

        bool success = !string.IsNullOrEmpty(passportNumber) || !string.IsNullOrEmpty(personalNumber) || !string.IsNullOrEmpty(surname);

        return new MrzParseResult(
            Surname: surname,
            Name: name,
            SurnameCyrillic: surnameCyr,
            NameCyrillic: nameCyr,
            PassportNumber: passportNumber,
            PersonalNumber: personalNumber,
            BirthDate: birthDate,
            BirthDateString: birthDateStr,
            Sex: sex,
            ExpiryDate: expiryDate,
            ExpiryDateString: expiryDateStr,
            Success: success
        );
    }

    /// <summary>
    /// Ищет строки MRZ в зашумленном тексте.
    /// </summary>
    private static (string? Line1, string? Line2) FindMrzLines(string rawText)
    {
        string? line1 = null;
        string? line2 = null;

        // Поиск первой строки: ищем P<BLR или цепочку <<<<
        var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i];
            if (l.Contains("P<BLR") || l.Contains("<<<<"))
            {
                line1 = l;
                // Вторая строка MRZ обычно идет следом
                if (i + 1 < lines.Length)
                {
                    line2 = lines[i + 1];
                }
                break;
            }
        }

        // Если вторая строка не найдена по порядку строк, ищем по BLR на 10-й позиции
        if (string.IsNullOrEmpty(line2))
        {
            line2 = FindLine2ByBlr(rawText, line1);
        }

        return (line1, line2);
    }

    /// <summary>
    /// Ищет вторую строку MRZ по коду страны BLR с отступом 10 символов влево от B.
    /// </summary>
    private static string? FindLine2ByBlr(string rawText, string? line1)
    {
        // Ищем вхождение BLR, перед которым есть серия/номер паспорта
        int searchIndex = 0;
        while (searchIndex < rawText.Length)
        {
            int blrIndex = rawText.IndexOf("BLR", searchIndex, StringComparison.OrdinalIgnoreCase);
            if (blrIndex == -1)
                break;

            // Пропускаем BLR, который является частью REPUBLIC OF BLR или P<BLR
            if (blrIndex >= 2 && rawText.Substring(blrIndex - 2, 2) == "P<")
            {
                searchIndex = blrIndex + 3;
                continue;
            }
            if (blrIndex >= 3 && rawText.Substring(blrIndex - 3, 3).Equals("OF ", StringComparison.OrdinalIgnoreCase))
            {
                searchIndex = blrIndex + 3;
                continue;
            }

            // Отсчитываем начало строки: строго 10 символов влево от 'B' без учета пробелов
            // Сначала берем подстроку слева и удаляем пробелы
            int leftLookback = Math.Min(blrIndex, 25);
            string leftSnippet = rawText.Substring(blrIndex - leftLookback, leftLookback);
            string cleanLeft = leftSnippet.Replace(" ", "");

            if (cleanLeft.Length >= 10)
            {
                // Нашли 10 символов слева от BLR
                // Берем фрагмент текста от начала найденного блока
                int rightLookahead = Math.Min(rawText.Length - blrIndex, 45);
                string rawCandidate = rawText.Substring(blrIndex - leftLookback, leftLookback + rightLookahead);
                return rawCandidate;
            }

            searchIndex = blrIndex + 3;
        }

        // Критичный fallback: если BLR не найдено, взять 44 символа сразу после первой строки
        if (!string.IsNullOrEmpty(line1))
        {
            int idx = rawText.IndexOf(line1, StringComparison.Ordinal);
            if (idx != -1 && idx + line1.Length < rawText.Length)
            {
                string rest = rawText.Substring(idx + line1.Length).TrimStart();
                if (rest.Length >= 44)
                    return rest.Substring(0, 44);
            }
        }

        return null;
    }

    /// <summary>
    /// Парсинг первой строки MRZ (ФИО латиницей).
    /// </summary>
    private static void ParseLine1(string cleanLine1, ref string? surname, ref string? name)
    {
        int pIndex = cleanLine1.IndexOf("P<BLR", StringComparison.Ordinal);
        string payload = pIndex != -1 ? cleanLine1.Substring(pIndex + 5) : cleanLine1;

        int delim = payload.IndexOf("<<", StringComparison.Ordinal);
        if (delim != -1)
        {
            surname = payload.Substring(0, delim).Replace("<", "").Trim();
            string rest = payload.Substring(delim + 2);
            int endName = rest.IndexOf('<');
            name = endName != -1 ? rest.Substring(0, endName).Trim() : rest.Trim();
        }
    }

    /// <summary>
    /// Парсинг второй строки MRZ.
    /// </summary>
    private static void ParseLine2(
        string cleanLine2,
        string fullRawText,
        ref string? passportNumber,
        ref string? personalNumber,
        ref DateTime? birthDate,
        ref string? birthDateStr,
        ref string? sex,
        ref DateTime? expiryDate,
        ref string? expiryDateStr)
    {
        // Выравниваем строку так, чтобы BLR оказалось на позициях 10-12
        int blrPos = cleanLine2.IndexOf("BLR", StringComparison.Ordinal);
        string alignedLine = cleanLine2;
        if (blrPos >= 10)
        {
            alignedLine = cleanLine2.Substring(blrPos - 10);
        }

        // 1. Номер паспорта (позиции 0..8): 9 символов
        if (alignedLine.Length >= 9)
        {
            string rawDoc = alignedLine.Substring(0, 9);
            // Первые 2 символа — серия (латиница)
            string series = rawDoc.Substring(0, 2);
            // Оставшиеся 7 символов — цифры номера
            string numPart = rawDoc.Substring(2);
            // Применяем SanitizeNumbers ТОЛЬКО к цифровой части!
            string cleanNum = MrzSanitizer.SanitizeNumbers(numPart);
            passportNumber = $"{series}{cleanNum}";
        }

        // 2. Личный номер (позиции 28..41): 14 символов
        // ⚠️ КРИТИЧНО: извлекаем через Substring(28, 14) ИСКЛЮЧИТЕЛЬНО если длина >= 42
        if (alignedLine.Length >= 42)
        {
            string candidate = alignedLine.Substring(28, 14);
            // Проверяем валидность формата личного номера: [1-6][0-9]{6}[A-Z][0-9]{3}[A-Z]{2}[0-9]
            if (IsValidPersonalNumberFormat(candidate))
            {
                personalNumber = candidate;
            }
        }

        // 3. Если личный номер не извлекся по фиксированным индексам — запускаем Fallback
        if (string.IsNullOrEmpty(personalNumber))
        {
            personalNumber = ExtractPersonalNumberFallback(alignedLine)
                          ?? ExtractPersonalNumberFallback(fullRawText);
        }

        // 4. Дата рождения (позиции 13..18)
        if (alignedLine.Length >= 19)
        {
            string rawBirth = alignedLine.Substring(13, 6);
            string cleanBirth = MrzSanitizer.SanitizeNumbers(rawBirth);
            if (cleanBirth.Length == 6 && cleanBirth.All(char.IsDigit))
            {
                if (DateTime.TryParseExact(cleanBirth, "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    // Коррекция века для YYMMDD: если дата в будущем, вычитаем 100 лет
                    if (dt > DateTime.Today)
                        dt = dt.AddYears(-100);

                    birthDate = dt;
                    birthDateStr = dt.ToString("dd.MM.yyyy");
                }
            }
        }

        // 5. Пол (позиция 20)
        if (alignedLine.Length >= 21)
        {
            char sexChar = alignedLine[20];
            if (sexChar == 'M' || sexChar == 'М') sex = "M";
            else if (sexChar == 'F') sex = "F";
        }

        // 6. Срок действия (позиции 21..26)
        if (alignedLine.Length >= 27)
        {
            string rawExp = alignedLine.Substring(21, 6);
            string cleanExp = MrzSanitizer.SanitizeNumbers(rawExp);
            if (cleanExp.Length == 6 && cleanExp.All(char.IsDigit))
            {
                if (DateTime.TryParseExact(cleanExp, "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    expiryDate = dt;
                    expiryDateStr = dt.ToString("dd.MM.yyyy");
                }
            }
        }
    }

    /// <summary>
    /// Проверяет базовый формат личного номера гражданина РБ:
    /// 7 цифр + 1 буква + 3 цифры + 2 буквы + 1 цифра.
    /// </summary>
    private static bool IsValidPersonalNumberFormat(string pn)
    {
        if (string.IsNullOrEmpty(pn) || pn.Length != 14)
            return false;

        // Позиция 0: цифра 1-6
        if (pn[0] < '1' || pn[0] > '6')
            return false;

        // Позиции 1-6: цифры
        for (int i = 1; i <= 6; i++)
            if (!char.IsDigit(pn[i])) return false;

        // Позиция 7: буква (A-Z)
        if (!char.IsLetter(pn[7])) return false;

        // Позиции 8-10: цифры
        for (int i = 8; i <= 10; i++)
            if (!char.IsDigit(pn[i])) return false;

        // Позиции 11-12: буквы
        if (!char.IsLetter(pn[11]) || !char.IsLetter(pn[12])) return false;

        // Позиция 13: цифра
        if (!char.IsDigit(pn[13])) return false;

        return true;
    }

    /// <summary>
    /// Извлечение личного номера РБ через гибкую регулярку с учетом путаницы OCR.
    /// ⚠️ ВАЖНО: прогоняем через Sanitize (кириллица -> латиница), но НЕ через SanitizeNumbers!
    /// </summary>
    public static string? ExtractPersonalNumberFallback(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        // Удаляем пробелы и переводим кириллические буквы в латинские
        string sanitized = MrzSanitizer.Sanitize(text);

        var match = PersonalNumberFallbackRegex.Match(sanitized);
        if (!match.Success)
            return null;

        string raw = match.Value;
        if (raw.Length != 14)
            return null;

        var sb = new System.Text.StringBuilder(14);
        for (int i = 0; i < 14; i++)
        {
            char c = raw[i];
            if (i == 7 || i == 11 || i == 12)
            {
                // Позиции для букв (A-Z)
                sb.Append(char.ToUpperInvariant(c));
            }
            else
            {
                // Цифровые позиции: исправляем O, I, Q
                if (c == 'O' || c == 'o' || c == 'Q' || c == 'q') sb.Append('0');
                else if (c == 'I' || c == 'i' || c == 'l') sb.Append('1');
                else sb.Append(c);
            }
        }

        string result = sb.ToString();
        return IsValidPersonalNumberFormat(result) ? result : null;
    }

    /// <summary>
    /// Резервное извлечение номера паспорта из текста (визуальной зоны или фрагмента MRZ).
    /// </summary>
    private static string? ExtractPassportNumberFallback(string rawText)
    {
        // Ищем паттерн серии и номера: 2 буквы + до 8 цифр/гомоглифов
        // Например: МРГ41 78795 или MP 2417879
        var regex = new Regex(@"([A-ZА-Я]{2})\s*([0-9ZГЗЧSBБВOIQ]{7,8})", RegexOptions.IgnoreCase);
        var match = regex.Match(rawText);
        if (match.Success)
        {
            string rawSeries = match.Groups[1].Value.ToUpperInvariant();
            // Кириллические буквы серии в латинские (МР -> MP)
            string series = rawSeries
                .Replace('М', 'M')
                .Replace('Р', 'P')
                .Replace('В', 'B')
                .Replace('А', 'A')
                .Replace('К', 'K')
                .Replace('Н', 'H');

            string numPart = match.Groups[2].Value;
            // Применяем SanitizeNumbers только к цифровой части
            string cleanNum = MrzSanitizer.SanitizeNumbers(numPart);
            if (cleanNum.Length > 7)
                cleanNum = cleanNum.Substring(0, 7);

            return $"{series}{cleanNum}";
        }

        return null;
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "REPUBLIC", "REPUBUC", "BELARUS", "MINISTRY", "MLNISTRY", "INTERNAL", "INJERNAL", "AFFAIRS", "AFFA1RS", "AFFALRS", "BLR", "PASSPORT", "IDENTIFICATION", "CARD", "OF", "STATE", "DOCUMENT"
    };

    /// <summary>
    /// Извлечение ФИО из визуальной зоны.
    /// </summary>
    private static void ExtractVisualNames(string rawText, ref string? surname, ref string? name)
    {
        // Очищаем от апострофов и спецсимволов OCR внутри слов вроде REl'L'BLfC
        string normalizedText = rawText.Replace("'", "").Replace("`", "");

        // Извлекаем слова длиной от 3 символов
        var matches = Regex.Matches(normalizedText, @"\b[A-Za-z0-9]{3,}\b");
        var words = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            words.Add(m.Value.ToUpperInvariant());
        }

        // 1. Приоритетный поиск: имя из словаря (например ANDREI / ANDRE1)
        for (int i = 0; i < words.Count - 1; i++)
        {
            string w1 = words[i];
            string w2 = words[i + 1];

            if (StopWords.Contains(w1))
                continue;

            if (w1.All(char.IsLetter) && TransliterationHelper.IsKnownName(w2))
            {
                surname = w1;
                name = w2.Replace('1', 'I').Replace('0', 'O');
                return;
            }
        }

        // 2. Поиск после маркеров страны (OF BLR / BELARUS)
        int markerIdx = -1;
        for (int i = 0; i < words.Count; i++)
        {
            if (words[i] == "BLR" || words[i] == "BELARUS")
            {
                markerIdx = i;
                break;
            }
        }

        int startSearch = markerIdx != -1 ? markerIdx + 1 : 0;
        for (int i = startSearch; i < words.Count - 1; i++)
        {
            string w1 = words[i];
            string w2 = words[i + 1];

            if (StopWords.Contains(w1) || StopWords.Contains(w2))
                continue;

            if (!w1.All(char.IsLetter))
                continue;

            string candidateName = w2.Replace('1', 'I').Replace('0', 'O');
            if (candidateName.All(char.IsLetter))
            {
                surname = w1;
                name = candidateName;
                return;
            }
        }
    }

    /// <summary>
    /// Извлечение даты рождения из визуальной зоны (например 26 04 1983) или из личного номера.
    /// </summary>
    private static void ExtractBirthDate(string rawText, string? personalNumber, ref DateTime? birthDate, ref string? birthDateStr)
    {
        // 1. Попытка извлечь из визуальной зоны (26 04 1983)
        var match = VisualDateRegex.Match(rawText);
        if (match.Success)
        {
            string day = match.Groups[1].Value;
            string month = match.Groups[2].Value;
            string year = match.Groups[3].Value;
            if (DateTime.TryParseExact($"{day}.{month}.{year}", "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                birthDate = dt;
                birthDateStr = $"{day}.{month}.{year}";
                return;
            }
        }

        // 2. Попытка извлечь из личного номера гражданина РБ (например 3260483A011PB6)
        if (!string.IsNullOrEmpty(personalNumber) && personalNumber.Length == 14)
        {
            char centuryChar = personalNumber[0];
            string dayStr = personalNumber.Substring(1, 2);
            string monthStr = personalNumber.Substring(3, 2);
            string yearStr = personalNumber.Substring(5, 2);

            int century = centuryChar switch
            {
                '1' or '2' => 1800,
                '3' or '4' => 1900,
                '5' or '6' => 2000,
                _ => 1900
            };

            if (int.TryParse(dayStr, out int d) && int.TryParse(monthStr, out int m) && int.TryParse(yearStr, out int y))
            {
                int fullYear = century + y;
                try
                {
                    var dt = new DateTime(fullYear, m, d);
                    birthDate = dt;
                    birthDateStr = dt.ToString("dd.MM.yyyy");
                }
                catch
                {
                    // Некорректная дата
                }
            }
        }
    }
}
