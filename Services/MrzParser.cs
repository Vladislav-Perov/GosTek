/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Парсер Machine Readable Zone (MRZ) биометрических паспортов и ID-карт по стандарту ICAO 9303
 * СОДЕРЖИМОЕ: MrzParser, MrzResult, MrzFormat
 * ЗАВИСИМОСТИ: System, System.Text.RegularExpressions
 * ==============================================================================
 */

using System.Globalization;
using System.Text.RegularExpressions;

namespace GosTek.Services;

public enum MrzFormat
{
    None,
    Td1, // ID-карты: 3 строки по 30 символов
    Td3  // Паспорта: 2 строки по 44 символа
}

public sealed record MrzResult(
    string? DocumentNumber,
    bool DocumentNumberValid,
    string? PersonalNumber,
    bool PersonalNumberValid,
    string? LastName,
    string? FirstName,
    string? FullName,
    DateTime? BirthDate,
    bool BirthDateValid,
    string? Sex,
    DateTime? ExpiryDate,
    bool ExpiryDateValid,
    string? IssuerCountry,
    string? Nationality,
    bool OverallValid,
    MrzFormat Format)
{
    public static MrzResult Empty => new(
        null, false, null, false, null, null, null,
        null, false, null, null, false, null, null, false, MrzFormat.None);
}

/// <summary>
/// Детерминированный парсер ICAO 9303 с валидацией контрольных сумм по алгоритму 7-3-1
/// и нормализацией распространенных оптических искажений OCR.
/// </summary>
public static class MrzParser
{
    private static readonly int[] Weights = { 7, 3, 1 };

    /// <summary>
    /// Вычисляет контрольную цифру ICAO 9303 для указанной строки.
    /// Веса: 7, 3, 1 циклом. Символы: 0-9 = 0-9, A-Z = 10-35, разделитель = 0.
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
                charValue = 0; // '<' и прочие разделители = 0

            int weight = Weights[i % 3];
            sum += charValue * weight;
        }

        return sum % 10;
    }

    /// <summary>
    /// Проверяет совпадение контрольной цифры поля.
    /// </summary>
    public static bool VerifyCheckDigit(string value, char checkDigitChar)
    {
        int expected = CalculateCheckDigit(value);
        int actual = NormalizeDigit(checkDigitChar) - '0';
        return expected == actual;
    }

    /// <summary>
    /// Нормализует частые ошибки OCR в числовых зонах (O->0, I->1, B->8, S->5, Z->2).
    /// </summary>
    public static string NormalizeDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var chars = input.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = NormalizeDigit(chars[i]);
        }
        return new string(chars);
    }

    public static char NormalizeDigit(char c)
    {
        return char.ToUpperInvariant(c) switch
        {
            'O' or 'Q' or 'D' => '0',
            'I' or 'L' or '|' or '!' or 'J' => '1',
            'Z' => '2',
            'S' => '5',
            'B' => '8',
            _ => c
        };
    }

    /// <summary>
    /// Нормализует частые ошибки OCR в буквенных зонах (0->O, 1->I, 8->B, 5->S, 2->Z).
    /// </summary>
    public static string NormalizeAlpha(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var chars = input.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = char.ToUpperInvariant(chars[i]) switch
            {
                '0' => 'O',
                '1' => 'I',
                '2' => 'Z',
                '5' => 'S',
                '8' => 'B',
                _ => char.ToUpperInvariant(chars[i])
            };
        }
        return new string(chars);
    }

    /// <summary>
    /// Очищает строку MRZ от разделителей и пробелов.
    /// </summary>
    public static string CleanField(string value)
    {
        return value.Replace('<', ' ').Trim();
    }

    /// <summary>
    /// Пытается найти и распарсить зону MRZ (TD3 или TD1) в произвольном тексте OCR.
    /// </summary>
    public static MrzResult Parse(string? rawOcrText)
    {
        if (string.IsNullOrWhiteSpace(rawOcrText))
            return MrzResult.Empty;

        // Нормализуем строки текста: убираем пробелы внутри строк MRZ, заменяем похожие на '<' символы
        var lines = rawOcrText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().Replace(" ", "").Replace("«", "<").Replace("‹", "<"))
            .Where(l => l.Length >= 28 && l.Contains('<'))
            .ToList();

        // 1. Поиск TD3 (2 строки по 44 символа) - паспорта
        for (int i = 0; i < lines.Count - 1; i++)
        {
            var l1 = lines[i];
            var l2 = lines[i + 1];

            if (l1.Length >= 42 && l1.Length <= 46 && l2.Length >= 42 && l2.Length <= 46)
            {
                // Приводим ровно к 44 символам при необходимости
                string line1 = l1.Length >= 44 ? l1[..44] : l1.PadRight(44, '<');
                string line2 = l2.Length >= 44 ? l2[..44] : l2.PadRight(44, '<');

                if (line1.StartsWith("P", StringComparison.OrdinalIgnoreCase))
                {
                    var result = ParseTd3(line1, line2);
                    if (result != null)
                        return result;
                }
            }
        }

        // 2. Поиск TD1 (3 строки по 30 символов) - ID-карты
        for (int i = 0; i < lines.Count - 2; i++)
        {
            var l1 = lines[i];
            var l2 = lines[i + 1];
            var l3 = lines[i + 2];

            if (l1.Length >= 28 && l1.Length <= 32 &&
                l2.Length >= 28 && l2.Length <= 32 &&
                l3.Length >= 28 && l3.Length <= 32)
            {
                string line1 = l1.Length >= 30 ? l1[..30] : l1.PadRight(30, '<');
                string line2 = l2.Length >= 30 ? l2[..30] : l2.PadRight(30, '<');
                string line3 = l3.Length >= 30 ? l3[..30] : l3.PadRight(30, '<');

                if (line1.StartsWith("I", StringComparison.OrdinalIgnoreCase) ||
                    line1.StartsWith("A", StringComparison.OrdinalIgnoreCase) ||
                    line1.StartsWith("C", StringComparison.OrdinalIgnoreCase))
                {
                    var result = ParseTd1(line1, line2, line3);
                    if (result != null)
                        return result;
                }
            }
        }

        return MrzResult.Empty;
    }

    /// <summary>
    /// Парсинг двух строк формата TD3 (паспорт, 2x44).
    /// </summary>
    public static MrzResult? ParseTd3(string line1, string line2)
    {
        if (line1.Length != 44 || line2.Length != 44)
            return null;

        // Строка 1: P<BLRIVANOV<<IVAN<<<<<<<<<<<<<<<<<<<<<<<<<<<
        string issuer = NormalizeAlpha(line1.Substring(2, 3));
        string rawNames = line1.Substring(5, 39);

        var nameParts = rawNames.Split(new[] { "<<" }, StringSplitOptions.RemoveEmptyEntries);
        string? lastName = nameParts.Length > 0 ? CleanField(NormalizeAlpha(nameParts[0])) : null;
        string? firstName = nameParts.Length > 1 ? CleanField(NormalizeAlpha(nameParts[1])) : null;
        string? fullName = (!string.IsNullOrWhiteSpace(lastName) && !string.IsNullOrWhiteSpace(firstName))
            ? $"{lastName} {firstName}"
            : (lastName ?? firstName);

        // Строка 2:
        // 0..8: Номер паспорта (9 симв.)
        string rawDocNumber = line2.Substring(0, 9);
        // Серия обычно 2 буквы, остальное цифры: нормализуем первые 2 как буквы, остальные как цифры
        string series = NormalizeAlpha(rawDocNumber.Substring(0, 2));
        string docNum = NormalizeDigits(rawDocNumber.Substring(2, 7));
        string documentNumber = $"{series} {docNum}";

        char docCheckDigit = line2[9];
        bool docValid = VerifyCheckDigit(rawDocNumber, docCheckDigit);

        string nationality = NormalizeAlpha(line2.Substring(10, 3));

        // 13..18: Дата рождения ГГММДД
        string rawBirth = NormalizeDigits(line2.Substring(13, 6));
        char birthCheck = line2[19];
        bool birthValid = VerifyCheckDigit(rawBirth, birthCheck);
        DateTime? birthDate = ParseDate(rawBirth, isPast: true);

        // 20: Пол
        char sexChar = char.ToUpperInvariant(line2[20]);
        string sex = sexChar == 'M' ? "Мужской" : (sexChar == 'F' ? "Женский" : "Не указан");

        // 21..26: Срок действия ГГММДД
        string rawExpiry = NormalizeDigits(line2.Substring(21, 6));
        char expiryCheck = line2[27];
        bool expiryValid = VerifyCheckDigit(rawExpiry, expiryCheck);
        DateTime? expiryDate = ParseDate(rawExpiry, isPast: false);

        // 28..41: Личный (идентификационный) номер гражданина РБ (14 симв.)
        string rawPersonal = line2.Substring(28, 14).Replace('<', ' ').Trim();
        string personalNumber = rawPersonal;
        char personalCheck = line2[42];
        bool personalValid = personalCheck == '<' || VerifyCheckDigit(line2.Substring(28, 14), personalCheck);

        // Общая контрольная сумма
        char overallCheck = line2[43];
        string compositeSource = line2.Substring(0, 10) + line2.Substring(13, 7) + line2.Substring(21, 22);
        bool overallValid = VerifyCheckDigit(compositeSource, overallCheck);

        return new MrzResult(
            DocumentNumber: documentNumber,
            DocumentNumberValid: docValid,
            PersonalNumber: personalNumber,
            PersonalNumberValid: personalValid,
            LastName: lastName,
            FirstName: firstName,
            FullName: fullName,
            BirthDate: birthDate,
            BirthDateValid: birthValid,
            Sex: sex,
            ExpiryDate: expiryDate,
            ExpiryDateValid: expiryValid,
            IssuerCountry: issuer,
            Nationality: nationality,
            OverallValid: overallValid,
            Format: MrzFormat.Td3
        );
    }

    /// <summary>
    /// Парсинг трех строк формата TD1 (ID-карта, 3x30).
    /// </summary>
    public static MrzResult? ParseTd1(string line1, string line2, string line3)
    {
        if (line1.Length != 30 || line2.Length != 30 || line3.Length != 30)
            return null;

        string issuer = NormalizeAlpha(line1.Substring(2, 3));
        string rawDocNumber = line1.Substring(5, 9);
        char docCheckDigit = line1[14];
        bool docValid = VerifyCheckDigit(rawDocNumber, docCheckDigit);
        string documentNumber = CleanField(rawDocNumber);

        // Доп. данные первой строки (часто личный номер)
        string optional1 = line1.Substring(15, 15).Replace('<', ' ').Trim();

        // Строка 2:
        string rawBirth = NormalizeDigits(line2.Substring(0, 6));
        char birthCheck = line2[6];
        bool birthValid = VerifyCheckDigit(rawBirth, birthCheck);
        DateTime? birthDate = ParseDate(rawBirth, isPast: true);

        char sexChar = char.ToUpperInvariant(line2[7]);
        string sex = sexChar == 'M' ? "Мужской" : (sexChar == 'F' ? "Женский" : "Не указан");

        string rawExpiry = NormalizeDigits(line2.Substring(8, 6));
        char expiryCheck = line2[14];
        bool expiryValid = VerifyCheckDigit(rawExpiry, expiryCheck);
        DateTime? expiryDate = ParseDate(rawExpiry, isPast: false);

        string nationality = NormalizeAlpha(line2.Substring(15, 3));
        string optional2 = line2.Substring(18, 11).Replace('<', ' ').Trim();

        char overallCheck = line2[29];
        string composite = line1.Substring(5, 25) + line2.Substring(0, 7) + line2.Substring(8, 7) + line2.Substring(18, 11);
        bool overallValid = VerifyCheckDigit(composite, overallCheck);

        // Строка 3: Имена
        string rawNames = line3;
        var nameParts = rawNames.Split(new[] { "<<" }, StringSplitOptions.RemoveEmptyEntries);
        string? lastName = nameParts.Length > 0 ? CleanField(NormalizeAlpha(nameParts[0])) : null;
        string? firstName = nameParts.Length > 1 ? CleanField(NormalizeAlpha(nameParts[1])) : null;
        string? fullName = (!string.IsNullOrWhiteSpace(lastName) && !string.IsNullOrWhiteSpace(firstName))
            ? $"{lastName} {firstName}"
            : (lastName ?? firstName);

        string? personalNumber = !string.IsNullOrWhiteSpace(optional1) ? optional1 : (!string.IsNullOrWhiteSpace(optional2) ? optional2 : null);

        return new MrzResult(
            DocumentNumber: documentNumber,
            DocumentNumberValid: docValid,
            PersonalNumber: personalNumber,
            PersonalNumberValid: true,
            LastName: lastName,
            FirstName: firstName,
            FullName: fullName,
            BirthDate: birthDate,
            BirthDateValid: birthValid,
            Sex: sex,
            ExpiryDate: expiryDate,
            ExpiryDateValid: expiryValid,
            IssuerCountry: issuer,
            Nationality: nationality,
            OverallValid: overallValid,
            Format: MrzFormat.Td1
        );
    }

    private static DateTime? ParseDate(string yymmdd, bool isPast)
    {
        if (yymmdd.Length != 6)
            return null;

        if (!int.TryParse(yymmdd.Substring(0, 2), out int yy) ||
            !int.TryParse(yymmdd.Substring(2, 2), out int mm) ||
            !int.TryParse(yymmdd.Substring(4, 2), out int dd))
        {
            return null;
        }

        if (mm < 1 || mm > 12 || dd < 1 || dd > 31)
            return null;

        int currentYear = DateTime.Now.Year % 100;
        int century = isPast
            ? (yy > currentYear ? 1900 : 2000)
            : (yy >= (currentYear - 10) ? 2000 : 2100);

        int fullYear = century + yy;

        try
        {
            return new DateTime(fullYear, mm, dd);
        }
        catch
        {
            return null;
        }
    }
}
