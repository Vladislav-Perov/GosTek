using System.Text.RegularExpressions;

namespace GosTek.Services;

/// <summary>
/// Простое и честное извлечение данных из распознанного OCR-текста документа:
/// только ФИО и серия+номер паспорта, по регулярным выражениям. Не пытается угадать
/// адрес или произвольные поля шаблона — без разметки конкретного документа это
/// ненадёжно. Итоговая нормализация (вид «KN 1234567», кириллица→латиница в серии и т.п.)
/// выполняется существующим ProfileRules.Normalize при сохранении в профиль.
/// </summary>
public static class DocumentDataExtractor
{
    public sealed record ExtractedData(string? FullName, string? Passport);

    // Серия паспорта: 2 буквы (кириллица или латиница, OCR может перепутать) + 7 цифр
    private static readonly Regex PassportRegex =
        new(@"\b([А-ЯA-Z]{2})\s?(\d{7})\b", RegexOptions.Compiled);

    // Три слова с большой буквы подряд — характерно для «Фамилия Имя Отчество»
    private static readonly Regex FullNameRegex =
        new(@"\b([А-ЯЁ][а-яё]+)\s+([А-ЯЁ][а-яё]+)\s+([А-ЯЁ][а-яё]+)\b", RegexOptions.Compiled);

    public static ExtractedData Extract(string rawText)
    {
        string? passport = null;
        var passportMatch = PassportRegex.Match(rawText);
        if (passportMatch.Success)
            passport = $"{passportMatch.Groups[1].Value} {passportMatch.Groups[2].Value}";

        string? fullName = null;
        var nameMatch = FullNameRegex.Match(rawText);
        if (nameMatch.Success)
            fullName = $"{nameMatch.Groups[1].Value} {nameMatch.Groups[2].Value} {nameMatch.Groups[3].Value}";

        return new ExtractedData(fullName, passport);
    }
}
