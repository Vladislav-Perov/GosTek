/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Интеллектуальная классификация типов документов по распознанному тексту OCR
 * СОДЕРЖИМОЕ: IDocumentClassifier, DocumentClassifier, DocumentClassification
 * ЗАВИСИМОСТИ: System.Text.RegularExpressions
 * ==============================================================================
 */
using System.Text.RegularExpressions;

namespace GosTek.Services;

/// <summary>
/// Результат определения типа и категории документа по тексту.
/// </summary>
public record DocumentClassification
{
    public bool IsDetected { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string MatchedKeyword { get; init; } = string.Empty;
    public double Confidence { get; init; }

    public static DocumentClassification Undetected => new() {
        IsDetected = false,
        Confidence = 0.0
    };
}

/// <summary>
/// Контракт классификатора документов по OCR-тексту.
/// </summary>
public interface IDocumentClassifier
{
    DocumentClassification Classify(string? text);
    string? DetectCategory(string? text);
}

/// <summary>
/// Сервис классификации документов на основе семантического анализа ключевых слов и заголовков.
/// </summary>
public class DocumentClassifier : IDocumentClassifier
{
    public static IDocumentClassifier Instance { get; set; } = new DocumentClassifier();

    private sealed record RuleDefinition(
        string DocumentType,
        string Category,
        string[] Phrases,
        string[] Words
    );

    // Правила упорядочены от частных/длинных формулировок к более общим
    private static readonly RuleDefinition[] Rules =
    {
        new(
            DocumentType: "Дипломный проект",
            Category: "Учебные работы, БГТУ",
            Phrases: new[] { "ДИПЛОМНЫЙ ПРОЕКТ", "ДИПЛОМНАЯ РАБОТА", "ВЫПУСКНАЯ КВАЛИФИКАЦИОННАЯ РАБОТА", "ВКР" },
            Words: Array.Empty<string>()
        ),
        new(
            DocumentType: "Курсовая работа",
            Category: "Учебные работы, БГТУ",
            Phrases: new[] { "КУРСОВАЯ РАБОТА", "КУРСОВОЙ ПРОЕКТ", "ПОЯСНИТЕЛЬНАЯ ЗАПИСКА" },
            Words: Array.Empty<string>()
        ),
        new(
            DocumentType: "Объяснительная",
            Category: "Заявления и бланки",
            Phrases: new[] { "ОБЪЯСНИТЕЛЬНАЯ ЗАПИСКА", "ДОКЛАДНАЯ ЗАПИСКА" },
            Words: new[] { "ОБЪЯСНИТЕЛЬНАЯ", "ДОКЛАДНАЯ" }
        ),
        new(
            DocumentType: "Заявление",
            Category: "Заявления и бланки",
            Phrases: new[] { "ПРОШУ ПРЕДОСТАВИТЬ", "ПРОШУ ВАС", "ЗАЯВЛЕНИЕ О РЕГИСТРАЦИИ", "ЗАЯВЛЕНИЕ О ПРИЕМЕ", "ИСКОВОЕ ЗАЯВЛЕНИЕ" },
            Words: new[] { "ЗАЯВЛЕНИЕ", "ЗАЯВИТЕЛЬ" }
        ),
        new(
            DocumentType: "Договор",
            Category: "Договоры",
            Phrases: new[] { "ДОГОВОР АРЕНДЫ", "ДОГОВОР ПОСТАВКИ", "ДОГОВОР ОКАЗАНИЯ УСЛУГ", "ТРУДОВОЙ ДОГОВОР", "ДОПОЛНИТЕЛЬНОЕ СОГЛАШЕНИЕ" },
            Words: new[] { "ДОГОВОР", "КОНТРАКТ", "СОГЛАШЕНИЕ" }
        ),
        new(
            DocumentType: "Приказ",
            Category: "Приказы",
            Phrases: new[] { "ПРИКАЗЫВАЮ", "РАСПОРЯЖЕНИЕ РУКОВОДИТЕЛЯ" },
            Words: new[] { "ПРИКАЗ", "РАСПОРЯЖЕНИЕ", "ПОСТАНОВЛЕНИЕ" }
        ),
        new(
            DocumentType: "Акт",
            Category: "Акты",
            Phrases: new[] { "АКТ ПРИЕМА-ПЕРЕДАЧИ", "АКТ СДАЧИ-ПРИЕМКИ", "АКТ ОБСЛЕДОВАНИЯ", "АКТ ВЫПОЛНЕННЫХ РАБОТ", "ДЕФЕКТНЫЙ АКТ" },
            Words: new[] { "АКТ" }
        ),
        new(
            DocumentType: "Справка",
            Category: "Справки",
            Phrases: new[] { "СПРАВКА-ВЫЗОВ", "СПРАВКА О ДОХОДАХ", "СПРАВКА С МЕСТА РАБОТЫ", "НАСТОЯЩИМ УДОСТОВЕРЯЕТСЯ" },
            Words: new[] { "СПРАВКА" }
        ),
        new(
            DocumentType: "Отчет",
            Category: "Отчеты",
            Phrases: new[] { "ОТЧЕТ ПО ПРАКТИКЕ", "ОТЧЁТ ПО ПРАКТИКЕ", "ОТЧЕТ О РЕЗУЛЬТАТАХ", "ОТЧЁТ О РЕЗУЛЬТАТАХ" },
            Words: new[] { "ОТЧЕТ", "ОТЧЁТ" }
        ),
        new(
            DocumentType: "Устав",
            Category: "Учредительные документы",
            Phrases: new[] { "УСТАВ ОБЩЕСТВА", "УЧРЕДИТЕЛЬНЫЙ ДОГОВОР" },
            Words: new[] { "УСТАВ" }
        )
    };

    public DocumentClassification Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return DocumentClassification.Undetected;

        // Нормализация текста: удаление множественных пробелов и перевод в верхний регистр
        var normalized = Regex.Replace(text, @"\s+", " ").Trim().ToUpperInvariant();

        // 1. Поиск точных многословных фраз (высокий приоритет и надежность)
        foreach (var rule in Rules) {
            foreach (var phrase in rule.Phrases) {
                if (ContainsWordOrPhrase(normalized, phrase)) {
                    bool inHeader = IsInHeader(normalized, phrase);
                    double confidence = inHeader ? 0.95 : 0.85;

                    return new DocumentClassification {
                        IsDetected = true,
                        DocumentType = rule.DocumentType,
                        Category = rule.Category,
                        MatchedKeyword = phrase,
                        Confidence = confidence
                    };
                }
            }
        }

        // 2. Поиск отдельных ключевых слов с контролем границ слов
        foreach (var rule in Rules) {
            foreach (var word in rule.Words) {
                if (ContainsWordOrPhrase(normalized, word)) {
                    bool inHeader = IsInHeader(normalized, word);
                    double confidence = inHeader ? 0.90 : 0.75;

                    return new DocumentClassification {
                        IsDetected = true,
                        DocumentType = rule.DocumentType,
                        Category = rule.Category,
                        MatchedKeyword = word,
                        Confidence = confidence
                    };
                }
            }
        }

        return DocumentClassification.Undetected;
    }

    public string? DetectCategory(string? text)
    {
        var result = Classify(text);
        return result.IsDetected ? result.Category : null;
    }

    private static bool ContainsWordOrPhrase(string text, string phraseOrWord)
    {
        // Проверка с соблюдением границ слов для кириллицы и латиницы
        // (?<![\p{L}\p{Nd}]) и (?![\p{L}\p{Nd}]) гарантируют, что "АКТ" не найдётся внутри "КОНТАКТ" или "ФАКТИЧЕСКИ"
        string pattern = $@"(?<![\p{{L}}\p{{Nd}}]){Regex.Escape(phraseOrWord)}(?![\p{{L}}\p{{Nd}}])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsInHeader(string text, string term)
    {
        // Первые 300 символов считаются областью заголовка документа
        int searchLen = Math.Min(300, text.Length);
        var headerSub = text[..searchLen];
        return headerSub.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
