/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Сервис экспорта оцифрованного скана в профессиональный Word-документ (.docx)
 *             с соблюдением структуры ГОСТ и пользовательских настроек форматирования.
 * СОДЕРЖИМОЕ: GostDocxExporter
 * ЗАВИСИМОСТИ: DocumentFormat.OpenXml, GosTek.Models.GostFormattingOptions, UniversalDocumentParser
 * ==============================================================================
 */
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GosTek.Models;
using FontSize = DocumentFormat.OpenXml.Wordprocessing.FontSize;

namespace GosTek.Services;

public static class GostDocxExporter
{
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

        // Конвертация полей страницы из мм в dxa: 1 мм ≈ 56.7 dxa
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

        // Универсальный структурный парсер документа (Зоны 1-4)
        var blocks = UniversalDocumentParser.Parse(text ?? string.Empty);

        foreach (var block in blocks)
        {
            switch (block.Type)
            {
                case DocumentBlockType.HeaderRight:
                    // Шапка документа: правая половина страницы (отступ слева 8.5 см = 4820 dxa)
                    // Внутри выравнивание по левому краю по ГОСТ
                    AddHeaderRightParagraph(body, block.Text, options);
                    break;

                case DocumentBlockType.Title:
                    // Заголовок (Заявление, Приказ...): по центру, жирный, без красной строки
                    AddParagraph(body, block.Text, options, JustificationValues.Center, isBold: true, isFirstLineIndent: false);
                    break;

                case DocumentBlockType.ListItem:
                    // Элемент списка: отступ 1.25 см + висячая строка
                    AddListParagraph(body, block.Text, options);
                    break;

                case DocumentBlockType.SignatureRow:
                    // Подвал с подписями: невидимая таблица на 2-3 колонки
                    AddSignatureTable(body, block, options);
                    break;

                case DocumentBlockType.EmptyLine:
                    AddParagraph(body, string.Empty, options, JustificationValues.Left, isFirstLineIndent: false);
                    break;

                case DocumentBlockType.Paragraph:
                default:
                    // Основной текст: выравнивание по ширине, красная строка 1.25 см
                    var alignVal = options.Alignment.Equals("Left", StringComparison.OrdinalIgnoreCase)
                        ? JustificationValues.Left
                        : JustificationValues.Both;
                    AddParagraph(body, block.Text, options, alignVal, isFirstLineIndent: true);
                    break;
            }
        }

        mainPart.Document.Save();
    }

    private static void AddHeaderRightParagraph(Body body, string text, GostFormattingOptions options)
    {
        var p = body.AppendChild(new Paragraph());
        var pPr = p.AppendChild(new ParagraphProperties());
        
        // Выравнивание по левому краю внутри правой половины листа
        pPr.AppendChild(new Justification { Val = JustificationValues.Left });

        // Отступ слева от середины листа (8.5 см = 4820 dxa по ГОСТ)
        pPr.AppendChild(new Indentation { Left = "4820" });

        // Одинарный или компактный интервал для шапки
        int lineDxa = (int)Math.Round(240 * Math.Min(options.LineSpacing, 1.15));
        pPr.AppendChild(new SpacingBetweenLines
        {
            Line = lineDxa.ToString(),
            LineRule = LineSpacingRuleValues.Auto,
            After = "40" // небольшой отступ между строками адресата
        });

        var run = p.AppendChild(new Run());
        var rPr = run.AppendChild(new RunProperties());
        rPr.AppendChild(new RunFonts
        {
            Ascii = options.FontFamily,
            HighAnsi = options.FontFamily,
            ComplexScript = options.FontFamily
        });

        // Размер шрифта: можно на 1 пт меньше основного по ГОСТ (например 12-13 пт)
        int fontSizeHalfPts = (int)Math.Round((options.FontSizePt - 1.0) * 2.0);
        rPr.AppendChild(new FontSize { Val = fontSizeHalfPts.ToString() });

        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
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

        // Абзацный отступ (красная строка): 1.25 см = 709 dxa
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

    private static void AddSignatureTable(Body body, AnalyzedBlock block, GostFormattingOptions options)
    {
        // Невидимая таблица подписей (без рамок) на 3 колонки:
        // Колонка 1: Дата / Должность (выравнивание влево)
        // Колонка 2: Место подписи (по центру)
        // Колонка 3: Расшифровка (вправо)
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

        // Растягивание таблицы на 100% ширины
        tblPr.AppendChild(new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" });

        var row = table.AppendChild(new TableRow());

        string col1 = block.Col1 ?? block.Text;
        string col2 = block.Col2 ?? "__________";
        string col3 = block.Col3 ?? string.Empty;

        // Ячейка 1: Левая
        AddTableCell(row, col1, JustificationValues.Left, options);

        // Ячейка 2: Центр (Подпись)
        AddTableCell(row, col2, JustificationValues.Center, options);

        // Ячейка 3: Правая (Расшифровка)
        if (!string.IsNullOrEmpty(col3))
        {
            AddTableCell(row, col3, JustificationValues.Right, options);
        }
    }

    private static void AddTableCell(TableRow row, string text, JustificationValues align, GostFormattingOptions options)
    {
        var cell = row.AppendChild(new TableCell());
        var p = cell.AppendChild(new Paragraph());
        var pPr = p.AppendChild(new ParagraphProperties());
        pPr.AppendChild(new Justification { Val = align });

        var run = p.AppendChild(new Run());
        var rPr = run.AppendChild(new RunProperties());
        rPr.AppendChild(new RunFonts { Ascii = options.FontFamily, HighAnsi = options.FontFamily });
        rPr.AppendChild(new FontSize { Val = ((int)Math.Round(options.FontSizePt * 2.0)).ToString() });
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }
}
