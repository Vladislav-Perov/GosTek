/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Интеллектуальные алгоритмы форматирования пользовательского ввода (ФИО, даты, клавиатуры)
 * СОДЕРЖИМОЕ: SmartInputHelper
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls
 * ==============================================================================
 */
namespace GosTek.Services;

/// <summary>
/// Помощник для интеллектуальной обработки пользовательского ввода:
/// автоматическая капитализация ФИО (Title Case), детекция дат и подбор клавиатур.
/// </summary>
public static class SmartInputHelper
{
    /// <summary>
    /// Преобразует текст в Title Case (каждое слово с заглавной буквы).
    /// Корректно обрабатывает дефисы («Петров-Водкин»), точки («И.И.») и множественные пробелы.
    /// </summary>
    public static string ToTitleCase(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input ?? "";

        var chars = input.ToCharArray();
        bool newWord = true;

        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];

            if (char.IsWhiteSpace(c) || c == '-' || c == '.' || c == '"' || c == '«' || c == '(')
            {
                newWord = true;
            }
            else if (newWord && char.IsLetter(c))
            {
                chars[i] = char.ToUpper(c);
                newWord = false;
            }
            else if (!newWord && char.IsLetter(c))
            {
                // Сохраняем исходный регистр последующих букв, если слово уже с заглавной
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Проверяет, является ли поле полем ввода даты (по названию, подсказке или идентификатору).
    /// </summary>
    public static bool IsDateField(string? label, string? placeholder = null, string? id = null)
    {
        bool Match(string? text) =>
            text is not null && (
                text.Contains("дата", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("число", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("00.00.0000", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("дд.мм.гггг", StringComparison.OrdinalIgnoreCase));

        return Match(label) || Match(placeholder) ||
               (id is not null && id.Contains("date", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Проверяет, является ли поле полем ввода ФИО или имени.
    /// </summary>
    public static bool IsFioField(string? label, string? placeholder = null, string? id = null)
    {
        bool Match(string? text) =>
            text is not null && (
                text.Contains("фио", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("фамилия", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("имя", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("отчество", StringComparison.OrdinalIgnoreCase));

        return Match(label) || Match(placeholder) ||
               (id is not null && (
                   id.Contains("fio", StringComparison.OrdinalIgnoreCase) ||
                   id.Contains("name", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Возвращает текущую системную дату в формате «ДД.ММ.ГГГГ».
    /// </summary>
    public static string FormatToday() => DateTime.Now.ToString("dd.MM.yyyy");

    /// <summary>
    /// Подбирает оптимальный тип системной клавиатуры для поля ввода.
    /// </summary>
    public static Keyboard ResolveKeyboard(string? label, string? placeholder = null, string? id = null)
    {
        string combined = $"{label} {placeholder} {id}".ToLowerInvariant();

        if (combined.Contains("телефон") || combined.Contains("phone") || combined.Contains("тел."))
            return Keyboard.Telephone;

        if (combined.Contains("унп") || combined.Contains("инн") || combined.Contains("сумма") ||
            combined.Contains("стоимость") || combined.Contains("цена") || combined.Contains("количество"))
            return Keyboard.Numeric;

        return Keyboard.Text;
    }
}
