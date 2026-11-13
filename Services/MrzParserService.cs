/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Распознавание и разбор данных паспорта РБ (ФИО, серия/номер, личный номер, дата)
 * СОДЕРЖИМОЕ: MrzParserService, MrzParseResult
 * ЗАВИСИМОСТИ: System, System.Text.RegularExpressions, GosTek.Services.MrzSanitizer, GosTek.Services.TransliterationHelper
 * ==============================================================================
 */
using System.Globalization;
using System.Text.RegularExpressions;

namespace GosTek.Services;

/// <summary>
/// Удобный контейнер для хранения данных, извлечённых из паспорта гражданина РБ:
/// фамилия, имя, номер документа, личный 14-значный номер и дата рождения.
/// </summary>
public class MrzParseResult
{
    // Фамилия латиницей (как напечатано в паспорте, например "SAYAPIN")
    public string? Surname { get; set; }

    // Имя латиницей (например "ANDREI")
    public string? GivenNames { get; set; }

    // Уникальный идентификационный номер гражданина РБ (14 символов, например "3260483A011PB6")
    public string? PersonalNumber { get; set; }

    // Серия и номер паспорта (2 буквы серии + 7 цифр, например "MP2417879")
    public string? PassportNumber { get; set; }

    // Дата рождения строкой (формат "ДД.ММ.ГГГГ")
    public string? DateOfBirth { get; set; }

    // Пол ("M" / "F")
    public string? Sex { get; set; }

    // Срок действия паспорта
    public DateTime? ExpiryDate { get; set; }
    public string? ExpiryDateString { get; set; }

    // Удобные свойства-псевдонимы, чтобы старый код и экраны работали без переделок:
    public string? Name { get => GivenNames; set => GivenNames = value; }

    // Автоматический перевод фамилии и имени на русский язык для официальных документов
    public string? SurnameCyrillic => !string.IsNullOrEmpty(Surname) ? TransliterationHelper.Transliterate(Surname, false) : null;
    public string? NameCyrillic => !string.IsNullOrEmpty(GivenNames) ? TransliterationHelper.Transliterate(GivenNames, true) : null;

    public string? BirthDateString => DateOfBirth;
    public DateTime? BirthDate =>
        DateTime.TryParseExact(DateOfBirth, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt
            : null;

    // Считаем распознавание успешным, если удалось вытащить хотя бы один ключевой реквизит
    public bool Success => !string.IsNullOrEmpty(PassportNumber) || !string.IsNullOrEmpty(PersonalNumber) || !string.IsNullOrEmpty(Surname);

    // Пустой результат для безопасного возврата при ошибках (защита от вылетов программы)
    public static MrzParseResult Empty => new();
}

/// <summary>
/// Сервис для умного разбора текста паспорта.
/// Умеет находить данные как в машиночитаемой строке внизу паспорта (MRZ), так и в обычном печатном тексте.
/// Исправляет типичные оптические ошибки сканера (когда путаются похожие буквы и цифры).
/// </summary>
public static class MrzParserService
{
    // Весовые коэффициенты (7, 3, 1), принятые в международных паспортах для вычисления контрольных цифр
    private static readonly int[] Weights = { 7, 3, 1 };

    /// <summary>
    /// Считает контрольную цифру по международному паспорту (алгоритм ICAO 9303).
    /// Каждая цифра умножается на свой вес (7, 3 или 1), результат складывается и берётся остаток от деления на 10.
    /// Это нужно, чтобы проверить, не ошиблась ли камера при съёмке цифр.
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
                charValue = 0; // Символ заполнителя '<' и пробелы считаются нулями

            sum += charValue * Weights[i % 3];
        }

        return sum % 10;
    }

    /// <summary>
    /// Проверяет, совпадает ли контрольная цифра в строке с расчётной.
    /// Если сканер ошибся (например, принял букву 'O' за цифру '0'), пробует до 3 замен похожих символов,
    /// чтобы спасти данные без повторного сканирования.
    /// </summary>
    public static bool VerifyCheckDigit(string value, char checkChar, out string correctedValue)
    {
        correctedValue = value;
        int expected = CalculateCheckDigit(value);
        int actual = char.IsDigit(checkChar) ? (checkChar - '0') : CalculateCheckDigit(checkChar.ToString());

        // Если цифра сразу совпала — данные точные
        if (expected == actual)
            return true;

        // Пары символов, которые оптический сканер чаще всего путает между собой
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
    /// Главный метод разбора паспорта: получает сырой текст со сканера и находит в нём все нужные поля.
    /// Полностью защищён от падений — если фотография совсем неразборчива, метод вернёт пустой объект,
    /// а приложение продолжит спокойно работать.
    /// </summary>
    public static MrzParseResult Parse(string rawOcrText)
    {
        var result = new MrzParseResult();
        if (string.IsNullOrEmpty(rawOcrText)) return result;

        try
        {
            // Очищаем текст от переносов строк и пробелов для точного поиска номеров
            string cleanText = rawOcrText.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            string upperText = rawOcrText.ToUpperInvariant();

            // 1. Ищем Фамилию и Имя.
            // Сначала смотрим в видимую часть паспорта после слова "BLR" (например "BLR SAYAPIN ANDREI").
            // Если там не нашлось — берём нижнюю машиночитаемую строчку "P<BLR...".
            var nameMatch = Regex.Match(upperText, @"BLR\s+([A-Z]+)\s+([A-Z0-9]+)");
            if (nameMatch.Success)
            {
                // Устраняем частые огрехи распознавания в именах (цифра 1 вместо I, цифра 0 вместо O)
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

            // 2. Ищем 14-значный Личный номер гражданина Беларуси (например 3260483A011PB6).
            // В нём строго определено, где должны быть цифры, а где буквы (позиции 7, 11 и 12).
            var idMatch = Regex.Match(cleanText.ToUpperInvariant(), @"\d{7}[A-ZА-Я][0-9A-ZА-Я]{3}[A-ZА-Я]{2}\d");
            if (idMatch.Success)
            {
                char[] idChars = idMatch.Value.ToCharArray();
                for (int i = 0; i < idChars.Length; i++)
                {
                    if (i == 7 || i == 11 || i == 12) // На этих позициях по закону обязаны быть только буквы
                    {
                        if (idChars[i] == '0' || idChars[i] == 'О') idChars[i] = 'O';
                        if (idChars[i] == '1') idChars[i] = 'I';
                        if (idChars[i] == '8' || idChars[i] == 'В' || idChars[i] == 'Б') idChars[i] = 'B';
                        if (idChars[i] == 'P' || idChars[i] == 'Р') idChars[i] = 'P';
                        if (idChars[i] == 'C' || idChars[i] == 'С') idChars[i] = 'C';
                    }
                    else // На всех остальных позициях должны стоять строго цифры
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

            // 3. Ищем серию и номер паспорта (например "MP2417879").
            // Строго проверяем 2 буквы серии и 7 цифр номера перед кодом страны BLR, чтобы не захватить шапку документа.
            var mrzLine2Match = Regex.Match(cleanText.ToUpperInvariant(), @"([A-ZА-Я]{2}[0-9A-ZА-Я]{7})\d?BLR");
            if (mrzLine2Match.Success)
            {
                string passportRaw = mrzLine2Match.Groups[1].Value;

                // Если серия прочиталась русскими буквами (например "МР"), переводим её в латиницу ("MP")
                passportRaw = passportRaw.Replace("М", "M").Replace("Р", "P").Replace("В", "B")
                                         .Replace("С", "C").Replace("А", "A").Replace("К", "K")
                                         .Replace("Е", "E").Replace("Н", "H");

                char[] pChars = passportRaw.ToCharArray();
                for (int i = 0; i < pChars.Length; i++)
                {
                    if (i >= 2) // Начиная с 3-го символа идут строго 7 цифр номера
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

            // 4. Ищем дату рождения.
            // Сначала ищем в тексте явную запись вроде "26 04 1983" или "26.04.1983".
            // Если дата смазана или закрыта бликом — вычисляем её прямо из личного номера гражданина!
            var dobMatch = Regex.Match(upperText, @"(\d{2})[\s\.\-](\d{2})[\s\.\-](\d{4})");
            if (dobMatch.Success)
            {
                result.DateOfBirth = $"{dobMatch.Groups[1].Value}.{dobMatch.Groups[2].Value}.{dobMatch.Groups[3].Value}";
            }
            else if (!string.IsNullOrEmpty(result.PersonalNumber) && result.PersonalNumber.Length == 14)
            {
                // Первая цифра личного номера кодирует век рождения (1-2: 19 век, 3-4: 20 век, 5-6: 21 век)
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
            // Если в строковых функциях что-то пошло не так — просто возвращаем то, что успели собрать
        }

        return result;
    }

    /// <summary>
    /// Запасной способ найти личный номер в тексте через общий алгоритм парсинга.
    /// </summary>
    public static string? ExtractPersonalNumberFallback(string text)
    {
        var result = Parse(text);
        return result.PersonalNumber;
    }
}
