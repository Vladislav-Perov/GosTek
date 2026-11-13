/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Парсер машиносчитываемой зоны (MRZ) паспортов РБ с контрольными суммами 7-3-1 и Regex-фоллбэками
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
public class MrzParseResult
{
    public string? Surname { get; set; }
    public string? GivenNames { get; set; }
    public string? PersonalNumber { get; set; }
    public string? PassportNumber { get; set; }
    public string? DateOfBirth { get; set; }
    public string? Sex { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? ExpiryDateString { get; set; }

    // Алиасы для полной совместимости с DocumentDataExtractor и тестами:
    public string? Name { get => GivenNames; set => GivenNames = value; }
    public string? SurnameCyrillic => !string.IsNullOrEmpty(Surname) ? TransliterationHelper.Transliterate(Surname, false) : null;
    public string? NameCyrillic => !string.IsNullOrEmpty(GivenNames) ? TransliterationHelper.Transliterate(GivenNames, true) : null;
    public string? BirthDateString => DateOfBirth;
    public DateTime? BirthDate =>
        DateTime.TryParseExact(DateOfBirth, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt
            : null;

    public bool Success => !string.IsNullOrEmpty(PassportNumber) || !string.IsNullOrEmpty(PersonalNumber) || !string.IsNullOrEmpty(Surname);

    public static MrzParseResult Empty => new();
}

/// <summary>
/// Сервис парсинга MRZ и данных паспортов РБ с автоисправлением оптических искажений и Regex-фоллбэками.
/// </summary>
public static class MrzParserService
{
    private static readonly int[] Weights = { 7, 3, 1 };

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
    /// </summary>
    public static bool VerifyCheckDigit(string value, char checkChar, out string correctedValue)
    {
        correctedValue = value;
        int expected = CalculateCheckDigit(value);
        int actual = char.IsDigit(checkChar) ? (checkChar - '0') : CalculateCheckDigit(checkChar.ToString());

        if (expected == actual)
            return true;

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
    /// Защищен от любых сбоев через try-catch.
    /// </summary>
    public static MrzParseResult Parse(string rawOcrText)
    {
        var result = new MrzParseResult();
        if (string.IsNullOrEmpty(rawOcrText)) return result;

        try
        {
            string cleanText = rawOcrText.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            string upperText = rawOcrText.ToUpperInvariant();

            // 1. ФИО (сначала визуальная зона "BLR ФАМИЛИЯ ИМЯ", затем MRZ "P<BLR...")
            var nameMatch = Regex.Match(upperText, @"BLR\s+([A-Z]+)\s+([A-Z0-9]+)");
            if (nameMatch.Success)
            {
                string surname = nameMatch.Groups[1].Value.Replace("1", "I").Replace("0", "O");
                string name = nameMatch.Groups[2].Value.Replace("1", "I").Replace("0", "O");
                result.Surname = surname;
                result.GivenNames = name;
            }
            else
            {
                var mrzLine1 = Regex.Match(cleanText.ToUpperInvariant(), @"P<BLR([A-Z]+)<<([A-Z]+)");
                if (mrzLine1.Success)
                {
                    result.Surname = mrzLine1.Groups[1].Value.Replace("<", "");
                    result.GivenNames = mrzLine1.Groups[2].Value.Replace("<", "");
                }
            }

            // 2. Личный номер (надежная регулярка + посимвольная коррекция гомоглифов)
            var idMatch = Regex.Match(cleanText.ToUpperInvariant(), @"\d{7}[A-ZА-Я][0-9A-ZА-Я]{3}[A-ZА-Я]{2}\d");
            if (idMatch.Success)
            {
                char[] idChars = idMatch.Value.ToCharArray();
                for (int i = 0; i < idChars.Length; i++)
                {
                    if (i == 7 || i == 11 || i == 12) // Позиции букв
                    {
                        if (idChars[i] == '0' || idChars[i] == 'О') idChars[i] = 'O';
                        if (idChars[i] == '1') idChars[i] = 'I';
                        if (idChars[i] == '8' || idChars[i] == 'В' || idChars[i] == 'Б') idChars[i] = 'B';
                        if (idChars[i] == 'P' || idChars[i] == 'Р') idChars[i] = 'P';
                        if (idChars[i] == 'C' || idChars[i] == 'С') idChars[i] = 'C';
                    }
                    else // Позиции цифр
                    {
                        if (idChars[i] == 'O' || idChars[i] == 'Q' || idChars[i] == 'D' || idChars[i] == 'О' || idChars[i] == 'C') idChars[i] = '0';
                        if (idChars[i] == 'I' || idChars[i] == 'L' || idChars[i] == 'T' || idChars[i] == 'l' || idChars[i] == 't') idChars[i] = '1';
                        if (idChars[i] == 'Z' || idChars[i] == 'Г') idChars[i] = '2';
                        if (idChars[i] == 'S') idChars[i] = '5';
                        if (idChars[i] == 'B' || idChars[i] == 'Б' || idChars[i] == 'В') idChars[i] = '8';
                    }
                }
                result.PersonalNumber = new string(idChars);
            }

            // 3. Серия и номер паспорта: строго 2 буквы + 7 цифр перед BLR (пропускаем "OF BLR")
            var mrzLine2Match = Regex.Match(cleanText.ToUpperInvariant(), @"([A-ZА-Я]{2}[0-9A-ZА-Я]{7})\d?BLR");
            if (mrzLine2Match.Success)
            {
                string passportRaw = mrzLine2Match.Groups[1].Value;

                // Переводим кириллицу серии в латиницу
                passportRaw = passportRaw.Replace("М", "M").Replace("Р", "P").Replace("В", "B")
                                         .Replace("С", "C").Replace("А", "A").Replace("К", "K")
                                         .Replace("Е", "E").Replace("Н", "H");

                char[] pChars = passportRaw.ToCharArray();
                for (int i = 0; i < pChars.Length; i++)
                {
                    if (i >= 2) // С 3-го символа идут только цифры
                    {
                        if (pChars[i] == 'O' || pChars[i] == 'Q' || pChars[i] == 'D' || pChars[i] == 'О') pChars[i] = '0';
                        if (pChars[i] == 'I' || pChars[i] == 'L' || pChars[i] == 'T') pChars[i] = '1';
                        if (pChars[i] == 'Z' || pChars[i] == 'Г') pChars[i] = '2';
                        if (pChars[i] == 'S') pChars[i] = '5';
                        if (pChars[i] == 'B' || pChars[i] == 'В') pChars[i] = '8';
                    }
                }
                result.PassportNumber = new string(pChars);
            }

            // 4. Дата рождения (пробелы, точки, дефисы + fallback из личного номера)
            var dobMatch = Regex.Match(upperText, @"(\d{2})[\s\.\-](\d{2})[\s\.\-](\d{4})");
            if (dobMatch.Success)
            {
                result.DateOfBirth = $"{dobMatch.Groups[1].Value}.{dobMatch.Groups[2].Value}.{dobMatch.Groups[3].Value}";
            }
            else if (!string.IsNullOrEmpty(result.PersonalNumber) && result.PersonalNumber.Length == 14)
            {
                // Извлекаем ДД.ММ.ГГГГ из личного номера гражданина РБ
                char c = result.PersonalNumber[0];
                int century = (c == '1' || c == '2') ? 1800 : (c == '3' || c == '4') ? 1900 : 2000;
                string d = result.PersonalNumber.Substring(1, 2);
                string m = result.PersonalNumber.Substring(3, 2);
                string y = result.PersonalNumber.Substring(5, 2);
                if (int.TryParse(y, out int yy))
                    result.DateOfBirth = $"{d}.{m}.{century + yy}";
            }
        }
        catch
        {
            // Защита от любых сбоев парсинга
        }

        return result;
    }

    /// <summary>
    /// Резервный метод поиска личного номера через регулярное выражение.
    /// </summary>
    public static string? ExtractPersonalNumberFallback(string text)
    {
        var result = Parse(text);
        return result.PersonalNumber;
    }
}
