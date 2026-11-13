/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Детерминированное извлечение ФИО, паспорта и личного номера гражданина РБ из OCR-текста
 * СОДЕРЖИМОЕ: DocumentDataExtractor, ExtractedData
 * ЗАВИСИМОСТИ: System.Text.RegularExpressions, GosTek.Services.MrzParser, GosTek.Services.MrzParserService, GosTek.Services.MrzSanitizer
 * ==============================================================================
 */
using System.Text.RegularExpressions;

namespace GosTek.Services;

/// <summary>
/// Извлечение данных из распознанного OCR-текста документа:
/// приоритет отдается визуальной зоне (крупному тексту), а MRZ (ICAO 9303) служит надежным источником данных и резервом.
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
        bool IsValid = true,
        string? Surname = null,
        string? GivenName = null,
        string? SurnameCyrillic = null,
        string? GivenNameCyrillic = null);

    // Серия паспорта: 2 буквы (кириллица или латиница) + пробел? + 7 цифр
    private static readonly Regex PassportRegex =
        new(@"\b([А-ЯA-Z]{2})\s?(\d{7})\b", RegexOptions.Compiled);

    // Три слова с большой буквы подряд — характерно для «Фамилия Имя Отчество»
    private static readonly Regex FullNameRegex =
        new(@"\b([А-ЯЁ][а-яё]+)\s+([А-ЯЁ][а-яё]+)\s+([А-ЯЁ][а-яё]+)\b", RegexOptions.Compiled);

    public static ExtractedData Extract(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new ExtractedData(null, null);

        // 1. Полноценная проверка через MrzParser (чистый TD1 / TD3)
        var standardMrz = MrzParser.Parse(rawText);
        if (standardMrz.Format != MrzFormat.None)
        {
            return new ExtractedData(
                FullName: standardMrz.FullName,
                Passport: standardMrz.DocumentNumber,
                PersonalNumber: standardMrz.PersonalNumber,
                BirthDate: standardMrz.BirthDate,
                Sex: standardMrz.Sex,
                ExpiryDate: standardMrz.ExpiryDate,
                IsMrz: true,
                IsValid: standardMrz.DocumentNumberValid && (standardMrz.BirthDateValid || standardMrz.BirthDate == null),
                Surname: standardMrz.LastName,
                GivenName: standardMrz.FirstName
            );
        }

        // 2. Визуальная зона: кириллическое ФИО и номер паспорта
        string? visualPassport = null;
        var passportMatch = PassportRegex.Match(rawText);
        if (passportMatch.Success)
            visualPassport = $"{passportMatch.Groups[1].Value} {passportMatch.Groups[2].Value}";

        string? visualFullName = null;
        var nameMatch = FullNameRegex.Match(rawText);
        if (nameMatch.Success)
            visualFullName = $"{nameMatch.Groups[1].Value} {nameMatch.Groups[2].Value} {nameMatch.Groups[3].Value}";

        // 3. Бронебойный парсинг зашумленного MRZ / гибридного текста
        var mrzServiceResult = MrzParserService.Parse(rawText);

        bool hasMrzMarkers = rawText.Contains("<<<<") ||
            (rawText.Contains("BLR") && !string.IsNullOrEmpty(mrzServiceResult.PersonalNumber));

        // Выборка номера паспорта: приоритет визуальной зоне, если найдена; иначе MRZ
        string? passport = visualPassport;
        if (string.IsNullOrEmpty(passport) && !string.IsNullOrEmpty(mrzServiceResult.PassportNumber))
        {
            passport = mrzServiceResult.PassportNumber;
        }

        // Выборка ФИО: визуальная зона на кириллице в приоритете; затем транслитерация из зашумленного текста
        string? fullName = visualFullName;
        if (string.IsNullOrEmpty(fullName))
        {
            if (!string.IsNullOrEmpty(mrzServiceResult.SurnameCyrillic) && !string.IsNullOrEmpty(mrzServiceResult.NameCyrillic))
            {
                fullName = $"{mrzServiceResult.SurnameCyrillic} {mrzServiceResult.NameCyrillic}";
            }
            else if (!string.IsNullOrEmpty(mrzServiceResult.Surname) && !string.IsNullOrEmpty(mrzServiceResult.Name))
            {
                fullName = $"{mrzServiceResult.Surname} {mrzServiceResult.Name}";
            }
        }

        string? personalNumber = mrzServiceResult.PersonalNumber;
        DateTime? birthDate = mrzServiceResult.BirthDate;
        string? sex = mrzServiceResult.Sex;
        DateTime? expiryDate = mrzServiceResult.ExpiryDate;

        bool isMrz = hasMrzMarkers;
        bool isValid = !string.IsNullOrEmpty(passport) || !string.IsNullOrEmpty(personalNumber) || !string.IsNullOrEmpty(fullName);

        return new ExtractedData(
            FullName: fullName,
            Passport: passport,
            PersonalNumber: personalNumber,
            BirthDate: birthDate,
            Sex: sex,
            ExpiryDate: expiryDate,
            IsMrz: isMrz,
            IsValid: isValid,
            Surname: mrzServiceResult.Surname,
            GivenName: mrzServiceResult.Name,
            SurnameCyrillic: mrzServiceResult.SurnameCyrillic,
            GivenNameCyrillic: mrzServiceResult.NameCyrillic
        );
    }
}
