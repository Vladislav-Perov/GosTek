/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Санитаризация машиносчитываемой зоны (MRZ) и замена гомоглифов OCR
 * СОДЕРЖИМОЕ: MrzSanitizer
 * ЗАВИСИМОСТИ: System, System.Text
 * ==============================================================================
 */
using System.Text;

namespace GosTek.Services;

/// <summary>
/// Сервис санитаризации MRZ-строк и замены оптических гомоглифов (кириллица -> латиница, буквы -> цифры).
/// </summary>
public static class MrzSanitizer
{
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
    /// Проверяет, является ли строка кандидатом на MRZ (содержит &lt;&lt;&lt;&lt; или BLR).
    /// </summary>
    public static bool IsMrzCandidate(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        return text.Contains("<<<<") || text.Contains("BLR");
    }

    /// <summary>
    /// Санитаризирует строку MRZ: удаляет ВСЕ пробелы и заменяет кириллические гомоглифы на латиницу.
    /// ⚠️ КРИТИЧНО: Применяется ТОЛЬКО к строкам, содержащим &lt;&lt;&lt;&lt; или BLR. Визуальную зону не трогает.
    /// </summary>
    public static string Sanitize(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
            return rawText ?? string.Empty;

        // Визуальную зону не трогать: только строки с <<<< или BLR
        if (!IsMrzCandidate(rawText))
            return rawText;

        // САМЫЙ ПЕРВЫЙ ШАГ ДО ЛЮБЫХ ДЕЙСТВИЙ — удаление ВСЕХ пробелов
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
    /// Заменяет латинские и кириллические буквы-гомоглифы на цифры (O,D,Q->0, I,l->1, Z,Г->2, З->3, Ч->4, S->5, B,Б,В->8).
    /// ⚠️ КРИТИЧНО: Применяется ТОЛЬКО к датам и цифровой части номера паспорта!
    /// Серию паспорта и Личный номер (28-41) этим методом трогать КАТЕГОРИЧЕСКИ НЕЛЬЗЯ!
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
