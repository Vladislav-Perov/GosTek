/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Сервис автоматического создания ГОСТ-шаблона .docx из произвольного отсканированного текста
 * СОДЕРЖИМОЕ: ScanTemplateBuilder, ScanTemplateResult, DetectedField
 * ЗАВИСИМОСТИ: DocumentFormat.OpenXml, GosTek.SettingsService, GosTek.Models.TemplateCatalog
 * ==============================================================================
 */
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GosTek.Models;
using FontSize = DocumentFormat.OpenXml.Wordprocessing.FontSize;

namespace GosTek.Services;

public sealed record DetectedField(string OriginalSnippet, string FieldTag, string Label);

public sealed record ScanTemplateResult(string Title, string Category, string FilePath, int ReplacedFieldsCount);

/// <summary>
/// Анализирует сырой текст со сканера, находит потенциальные реквизиты (ФИО, даты, паспорт, суммы, прочерки)
/// и генерирует эталонный Word-шаблон (.docx) со стилями ГОСТ и тегами {{Поле}} / [Поле].
/// </summary>
public static class ScanTemplateBuilder
{
    // Регулярные выражения для детекции типовых реквизитов
    private static readonly Regex DateRegex = new(
        @"\b(\d{1,2}[\.\/]\d{1,2}[\.\/]\d{2,4}|\d{1,2}\s+(?:января|февраля|марта|апреля|мая|июня|июля|августа|сентября|октября|ноября|декабря)\s+\d{4}(?:\s*года|\s*г\.)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PassportSeriesRegex = new(
        @"\b([A-ZА-Я]{2}\s*№?\s*\d{7})\b",
        RegexOptions.Compiled);

    private static readonly Regex PersonalIdRegex = new(
        @"\b([1-6]\d{6}[A-ZА-Я]\d{3}[A-ZА-Я]{2}\d)\b",
        RegexOptions.Compiled);

    private static readonly Regex UnderlineBlankRegex = new(
        @"_{3,}",
        RegexOptions.Compiled);

    private static readonly Regex MoneyRegex = new(
        @"\b(\d+[\s\d]*(?:[,\.]\d{2})?\s*(?:руб(?:лей|\.)?|бел\.?\s*руб\.?|BYN|USD|EUR))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UnpRegex = new(
        @"\bУНП\s*[:№]?\s*(\d{9})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Автоматически выявляет название документа по первой шапке или заголовку.
    /// </summary>
    public static string DetectDocumentTitle(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return "Новый документ";

        var lines = rawText.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Take(8))
        {
            var clean = line.Trim();
            if (clean.Length < 3 || clean.Length > 80)
                continue;

            // Ключевые слова официальных документов
            if (clean.Contains("ЗАЯВЛЕНИЕ", StringComparison.OrdinalIgnoreCase))
                return "Заявление";
            if (clean.Contains("ДОГОВОР", StringComparison.OrdinalIgnoreCase))
                return "Договор";
            if (clean.Contains("АКТ", StringComparison.OrdinalIgnoreCase))
                return "Акт";
            if (clean.Contains("СПРАВКА", StringComparison.OrdinalIgnoreCase))
                return "Справка";
            if (clean.Contains("ОБЪЯСНИТЕЛЬНАЯ", StringComparison.OrdinalIgnoreCase))
                return "Объяснительная записка";
            if (clean.Contains("ДОВЕРЕННОСТЬ", StringComparison.OrdinalIgnoreCase))
                return "Доверенность";
            if (clean.Contains("РАСПИСКА", StringComparison.OrdinalIgnoreCase))
                return "Расписка";
            if (clean.Contains("ПРИКАЗ", StringComparison.OrdinalIgnoreCase))
                return "Приказ";
            if (clean.Contains("УВЕДОМЛЕНИЕ", StringComparison.OrdinalIgnoreCase))
                return "Уведомление";
            if (clean.Contains("СЛУЖЕБНАЯ ЗАПИСКА", StringComparison.OrdinalIgnoreCase))
                return "Служебная записка";
        }

        // Если не найдено ключевое слово, берем первую непустую строку достаточной длины
        var firstValid = lines.FirstOrDefault(l => l.Length is >= 4 and <= 50);
        return !string.IsNullOrWhiteSpace(firstValid) ? firstValid.Trim() : "Пользовательский документ";
    }

    /// <summary>
    /// Заменяет динамические сущности в тексте на теги шаблона.
    /// </summary>
    public static (string ParameterizedText, List<DetectedField> Fields) ParameterizeText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return (string.Empty, new List<DetectedField>());

        var fields = new List<DetectedField>();
        var result = rawText;

        // 1. Идентификационный номер РБ
        result = PersonalIdRegex.Replace(result, m =>
        {
            string tag = "{{ЛичныйНомер}}";
            if (!fields.Any(f => f.FieldTag == tag))
                fields.Add(new DetectedField(m.Value, tag, "Личный номер"));
            return tag;
        });

        // 2. Паспортные данные
        result = PassportSeriesRegex.Replace(result, m =>
        {
            string tag = "{{Паспорт}}";
            if (!fields.Any(f => f.FieldTag == tag))
                fields.Add(new DetectedField(m.Value, tag, "Паспорт (серия и номер)"));
            return tag;
        });

        // 3. УНП
        result = UnpRegex.Replace(result, m =>
        {
            string tag = "УНП {{УНП}}";
            if (!fields.Any(f => f.FieldTag == "{{УНП}}"))
                fields.Add(new DetectedField(m.Groups[1].Value, "{{УНП}}", "УНП"));
            return tag;
        });

        // 4. Денежные суммы
        result = MoneyRegex.Replace(result, m =>
        {
            string tag = "[Сумма]";
            if (!fields.Any(f => f.FieldTag == tag))
                fields.Add(new DetectedField(m.Value, tag, "Сумма"));
            return tag;
        });

        // 5. Даты
        result = DateRegex.Replace(result, m =>
        {
            string tag = "{{Дата}}";
            if (!fields.Any(f => f.FieldTag == tag))
                fields.Add(new DetectedField(m.Value, tag, "Дата"));
            return tag;
        });

        // 6. Прочерки вида ___________ -> [Заполнить]
        int blankIndex = 1;
        result = UnderlineBlankRegex.Replace(result, m =>
        {
            string tag = blankIndex == 1 ? "[Поле для заполнения]" : $"[Поле {blankIndex}]";
            fields.Add(new DetectedField(m.Value, tag, $"Поле {blankIndex}"));
            blankIndex++;
            return tag;
        });

        return (result, fields);
    }

    /// <summary>
    /// Создает полноценный файл .docx по ГОСТ со стилями и тегами и регистрирует его в каталоге.
    /// </summary>
    public static ScanTemplateResult BuildAndSaveTemplate(string rawText, string? customTitle = null, string category = "Мои сканы", string? outputFolder = null)
    {
        string title = !string.IsNullOrWhiteSpace(customTitle)
            ? customTitle.Trim()
            : DetectDocumentTitle(rawText);

        // Формируем безопасное имя файла
        string safeTitle = Regex.Replace(title, @"[\\/:*?""<>|]", "_");
        string safeCategory = Regex.Replace(category, @"[\\/:*?""<>|]", "_");

        string templatesFolder = !string.IsNullOrWhiteSpace(outputFolder)
            ? outputFolder
            : SettingsService.TemplatesFolder;
        if (!Directory.Exists(templatesFolder))
            Directory.CreateDirectory(templatesFolder);

        string categoryDir = Path.Combine(templatesFolder, safeCategory);
        if (!Directory.Exists(categoryDir))
            Directory.CreateDirectory(categoryDir);

        string fileName = $"{safeCategory}_{safeTitle}.docx";
        string filePath = Path.Combine(categoryDir, fileName);

        // Если файл с таким именем есть — добавляем таймстемп
        if (File.Exists(filePath))
        {
            fileName = $"{safeCategory}_{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.docx";
            filePath = Path.Combine(categoryDir, fileName);
        }

        var (parameterizedText, fields) = ParameterizeText(rawText);

        CreateDocxWithGostStyles(filePath, title, parameterizedText);

        // Обновляем общий каталог шаблонов приложения (если не в тестовой среде)
        try
        {
            TemplateCatalog.Refresh();
        }
        catch
        {
            // Игнорируем ошибки доступа к Preferences в unit-тестах
        }

        return new ScanTemplateResult(title, safeCategory, filePath, fields.Count);
    }

    /// <summary>
    /// Создает разметку OpenXML документа с соблюдением требований ГОСТ.
    /// </summary>
    private static void CreateDocxWithGostStyles(string path, string title, string bodyText)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        // Настройка полей по ГОСТ: левое 30 мм (1701 dxa), правое 15 мм (850 dxa), верх 20 мм (1134 dxa), низ 20 мм (1134 dxa)
        var sectionProps = new SectionProperties();
        var pageMargin = new PageMargin
        {
            Top = 1134,
            Bottom = 1134,
            Left = 1701,
            Right = 850,
            Header = 720,
            Footer = 720
        };
        sectionProps.AppendChild(pageMargin);
        body.AppendChild(sectionProps);

        // Разбиваем текст на абзацы
        var paragraphs = bodyText.Split('\n', StringSplitOptions.TrimEntries);

        foreach (var text in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                // Пустая строка-отступ
                AddGostParagraph(body, string.Empty, JustificationValues.Left);
                continue;
            }

            // Центрируем заголовки, если строка короткая и написана капсом
            bool isHeading = text.Equals(text.ToUpperInvariant(), StringComparison.Ordinal) && text.Length <= 40;
            var align = isHeading ? JustificationValues.Center : JustificationValues.Left;

            AddGostParagraph(body, text, align, isBold: isHeading);
        }

        mainPart.Document.Save();
    }

    private static void AddGostParagraph(Body body, string text, JustificationValues align, bool isBold = false)
    {
        var p = body.AppendChild(new Paragraph());
        var pPr = p.AppendChild(new ParagraphProperties());
        pPr.AppendChild(new Justification { Val = align });

        // ГОСТ: полуторный интервал (line = 360), отступ после абзаца 0-6 pt
        pPr.AppendChild(new SpacingBetweenLines
        {
            Line = "360",
            LineRule = LineSpacingRuleValues.Auto,
            After = "120"
        });

        // Абзацный отступ по ГОСТ (1.25 см = 709 dxa) для обычного текста, не для заголовков по центру
        if (align == JustificationValues.Left && !string.IsNullOrWhiteSpace(text))
        {
            pPr.AppendChild(new Indentation { FirstLine = "709" });
        }

        var run = p.AppendChild(new Run());
        var rPr = run.AppendChild(new RunProperties());
        rPr.AppendChild(new RunFonts
        {
            Ascii = "Times New Roman",
            HighAnsi = "Times New Roman",
            ComplexScript = "Times New Roman"
        });
        rPr.AppendChild(new FontSize { Val = "28" }); // 14 pt по ГОСТ

        if (isBold)
            rPr.AppendChild(new Bold());

        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }
}
