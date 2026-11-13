/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Очистка текста паспорта от артефактов сканера и замена похожих символов
 * СОДЕРЖИМОЕ: MrzSanitizer
 * ЗАВИСИМОСТИ: System, System.Text
 * ==============================================================================
 */
using System.Text;

namespace GosTek.Services;

/// <summary>
/// Сервис предварительной очистки текста со сканера.
/// Когда камера фотографирует паспорт под углом или при плохом освещении, буквы часто смазываются:
/// русская буква 'М' может прочитаться вместо английской 'M', а буква 'О' вместо цифры '0'.
/// Этот сервис исправляет такую путаницу, чтобы программа не сбоила при разборе.
/// </summary>
public static class MrzSanitizer
{
    // Таблица соответствия: превращаем русские буквы в визуально одинаковые латинские
    private static readonly Dictionary<char, char> CyrillicToLatinHomoglyphs = new()
    {
        ['А'] = 'A', ['а'] = 'A',
        ['В'] = 'B', ['в'] = 'B',
        ['Е'] = 'E', ['е'] = 'E',
        ['К'] = 'K', ['к'] = 'K',
        ['М'] = 'M', ['м'] = 'M',
        ['Н'] = 'H', ['н'] = 'H',
        ['О'] = 'O', ['о'] = 'O',
        ['Р'] = 'P', ['р'] = 'P',
        ['С'] = 'C', ['с'] = 'C',
        ['Т'] = 'T', ['т'] = 'T',
        ['Х'] = 'X', ['х'] = 'X',
        ['У'] = 'Y', ['у'] = 'Y',
        ['І'] = 'I', ['і'] = 'I',
    };

    // Таблица соответствия: превращаем буквы, похожие на цифры, в настоящие цифры (для дат и номеров)
    private static readonly Dictionary<char, char> LetterToDigitHomoglyphs = new()
    {
        ['O'] = '0', ['o'] = '0',
        ['D'] = '0', ['d'] = '0',
        ['Q'] = '0', ['q'] = '0',
        ['I'] = '1', ['i'] = '1',
        ['l'] = '1', ['L'] = '1',
        ['|'] = '1',
        ['Z'] = '2', ['z'] = '2',
        ['Г'] = '2', ['г'] = '2',
        ['З'] = '3', ['з'] = '3',
        ['Ч'] = '4', ['ч'] = '4',
        ['S'] = '5', ['s'] = '5',
        ['B'] = '8', ['b'] = '8',
        ['Б'] = '8', ['б'] = '8',
        ['В'] = '8', ['в'] = '8',
    };

    /// <summary>
    /// Проверяет, похожа ли строчка на нижнюю машиночитаемую строку паспорта (MRZ).
    /// В таких строках всегда встречаются стрелочки-заполнители (&lt;&lt;&lt;&lt;) или код страны (BLR).
    /// </summary>
    public static bool IsMrzCandidate(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        return text.Contains("<<<<") || text.Contains("BLR");
    }

    /// <summary>
    /// Очищает машиночитаемую строку: удаляет случайные пробелы и заменяет русские буквы на латинские.
    /// Взаимодействует с OCR-распознавателем и MrzParserService.
    /// Важно: применяется ТОЛЬКО к строкам с кодом BLR или стрелками &lt;&lt;&lt;&lt;, чтобы не испортить обычный русский текст!
    /// </summary>
    public static string Sanitize(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
            return rawText ?? string.Empty;

        // Если это обычный печатный текст сверху страницы — оставляем его как есть
        if (!IsMrzCandidate(rawText))
            return rawText;

        // Первым делом удаляем случайные пробелы, которые сканер любит вставлять между буквами
        string cleaned = rawText.Replace(" ", "");

        var sb = new StringBuilder(cleaned.Length);
        foreach (char c in cleaned)
        {
            if (CyrillicToLatinHomoglyphs.TryGetValue(c, out char latin))
                sb.Append(latin);
            else
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Превращает подозрительные буквы в цифры (например, букву 'O' в '0', букву 'I' в '1').
    /// Взаимодействует с парсером дат и номеров документов.
    /// Нужен только для полей, где точно должны быть цифры (дата рождения, 7 цифр номера паспорта).
    /// </summary>
    public static string SanitizeNumbers(string rawNumber)
    {
        if (string.IsNullOrEmpty(rawNumber))
            return string.Empty;

        string cleaned = rawNumber.Replace(" ", "");
        var sb = new StringBuilder(cleaned.Length);

        foreach (char c in cleaned)
        {
            if (LetterToDigitHomoglyphs.TryGetValue(c, out char digit))
                sb.Append(digit);
            else
                sb.Append(c);
        }

        return sb.ToString();
    }
}
