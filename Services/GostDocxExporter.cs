/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Сервис экспорта оцифрованного скана в профессиональный Word-документ (.docx)
 *             с соблюдением структуры ГОСТ и пользовательских настроек форматирования.
 * СОДЕРЖИМОЕ: GostDocxExporter
 * ЗАВИСИМОСТИ: DocumentFormat.OpenXml, GosTek.Models.GostFormattingOptions
 * ==============================================================================
 */
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GosTek.Models;
using FontSize = DocumentFormat.OpenXml.Wordprocessing.FontSize;

namespace GosTek.Services;

public static class GostDocxExporter
{
    private static readonly Regex SignatureKeywordRegex = new(
        @"^(руководитель|директор|генеральный\s+директор|начальник|главный\s+бухгалтер|декан|заведующий|составил|подпись|исполнитель|секретарь|председатель)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NumberedListRegex = new(
        @"^(\d{1,2}[\.\)]|[a-zA-Zа-яА-Я][\.\)]|[\-–—•])\s+(.+)$",
        RegexOptions.Compiled);

    /// <summary>
    /// Создает форматированный документ Word по ГОСТ на основе входного текста и настроек.
    /// </summary>
    public static void ExportToDocx(string filePath, string text, GostFormattingOptions? options = null)
    {
        options ??= GostFormattingOptions.CreateDefault();

        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        // Конвертация полей страницы из мм в двадцатые доли пункта (dxa): 1 мм ≈ 56.7 dxa
        var sectionProps = new SectionProperties();
        var pageMargin = new PageMargin
        {
            Left = (uint)Math.Round(options.MarginLeftMm * 56.7),
            Right = (uint)Math.Round(options.MarginRightMm * 56.7),
            Top = (int)Math.Round(options.MarginTopMm * 56.7),
            Bottom = (int)Math.Round(options.MarginBottomMm * 56.7),
            Header = 720,
            Footer = 720
        };
        sectionProps.AppendChild(pageMargin);
        body.AppendChild(sectionProps);

        // Разбивка на строки и нормализация абзацев
        var lines = (text ?? string.Empty)
            .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
            .Select(l => l.Trim())
            .ToList();

        // Сборка разорванных строк скана в логические абзацы
        var normalizedParagraphs = NormalizeScanParagraphs(lines);

        bool isFirstHeaderBlock = true;

        for (int i = 0; i < normalizedParagraphs.Count; i++)
        {
            var pText = normalizedParagraphs[i];

            if (string.IsNullOrWhiteSpace(pText))
            {
                // Пустая строка
                AddParagraph(body, string.Empty, options, JustificationValues.Left, isFirstLineIndent: false);
                continue;
            }

            // 1. Проверка на шапку документа (адресат вверху страницы)
            if (isFirstHeaderBlock && i < 4 && IsHeaderBlock(pText))
            {
                AddParagraph(body, pText, options, JustificationValues.Right, isFirstLineIndent: false);
                continue;
            }
            else
            {
                isFirstHeaderBlock = false;
            }

            // 2. Проверка на заголовок документа (ПРИКАЗ, АКТ, ДОГОВОР, ЗАЯВЛЕНИЕ...)
            if (options.AutoDetectTitle && IsDocumentHeading(pText))
            {
                AddParagraph(body, pText, options, JustificationValues.Center, isBold: true, isFirstLineIndent: false);
                continue;
            }

            // 3. Проверка на блок подписи внизу
            if (options.AutoDetectSignatures && i >= normalizedParagraphs.Count - 5 && IsSignatureBlock(pText))
            {
                AddSignatureBlock(body, pText, options);
                continue;
            }

            // 4. Проверка на элемент списка
            if (options.AutoFormatLists && NumberedListRegex.IsMatch(pText))
            {
                AddListParagraph(body, pText, options);
                continue;
            }

            // 5. Стандартный абзац ГОСТ
            var alignVal = options.Alignment.Equals("Left", StringComparison.OrdinalIgnoreCase)
                ? JustificationValues.Left
                : JustificationValues.Both;

            AddParagraph(body, pText, options, alignVal, isFirstLineIndent: true);
        }

        mainPart.Document.Save();
    }

    private static List<string> NormalizeScanParagraphs(List<string> lines)
    {
        var result = new List<string>();
        string currentParagraph = string.Empty;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!string.IsNullOrEmpty(currentParagraph))
                {
                    result.Add(currentParagraph);
                    currentParagraph = string.Empty;
                }
                result.Add(string.Empty);
                continue;
            }

            // Если строка выглядит как явный новый заголовок, пункт списка или подпись
            if (IsDocumentHeading(line) || NumberedListRegex.IsMatch(line) || IsSignatureBlock(line))
            {
                if (!string.IsNullOrEmpty(currentParagraph))
                {
                    result.Add(currentParagraph);
                    currentParagraph = string.Empty;
                }
                result.Add(line);
                continue;
            }

            if (string.IsNullOrEmpty(currentParagraph))
            {
                currentParagraph = line;
            }
            else
            {
                // Если предыдущая строка не заканчивается точкой, двоеточием, восклицательным или вопросительным знаком,
                // скорее всего это разорванная строка одного абзаца
                char lastChar = currentParagraph[^1];
                if (lastChar != '.' && lastChar != ':' && lastChar != '!' && lastChar != '?' && lastChar != ';')
                {
                    currentParagraph += " " + line;
                }
                else
                {
                    result.Add(currentParagraph);
                    currentParagraph = line;
                }
            }
        }

        if (!string.IsNullOrEmpty(currentParagraph))
        {
            result.Add(currentParagraph);
        }

        return result;
    }

    private static bool IsHeaderBlock(string text)
    {
        return text.StartsWith("Кому:", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("В ", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Утверждаю", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("генеральному", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("директору", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDocumentHeading(string text)
    {
        if (text.Length > 80)
            return false;

        string upper = text.ToUpperInvariant();
        if (upper == "ПРИКАЗ" || upper == "АКТ" || upper == "ЗАЯВЛЕНИЕ" || upper == "ДОГОВОР" ||
            upper == "СЛУЖЕБНАЯ ЗАПИСКА" || upper == "ПРОТОКОЛ" || upper == "ПОСТАНОВЛЕНИЕ" ||
            upper == "РАСПОРЯЖЕНИЕ" || upper == "СОГЛАШЕНИЕ" || upper == "СПРАВКА")
        {
            return true;
        }

        return text.Length <= 45 && text.Equals(upper, StringComparison.Ordinal) && !text.Contains('.');
    }

    private static bool IsSignatureBlock(string text)
    {
        return SignatureKeywordRegex.IsMatch(text);
    }

    private static void AddParagraph(
        Body body,
        string text,
        GostFormattingOptions options,
        JustificationValues align,
        bool isBold = false,
        bool isFirstLineIndent = false)
    {
        var p = body.AppendChild(new Paragraph());
        var pPr = p.AppendChild(new ParagraphProperties());
        pPr.AppendChild(new Justification { Val = align });

        // Межстрочный интервал: line = 240 * lineSpacing (для 1.5 это 360)
        int lineDxa = (int)Math.Round(240 * options.LineSpacing);
        pPr.AppendChild(new SpacingBetweenLines
        {
            Line = lineDxa.ToString(),
            LineRule = LineSpacingRuleValues.Auto,
            After = "0"
        });

        // Абзацный отступ (красная строка): 1 см = 567 dxa (1.25 см = 709 dxa)
        if (isFirstLineIndent && options.FirstLineIndentCm > 0 && !string.IsNullOrWhiteSpace(text))
        {
            int indentDxa = (int)Math.Round(options.FirstLineIndentCm * 567.0);
            pPr.AppendChild(new Indentation { FirstLine = indentDxa.ToString() });
        }

        var run = p.AppendChild(new Run());
        var rPr = run.AppendChild(new RunProperties());
        rPr.AppendChild(new RunFonts
        {
            Ascii = options.FontFamily,
            HighAnsi = options.FontFamily,
            ComplexScript = options.FontFamily
        });

        // Размер шрифта: в полупунктах (pt * 2)
        int fontSizeHalfPts = (int)Math.Round(options.FontSizePt * 2.0);
        rPr.AppendChild(new FontSize { Val = fontSizeHalfPts.ToString() });

        if (isBold)
            rPr.AppendChild(new Bold());

        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    private static void AddListParagraph(Body body, string text, GostFormattingOptions options)
    {
        var p = body.AppendChild(new Paragraph());
        var pPr = p.AppendChild(new ParagraphProperties());
        pPr.AppendChild(new Justification { Val = JustificationValues.Both });

        int lineDxa = (int)Math.Round(240 * options.LineSpacing);
        pPr.AppendChild(new SpacingBetweenLines
        {
            Line = lineDxa.ToString(),
            LineRule = LineSpacingRuleValues.Auto,
            After = "0"
        });

        // Отступ списка: отступ слева 1.25 см + висячая строка
        int indentDxa = (int)Math.Round(options.FirstLineIndentCm * 567.0);
        pPr.AppendChild(new Indentation
        {
            Left = indentDxa.ToString(),
            Hanging = "360"
        });

        var run = p.AppendChild(new Run());
        var rPr = run.AppendChild(new RunProperties());
        rPr.AppendChild(new RunFonts
        {
            Ascii = options.FontFamily,
            HighAnsi = options.FontFamily,
            ComplexScript = options.FontFamily
        });
        int fontSizeHalfPts = (int)Math.Round(options.FontSizePt * 2.0);
        rPr.AppendChild(new FontSize { Val = fontSizeHalfPts.ToString() });

        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    private static void AddSignatureBlock(Body body, string text, GostFormattingOptions options)
    {
        // Для подписи создаем невидимую таблицу на 2 ячейки: слева должность, справа подпись и расшифровка
        var table = body.AppendChild(new Table());
        var tblPr = table.AppendChild(new TableProperties());
        tblPr.AppendChild(new TableBorders(
            new TopBorder { Val = BorderValues.None },
            new BottomBorder { Val = BorderValues.None },
            new LeftBorder { Val = BorderValues.None },
            new RightBorder { Val = BorderValues.None },
            new InsideHorizontalBorder { Val = BorderValues.None },
            new InsideVerticalBorder { Val = BorderValues.None }
        ));

        var row = table.AppendChild(new TableRow());

        // Левая ячейка: должность
        var cellLeft = row.AppendChild(new TableCell());
        var pLeft = cellLeft.AppendChild(new Paragraph());
        var pPrLeft = pLeft.AppendChild(new ParagraphProperties());
        pPrLeft.AppendChild(new Justification { Val = JustificationValues.Left });

        var runLeft = pLeft.AppendChild(new Run());
        var rPrLeft = runLeft.AppendChild(new RunProperties());
        rPrLeft.AppendChild(new RunFonts { Ascii = options.FontFamily, HighAnsi = options.FontFamily });
        rPrLeft.AppendChild(new FontSize { Val = ((int)Math.Round(options.FontSizePt * 2.0)).ToString() });
        runLeft.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

        // Правая ячейка: инициалы/подпись
        var cellRight = row.AppendChild(new TableCell());
        var pRight = cellRight.AppendChild(new Paragraph());
        var pPrRight = pRight.AppendChild(new ParagraphProperties());
        pPrRight.AppendChild(new Justification { Val = JustificationValues.Right });

        var runRight = pRight.AppendChild(new Run());
        var rPrRight = runRight.AppendChild(new RunProperties());
        rPrRight.AppendChild(new RunFonts { Ascii = options.FontFamily, HighAnsi = options.FontFamily });
        rPrRight.AppendChild(new FontSize { Val = ((int)Math.Round(options.FontSizePt * 2.0)).ToString() });
        runRight.AppendChild(new Text("___________ / ___________") { Space = SpaceProcessingModeValues.Preserve });
    }
}
