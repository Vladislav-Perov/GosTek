/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Универсальный классификатор структурных блоков документа
 *             (Шапка / Адресат, Заголовок, Тело, Списки, Подписи / Подвал)
 * ==============================================================================
 */
using System.Text.RegularExpressions;

namespace GosTek.Services;

public enum DocumentBlockType
{
    HeaderRight,     // Адресат в правом верхнем углу (Кому, от кого, Директору...)
    HeaderLeft,      // Реквизиты бланка слева (Исх. номер, дата, организация)
    Title,           // Название документа (Заявление, Приказ, Акт, Служебная записка...)
    Paragraph,       // Стандартный абзац основного текста (с красной строкой 1.25 см)
    ListItem,        // Элемент списка (1., 1.1, -, •)
    SignatureRow,    // Строка подписи (Дата / Должность / Подпись / ФИО)
    EmptyLine        // Пустая строка-разделитель
}

public sealed record AnalyzedBlock(
    DocumentBlockType Type,
    string Text,
    string? Col1 = null,
    string? Col2 = null,
    string? Col3 = null
);

public static class UniversalDocumentParser
{
    private static readonly HashSet<string> StandardDocTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ЗАЯВЛЕНИЕ", "ПРИКАЗ", "АКТ", "СЛУЖЕБНАЯ ЗАПИСКА", "ОБЪЯСНИТЕЛЬНАЯ ЗАПИСКА",
        "ДОГОВОР", "СОГЛАШЕНИЕ", "ПРОТОКОЛ", "РАСПОРЯЖЕНИЕ", "ПОСТАНОВЛЕНИЕ",
        "СПРАВКА", "ПРЕТЕНЗИЯ", "УВЕДОМЛЕНИЕ", "ДОКЛАДНАЯ ЗАПИСКА", "ХОДАТАЙСТВО",
        "РАПОРТ", "ТРЕБОВАНИЕ", "ДЕКЛАРАЦИЯ", "АНКЕТА"
    };

    private static readonly Regex NumberedListRegex = new(
        @"^(\d{1,2}[\.\)]|[a-zA-Zа-яА-Я][\.\)]|[\-–—•])\s+(.+)$",
        RegexOptions.Compiled);

    private static readonly Regex SignatureRowRegex = new(
        @"(руководитель|директор|генеральный|начальник|бухгалтер|декан|заведующий|составил|подпись|исполнитель|секретарь|родител[яь]|студент|заявител[яь]|дата|расшифровка)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<AnalyzedBlock> Parse(string rawText)
    {
        var result = new List<AnalyzedBlock>();
        if (string.IsNullOrWhiteSpace(rawText))
            return result;

        var rawLines = rawText
            .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
            .Select(l => l.Trim())
            .ToList();

        // 1. Находим "водораздел" документа — индекс строки заголовка (Заявление, Приказ и т.д.)
        int titleIndex = -1;
        for (int i = 0; i < rawLines.Count; i++)
        {
            string line = rawLines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (IsTitleLine(line))
            {
                titleIndex = i;
                break;
            }
        }

        // Если явный заголовок из списка не найден, ищем первое типичное вводное слово
        if (titleIndex == -1)
        {
            for (int i = 0; i < rawLines.Count; i++)
            {
                string line = rawLines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Слова начала основного текста
                if (line.StartsWith("Прошу", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("В соответствии", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Настоящим", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Уведомляем", StringComparison.OrdinalIgnoreCase))
                {
                    titleIndex = i;
                    break;
                }
            }
        }

        // 2. ЗОНА 1: Всё, что ДО заголовка — это ШАПКА
        int bodyStartIndex = 0;
        if (titleIndex > 0)
        {
            for (int i = 0; i < titleIndex; i++)
            {
                string line = rawLines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Строки шапки никогда не склеиваются, каждая строка адресата идет в правый блок
                result.Add(new AnalyzedBlock(DocumentBlockType.HeaderRight, line));
            }

            result.Add(new AnalyzedBlock(DocumentBlockType.EmptyLine, string.Empty));
            bodyStartIndex = titleIndex;
        }

        // 3. ЗОНА 2: ЗАГОЛОВОК
        if (titleIndex >= 0 && titleIndex < rawLines.Count)
        {
            result.Add(new AnalyzedBlock(DocumentBlockType.Title, rawLines[titleIndex]));
            result.Add(new AnalyzedBlock(DocumentBlockType.EmptyLine, string.Empty));
            bodyStartIndex = titleIndex + 1;
        }

        // 4. ЗОНА 3 и 4: ОСНОВНОЙ ТЕКСТ И ПОДВАЛ (ПОДПИСИ)
        var bodyLines = new List<string>();
        for (int i = bodyStartIndex; i < rawLines.Count; i++)
        {
            bodyLines.Add(rawLines[i]);
        }

        // Находим блок подписи с конца документа
        int signatureStartIndex = FindSignatureStartIndex(bodyLines);

        // Обрабатываем тело документа (склеиваем разорванные строки)
        int bodyEndIndex = signatureStartIndex >= 0 ? signatureStartIndex : bodyLines.Count;
        var normalizedParagraphs = NormalizeParagraphs(bodyLines.Take(bodyEndIndex).ToList());

        foreach (var p in normalizedParagraphs)
        {
            if (string.IsNullOrWhiteSpace(p))
            {
                result.Add(new AnalyzedBlock(DocumentBlockType.EmptyLine, string.Empty));
                continue;
            }

            if (NumberedListRegex.IsMatch(p))
            {
                result.Add(new AnalyzedBlock(DocumentBlockType.ListItem, p));
            }
            else
            {
                result.Add(new AnalyzedBlock(DocumentBlockType.Paragraph, p));
            }
        }

        // Обрабатываем подписи в подвале
        if (signatureStartIndex >= 0)
        {
            result.Add(new AnalyzedBlock(DocumentBlockType.EmptyLine, string.Empty));
            for (int i = signatureStartIndex; i < bodyLines.Count; i++)
            {
                string line = bodyLines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Парсим 2-3 части в строке подписи (например: "Дата     Подпись     Инициалы, фамилия")
                var (c1, c2, c3) = ParseSignatureColumns(line);
                result.Add(new AnalyzedBlock(DocumentBlockType.SignatureRow, line, c1, c2, c3));
            }
        }

        return result;
    }

    private static bool IsTitleLine(string line)
    {
        if (line.Length > 60)
            return false;

        string clean = line.Trim().TrimEnd('.', ':', ';', '!', '?');

        // Прямое совпадение со списком стандартных названий (в любом регистре!)
        if (StandardDocTitles.Contains(clean))
            return true;

        // Если это короткая обособленная строка полностью капсом
        if (clean.Length <= 40 && clean.Equals(clean.ToUpperInvariant(), StringComparison.Ordinal) && !clean.Contains(' '))
            return true;

        return false;
    }

    private static int FindSignatureStartIndex(List<string> lines)
    {
        // Ищем последние 1-4 строки, содержащие ключевые слова подписи
        // (но не основной текст со словом "родителя"!)
        for (int i = Math.Max(0, lines.Count - 4); i < lines.Count; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Строка подписи обычно короткая (< 80 символов) и содержит либо дату с подписью, либо подчеркивания
            if (line.Length < 90 && (line.Contains("___") || line.Contains("/ ") || 
                (line.Contains("Подпись", StringComparison.OrdinalIgnoreCase) && (line.Contains("Дата", StringComparison.OrdinalIgnoreCase) || line.Contains("фамилия", StringComparison.OrdinalIgnoreCase) || line.Length < 30))))
            {
                return i;
            }
        }

        return -1;
    }

    private static (string c1, string c2, string c3) ParseSignatureColumns(string line)
    {
        // Разбиваем по нескольким пробелам или табуляциям
        var parts = Regex.Split(line, @"\s{2,}|\t")
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();

        if (parts.Count >= 3)
            return (parts[0], parts[1], string.Join(" ", parts.Skip(2)));

        if (parts.Count == 2)
            return (parts[0], "__________", parts[1]);

        return (line, "__________", string.Empty);
    }

    private static List<string> NormalizeParagraphs(List<string> lines)
    {
        var result = new List<string>();
        string current = string.Empty;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!string.IsNullOrEmpty(current)) { result.Add(current); current = string.Empty; }
                result.Add(string.Empty);
                continue;
            }

            if (NumberedListRegex.IsMatch(line))
            {
                if (!string.IsNullOrEmpty(current)) { result.Add(current); current = string.Empty; }
                result.Add(line);
                continue;
            }

            if (string.IsNullOrEmpty(current))
            {
                current = line;
            }
            else
            {
                char last = current[^1];
                if (last != '.' && last != ':' && last != '!' && last != '?' && last != ';')
                {
                    current += " " + line;
                }
                else
                {
                    result.Add(current);
                    current = line;
                }
            }
        }

        if (!string.IsNullOrEmpty(current))
            result.Add(current);

        return result;
    }
}
