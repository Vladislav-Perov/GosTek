/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Детерминированное извлечение ФИО, паспорта и личного номера гражданина РБ из OCR-текста
 * СОДЕРЖИМОЕ: DocumentDataExtractor, ExtractedData
 * ЗАВИСИМОСТИ: System.Text.RegularExpressions, GosTek.Services.MrzParser, GosTek.Services.MrzParserService, GosTek.Services.MrzSanitizer, GosTek.Services.TransliterationHelper
 * ==============================================================================
 */
using System.Text.RegularExpressions;

namespace GosTek.Services;

/// <summary>
/// Извлечение данных из распознанного OCR-текста документа:
/// приоритет отдается визуальной зоне (крупному тексту), а MRZ (ICAO 9303) служит надежным источником данных и резервом.
/// При отсутствии или зашумлении MRZ применяются отказоустойчивые регулярные выражения и санитаризация гомоглифов.
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

    // Базовый поиск ФИО после кода BLR: BLR\s+([A-Z]+)\s+([A-Z0-9]+)
    private static readonly Regex BlrFullNameRegex =
        new(@"BLR\s+([A-Za-z]+)\s+([A-Za-z0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Личный номер РБ с учетом возможных гомоглифов в зашумленном OCR
    private static readonly Regex PersonalNumberFallbackRegex =
        new(@"\d{7}[A-Za-zА-Яа-я][0-9OtlIЗQS]{3}[A-Za-zА-Яа-я]{2}[0-9OtlI]", RegexOptions.Compiled);

    // 7 цифр для поиска номера паспорта в очищенном тексте
    private static readonly Regex SevenDigitsRegex =
        new(@"\d{7}", RegexOptions.Compiled);

    public static ExtractedData Extract(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new ExtractedData(null, null);

        try
        {
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
            string? surname = mrzServiceResult.Surname;
            string? givenName = mrzServiceResult.Name;
            string? surnameCyr = mrzServiceResult.SurnameCyrillic;
            string? givenNameCyr = mrzServiceResult.NameCyrillic;

            if (string.IsNullOrEmpty(fullName))
            {
                if (!string.IsNullOrEmpty(surnameCyr) && !string.IsNullOrEmpty(givenNameCyr))
                {
                    fullName = $"{surnameCyr} {givenNameCyr}";
                }
                else if (!string.IsNullOrEmpty(surname) && !string.IsNullOrEmpty(givenName))
                {
                    fullName = $"{surname} {givenName}";
                }
            }

            // Задача 2: Базовые регулярки для ФИО, если строгий парсер не нашел ФИО
            if (string.IsNullOrEmpty(fullName))
            {
                var blrMatch = BlrFullNameRegex.Match(rawText);
                if (blrMatch.Success)
                {
                    string rawSurname = blrMatch.Groups[1].Value;
                    string rawName = blrMatch.Groups[2].Value;

                    surname = rawSurname;
                    givenName = rawName.Replace('1', 'I').Replace('0', 'O');
                    surnameCyr = TransliterationHelper.Transliterate(rawSurname, false);
                    givenNameCyr = TransliterationHelper.Transliterate(rawName, true);
                    fullName = $"{surnameCyr} {givenNameCyr}";
                }
            }

            string? personalNumber = mrzServiceResult.PersonalNumber;
            DateTime? birthDate = mrzServiceResult.BirthDate;
            string? sex = mrzServiceResult.Sex;
            DateTime? expiryDate = mrzServiceResult.ExpiryDate;

            // Задача 3: Базовые регулярки для номеров поверх сырого текста без пробелов
            string textNoSpaces = rawText.Replace(" ", "");

            // 3.1. Непробиваемый Fallback для Личного номера
            if (string.IsNullOrEmpty(personalNumber))
            {
                var matchPn = PersonalNumberFallbackRegex.Match(textNoSpaces);
                if (!matchPn.Success)
                {
                    matchPn = Regex.Match(textNoSpaces, @"\d{7}[A-Za-zА-Яа-я]\d{3}[A-Za-zА-Яа-я]{2}\d");
                }

                if (matchPn.Success)
                {
                    string rawPn = matchPn.Value;
                    string sanitizedPn = MrzSanitizer.Sanitize(rawPn);

                    if (sanitizedPn.Length == 14)
                    {
                        var sb = new System.Text.StringBuilder(14);
                        for (int i = 0; i < 14; i++)
                        {
                            char c = sanitizedPn[i];
                            if (i == 7 || i == 11 || i == 12)
                            {
                                // Буквенные позиции личного номера: цифры-гомоглифы в буквы
                                if (c == '0') sb.Append('O');
                                else if (c == '1') sb.Append('I');
                                else sb.Append(char.ToUpperInvariant(c));
                            }
                            else
                            {
                                // Цифровые позиции личного номера: буквы-гомоглифы в цифры
                                if (c == 'O' || c == 'o' || c == 'Q' || c == 'q') sb.Append('0');
                                else if (c == 'I' || c == 'i' || c == 'l' || c == 't' || c == 'L' || c == '|') sb.Append('1');
                                else if (c == 'Z' || c == 'z' || c == 'Г' || c == 'г') sb.Append('2');
                                else if (c == 'З' || c == 'з') sb.Append('3');
                                else if (c == 'Ч' || c == 'ч') sb.Append('4');
                                else if (c == 'S' || c == 's') sb.Append('5');
                                else if (c == 'B' || c == 'b' || c == 'В' || c == 'в' || c == 'Б' || c == 'б') sb.Append('8');
                                else sb.Append(c);
                            }
                        }
                        personalNumber = sb.ToString();
                    }
                }
            }

            // Доизвлечение даты рождения из личного номера при необходимости
            if (!birthDate.HasValue && !string.IsNullOrEmpty(personalNumber) && personalNumber.Length == 14)
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
                    try { birthDate = new DateTime(century + y, m, d); } catch { }
                }
            }

            // 3.2. Непробиваемый Fallback для Номера паспорта
            if (string.IsNullOrEmpty(passport))
            {
                var matches7 = SevenDigitsRegex.Matches(textNoSpaces);
                foreach (Match m in matches7)
                {
                    string candidate7 = m.Value;
                    // Пропускаем 7 цифр, если они являются частью личного номера
                    if (!string.IsNullOrEmpty(personalNumber) && personalNumber.Contains(candidate7))
                        continue;

                    string series = "MP"; // Дефолтная серия паспорта РБ
                    int mIdx = m.Index;
                    if (mIdx >= 2)
                    {
                        string prev2 = textNoSpaces.Substring(mIdx - 2, 2);
                        if (prev2.All(char.IsLetter))
                        {
                            series = MrzSanitizer.Sanitize(prev2 + "BLR").Substring(0, 2);
                        }
                    }

                    passport = $"{series}{candidate7}";
                    break;
                }
            }

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
                Surname: surname,
                GivenName: givenName,
                SurnameCyrillic: surnameCyr,
                GivenNameCyrillic: givenNameCyr
            );
        }
        catch
        {
            // UI никогда не должен падать
            return new ExtractedData(null, null, IsValid: false);
        }
    }
}
