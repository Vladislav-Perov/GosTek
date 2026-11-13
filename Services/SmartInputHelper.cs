/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Умные подсказки и автоформатирование ввода (ФИО с большой буквы, маски дат, подбор клавиатуры)
 * СОДЕРЖИМОЕ: SmartInputHelper
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls
 * ==============================================================================
 */
namespace GosTek.Services;

/// <summary>
/// Умный помощник для полей ввода.
/// Что делает: делает каждое слово в имени с заглавной буквы, автоматически ставит точки в датах
/// и выбирает удобную клавиатуру (цифры или буквы).
/// С чем взаимодействует: с полями форм на экранах ProfilePage и FieldsPage.
/// Зачем это нужно: чтобы пользователю было удобно и быстро заполнять документы без лишних переключений.
/// </summary>
public static class SmartInputHelper
{
    /// <summary>
    /// Делает первую букву каждого слова заглавной (например, «иванов иван» превратит в «Иванов Иван»).
    /// Учитывает двойные фамилии через дефис («Мамин-Сибиряк») и инициалы с точками («А.С. Пушкин»).
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
                // Оставляем последующие буквы как есть
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Проверяет по названию, является ли поле вводом даты.
    /// Если да, то для него автоматически включится быстрый выбор дня и маска ввода.
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
    /// Проверяет, является ли поле именем человека (ФИО, фамилия, имя или отчество).
    /// Для таких полей включается автоматическое написание с большой буквы.
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
    /// Возвращает сегодняшнюю дату строкой в формате «ДД.ММ.ГГГГ» для быстрой вставки по кнопке.
    /// </summary>
    public static string FormatToday() => DateTime.Now.ToString("dd.MM.yyyy");

    /// <summary>
    /// Автоматически расставляет разделительные точки при вводе даты:
    /// пользователь просто вводит 8 цифр подряд (например, 26041983),
    /// а функция на лету превращает это в «26.04.1983».
    /// Корректно работает при нажатии Backspace и при вставке скопированной даты из буфера.
    /// </summary>
    public static string ApplyDateMask(string? newText, string? oldText = null)
    {
        if (string.IsNullOrEmpty(newText))
            return "";

        // Если вставили готовую дату в стандартных форматах, нормализуем её в ДД.ММ.ГГГГ
        if (DateTime.TryParseExact(newText.Trim(),
                new[] { "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "dd-MM-yyyy", "d.M.yyyy", "dd.MM.yyyy" },
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsedDate))
        {
            return parsedDate.ToString("dd.MM.yyyy");
        }

        // Обработка удаления (backspace)
        if (oldText != null && newText.Length < oldText.Length && oldText.StartsWith(newText))
        {
            // Если пользователь удалил точку (например было "12." стало "12")
            if (oldText.EndsWith('.') && !newText.EndsWith('.') && newText.Length > 0)
            {
                return newText.Substring(0, newText.Length - 1);
            }
            return newText;
        }

        // Извлекаем только цифры, максимум 8 цифр (ДДММГГГГ)
        var digits = new string(newText.Where(char.IsDigit).Take(8).ToArray());
        if (digits.Length == 0)
            return "";

        if (digits.Length <= 2)
        {
            if (digits.Length == 2)
                return $"{digits}.";
            return digits;
        }

        if (digits.Length <= 4)
        {
            string day = digits.Substring(0, 2);
            string month = digits.Substring(2);
            if (digits.Length == 4)
                return $"{day}.{month}.";
            return $"{day}.{month}";
        }

        string d = digits.Substring(0, 2);
        string m = digits.Substring(2, 2);
        string y = digits.Substring(4);
        return $"{d}.{m}.{y}";
    }

    /// <summary>
    /// Пытается распарсить дату из строки в один из поддерживаемых форматов (приоритет у ДД.ММ.ГГГГ).
    /// </summary>
    public static bool TryParseDate(string? input, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string trimmed = input.Trim();
        string[] formats = { "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy" };

        return DateTime.TryParseExact(trimmed, formats,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date)
            || DateTime.TryParse(trimmed, out date);
    }

    /// <summary>
    /// Проверяет, является ли строка валидной календарной датой в формате ДД.ММ.ГГГГ.
    /// </summary>
    public static bool IsValidDate(string? input) =>
        TryParseDate(input, out _);

    /// <summary>
    /// Форматирует дату для сохранения в черновик (DraftStore) и подстановки в шаблон.
    /// </summary>
    public static string FormatDateForStorage(string? input)
    {
        if (TryParseDate(input, out var date))
            return date.ToString("dd.MM.yyyy");

        return input?.Trim() ?? "";
    }

    /// <summary>
    /// Подбирает оптимальный тип системной клавиатуры для поля ввода.
    /// </summary>
    public static Keyboard ResolveKeyboard(string? label, string? placeholder = null, string? id = null)
    {
        if (IsDateField(label, placeholder, id))
            return Keyboard.Numeric;

        string combined = $"{label} {placeholder} {id}".ToLowerInvariant();

        if (combined.Contains("телефон") || combined.Contains("phone") || combined.Contains("тел."))
            return Keyboard.Telephone;

        if (combined.Contains("унп") || combined.Contains("инн") || combined.Contains("сумма") ||
            combined.Contains("стоимость") || combined.Contains("цена") || combined.Contains("количество"))
            return Keyboard.Numeric;

        return Keyboard.Text;
    }
}
