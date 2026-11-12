/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Детерминированное извлечение ФИО и паспортных данных гражданина РБ из OCR-текста
 * СОДЕРЖИМОЕ: DocumentDataExtractor, ExtractedData
 * ЗАВИСИМОСТИ: System.Text.RegularExpressions, GosTek.Services.MrzParser
 * ==============================================================================
 */
using System.Text.RegularExpressions;

namespace GosTek.Services;

/// <summary>
/// Извлечение данных из распознанного OCR-текста документа:
/// в первую очередь использует парсер машиносчитываемой зоны MRZ (ICAO 9303) биометрических паспортов и ID-карт,
/// а при отсутствии MRZ — регулярные выражения с нормализацией.
/// </summary>
public static class DocumentDataExtractor
{
    public sealed record ExtractedData(
        string? FullName,
        string? Passport,
        string? PersonalNumber = null,
        DateTime? BirthDate = null,
        string? Sex = null,
        DateTime? ExpiryDate = null,
        bool IsMrz = false,
        bool IsValid = true);

    // Серия паспорта: 2 буквы (кириллица или латиница, OCR может перепутать) + 7 цифр
    private static readonly Regex PassportRegex =
        new(@"\b([А-ЯA-Z]{2})\s?(\d{7})\b", RegexOptions.Compiled);

    // Три слова с большой буквы подряд — характерно для «Фамилия Имя Отчество»
    private static readonly Regex FullNameRegex =
        new(@"\b([А-ЯЁ][а-яё]+)\s+([А-ЯЁ][а-яё]+)\s+([А-ЯЁ][а-яё]+)\b", RegexOptions.Compiled);

    public static ExtractedData Extract(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new ExtractedData(null, null);

        // 1. Попытка распарсить через ICAO 9303 MRZ
        var mrz = MrzParser.Parse(rawText);
        if (mrz.Format != MrzFormat.None)
        {
            return new ExtractedData(
                FullName: mrz.FullName,
                Passport: mrz.DocumentNumber,
                PersonalNumber: mrz.PersonalNumber,
                BirthDate: mrz.BirthDate,
                Sex: mrz.Sex,
                ExpiryDate: mrz.ExpiryDate,
                IsMrz: true,
                IsValid: mrz.DocumentNumberValid && (mrz.BirthDateValid || mrz.BirthDate == null));
        }

        // 2. Резервный парсинг визуальной зоны по регулярным выражениям
        string? passport = null;
        var passportMatch = PassportRegex.Match(rawText);
        if (passportMatch.Success)
            passport = $"{passportMatch.Groups[1].Value} {passportMatch.Groups[2].Value}";

        string? fullName = null;
        var nameMatch = FullNameRegex.Match(rawText);
        if (nameMatch.Success)
            fullName = $"{nameMatch.Groups[1].Value} {nameMatch.Groups[2].Value} {nameMatch.Groups[3].Value}";

        return new ExtractedData(fullName, passport, IsMrz: false, IsValid: true);
    }
}
