/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Набор модульных тестов для движка инспекции ГОСТ (DocumentChecker)
 * СОДЕРЖИМОЕ: DocumentCheckerTests
 * ЗАВИСИМОСТИ: xUnit, GosTek.Services.DocumentChecker, DocumentFormat.OpenXml
 * ==============================================================================
 */
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GosTek.Models;
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class DocumentCheckerTests
{
    private static string CreateDocWithSection(Action<Body, SectionProperties> populate)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"check_doc_{Guid.NewGuid():N}.docx");
        using (var doc = WordprocessingDocument.Create(tempFile, WordprocessingDocumentType.Document)) {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            var secProps = new SectionProperties();
            populate(body, secProps);
            body.AppendChild(secProps);
            mainPart.Document.Save();
        }
        return tempFile;
    }

    [Fact]
    public void Check_InvalidMargins_ReportsMarginIssues()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            // Левое поле 1 см (567 twips) вместо ГОСТ 3 см (~1701 twips)
            var margin = new PageMargin {
                Left = 567,   // ~1.0 см
                Right = 567,  // ~1.0 см
                Top = 1134,   // ~2.0 см
                Bottom = 1134 // ~2.0 см
            };
            secProps.AppendChild(margin);

            var p = body.AppendChild(new Paragraph());
            var r = p.AppendChild(new Run());
            r.AppendChild(new Text("Тестовый абзац для проверки полей."));
        });

        try {
            var rules = RuleSets.Universal;
            var issues = DocumentChecker.Check(tempFile, rules);

            var marginIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.MarginLeft);
            Assert.NotNull(marginIssue);
            Assert.True(marginIssue.HasDiff);
            Assert.Equal("1 см", marginIssue.Actual);
            Assert.Equal("3 см", marginIssue.Expected);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Check_InvalidFontName_ReportsFontIssue()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            var p = body.AppendChild(new Paragraph());
            var r = p.AppendChild(new Run());
            var rPr = r.AppendChild(new RunProperties());
            rPr.AppendChild(new RunFonts { Ascii = "Arial", HighAnsi = "Arial" });
            r.AppendChild(new Text("Текст, набранный шрифтом Arial вместо Times New Roman."));
        });

        try {
            var rules = RuleSets.Universal; // Ожидает Times New Roman
            var issues = DocumentChecker.Check(tempFile, rules);

            var fontIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.FontName);
            Assert.NotNull(fontIssue);
            Assert.True(fontIssue.HasDiff);
            Assert.Equal("Arial", fontIssue.Actual);
            Assert.Equal("Times New Roman", fontIssue.Expected);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Check_InvalidFontSize_ReportsFontSizeIssue()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            var p = body.AppendChild(new Paragraph());
            var r = p.AppendChild(new Run());
            var rPr = r.AppendChild(new RunProperties());
            rPr.AppendChild(new RunFonts { Ascii = "Times New Roman" });
            // Размер 10 pt (20 полупунктов) вместо 14 pt (28 полупунктов)
            rPr.AppendChild(new FontSize { Val = "20" });
            r.AppendChild(new Text("Текст слишком мелкого кегля для ГОСТ."));
        });

        try {
            var rules = RuleSets.Universal;
            var issues = DocumentChecker.Check(tempFile, rules);

            var sizeIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.FontSize);
            Assert.NotNull(sizeIssue);
            Assert.True(sizeIssue.HasDiff);
            Assert.Equal("10 pt", sizeIssue.Actual);
            Assert.Equal("14 pt", sizeIssue.Expected);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Check_InvalidLineSpacing_ReportsSpacingIssue()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            var p = body.AppendChild(new Paragraph());
            var pPr = p.AppendChild(new ParagraphProperties());
            // Одинарный интервал (240) вместо полуторного (360)
            pPr.AppendChild(new SpacingBetweenLines { Line = "240", LineRule = LineSpacingRuleValues.Auto });

            var r = p.AppendChild(new Run());
            var rPr = r.AppendChild(new RunProperties());
            rPr.AppendChild(new RunFonts { Ascii = "Times New Roman" });
            rPr.AppendChild(new FontSize { Val = "28" });
            r.AppendChild(new Text("Текст с одинарным межстрочным интервалом."));
        });

        try {
            var rules = RuleSets.Universal;
            var issues = DocumentChecker.Check(tempFile, rules);

            var spIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.LineSpacing);
            Assert.NotNull(spIssue);
            Assert.True(spIssue.HasDiff);
            Assert.Equal("1", spIssue.Actual);
            Assert.Equal("1,5", spIssue.Expected);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Check_MissingPageNumbers_ReportsPageNumberIssue()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            var p = body.AppendChild(new Paragraph());
            var r = p.AppendChild(new Run());
            r.AppendChild(new Text("Документ без колонтитулов и без поля номера страницы."));
        });

        try {
            var rules = RuleSets.Universal with { RequirePageNumbers = true };
            var issues = DocumentChecker.Check(tempFile, rules);

            var pageIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.PageNumbers);
            Assert.NotNull(pageIssue);
            Assert.True(pageIssue.HasDiff);
            Assert.Equal("Отсутствует", pageIssue.Actual);
            Assert.Equal("Внизу по центру", pageIssue.Expected);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // =====================================================================
    //  ТЕСТЫ ТОЧЕЧНОГО ИСПРАВЛЕНИЯ (FixItems)
    // =====================================================================

    [Fact]
    public void FixItems_Margins_FixesOnlySelectedSection()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"check_doc_{Guid.NewGuid():N}.docx");
        using (var doc = WordprocessingDocument.Create(tempFile, WordprocessingDocumentType.Document)) {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // 3 раздела с левым полем 1.0 см (567 twips)
            var p1 = body.AppendChild(new Paragraph(new Run(new Text("Раздел 1 текст"))));
            var pPr1 = p1.AppendChild(new ParagraphProperties());
            pPr1.AppendChild(new SectionProperties(new PageMargin { Left = 567, Right = 567, Top = 1134, Bottom = 1134 }));

            var p2 = body.AppendChild(new Paragraph(new Run(new Text("Раздел 2 текст"))));
            var pPr2 = p2.AppendChild(new ParagraphProperties());
            pPr2.AppendChild(new SectionProperties(new PageMargin { Left = 567, Right = 567, Top = 1134, Bottom = 1134 }));

            var p3 = body.AppendChild(new Paragraph(new Run(new Text("Раздел 3 текст"))));
            var secProps3 = new SectionProperties(new PageMargin { Left = 567, Right = 567, Top = 1134, Bottom = 1134 });
            body.AppendChild(secProps3);

            mainPart.Document.Save();
        }

        try {
            var rules = RuleSets.Universal;
            var issues = DocumentChecker.Check(tempFile, rules);
            var marginIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.MarginLeft);
            Assert.NotNull(marginIssue);
            Assert.NotNull(marginIssue.Items);
            Assert.Equal(3, marginIssue.Items.Count);

            // Исправляем только раздел 2
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.MarginLeft, new HashSet<int> { 2 });
            Assert.Equal(1, changed);

            // Проверяем документ: раздел 2 исправлен (3 см ~ 1701 twips), разделы 1 и 3 остались 567
            using var doc = WordprocessingDocument.Open(tempFile, false);
            var secs = doc.MainDocumentPart!.Document.Body!.Descendants<SectionProperties>().ToList();
            Assert.Equal(3, secs.Count);
            Assert.Equal((uint)567, secs[0].GetFirstChild<PageMargin>()!.Left!.Value);
            Assert.Equal((uint)Math.Round(rules.Left.Cm * (1440 / 2.54)), secs[1].GetFirstChild<PageMargin>()!.Left!.Value);
            Assert.Equal((uint)567, secs[2].GetFirstChild<PageMargin>()!.Left!.Value);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_FontName_FixesOnlySelectedParagraph()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            for (int i = 0; i < 3; i++) {
                var p = body.AppendChild(new Paragraph());
                var r = p.AppendChild(new Run());
                var rPr = r.AppendChild(new RunProperties());
                rPr.AppendChild(new RunFonts { Ascii = "Arial", HighAnsi = "Arial" });
                r.AppendChild(new Text($"Абзац {i + 1} со шрифтом Arial."));
            }
        });

        try {
            var rules = RuleSets.Universal;
            var issues = DocumentChecker.Check(tempFile, rules);
            var fontIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.FontName);
            Assert.NotNull(fontIssue);
            Assert.NotNull(fontIssue.Items);
            Assert.Equal(3, fontIssue.Items.Count);

            // Исправляем только второй абзац (индекс 1)
            int targetIndex = fontIssue.Items[1].Index;
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.FontName, new HashSet<int> { targetIndex });
            Assert.Equal(1, changed);

            using var doc = WordprocessingDocument.Open(tempFile, false);
            var paras = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().ToList();
            Assert.Equal("Arial", paras[0].Descendants<RunFonts>().First().Ascii!.Value);
            Assert.Equal("Times New Roman", paras[1].Descendants<RunFonts>().First().Ascii!.Value);
            Assert.Equal("Arial", paras[2].Descendants<RunFonts>().First().Ascii!.Value);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_FontSize_FixesOnlySelectedParagraph()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            for (int i = 0; i < 3; i++) {
                var p = body.AppendChild(new Paragraph());
                var r = p.AppendChild(new Run());
                var rPr = r.AppendChild(new RunProperties());
                rPr.AppendChild(new RunFonts { Ascii = "Times New Roman" });
                rPr.AppendChild(new FontSize { Val = "20" }); // 10 pt
                r.AppendChild(new Text($"Абзац {i + 1} с размером 10 pt."));
            }
        });

        try {
            var rules = RuleSets.Universal;
            var issues = DocumentChecker.Check(tempFile, rules);
            var sizeIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.FontSize);
            Assert.NotNull(sizeIssue);
            Assert.NotNull(sizeIssue.Items);
            Assert.Equal(3, sizeIssue.Items.Count);

            // Исправляем только первый абзац
            int targetIndex = sizeIssue.Items[0].Index;
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.FontSize, new HashSet<int> { targetIndex });
            Assert.Equal(1, changed);

            using var doc = WordprocessingDocument.Open(tempFile, false);
            var paras = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().ToList();
            Assert.Equal("28", paras[0].Descendants<FontSize>().First().Val!.Value); // 14 pt
            Assert.Equal("20", paras[1].Descendants<FontSize>().First().Val!.Value); // 10 pt
            Assert.Equal("20", paras[2].Descendants<FontSize>().First().Val!.Value); // 10 pt
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_LineSpacing_FixesOnlySelectedParagraph()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            for (int i = 0; i < 3; i++) {
                var p = body.AppendChild(new Paragraph());
                var pPr = p.AppendChild(new ParagraphProperties());
                pPr.AppendChild(new SpacingBetweenLines { Line = "240", LineRule = LineSpacingRuleValues.Auto });
                var r = p.AppendChild(new Run(new Text($"Абзац {i + 1} одинарный интервал.")));
            }
        });

        try {
            var rules = RuleSets.Universal;
            var issues = DocumentChecker.Check(tempFile, rules);
            var spIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.LineSpacing);
            Assert.NotNull(spIssue);
            Assert.NotNull(spIssue.Items);
            Assert.Equal(3, spIssue.Items.Count);

            int targetIndex = spIssue.Items[2].Index;
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.LineSpacing, new HashSet<int> { targetIndex });
            Assert.Equal(1, changed);

            using var doc = WordprocessingDocument.Open(tempFile, false);
            var paras = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().ToList();
            Assert.Equal("240", paras[0].ParagraphProperties!.SpacingBetweenLines!.Line!.Value);
            Assert.Equal("240", paras[1].ParagraphProperties!.SpacingBetweenLines!.Line!.Value);
            Assert.Equal("360", paras[2].ParagraphProperties!.SpacingBetweenLines!.Line!.Value); // 1.5 интервал
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_Alignment_FixesOnlySelectedParagraph()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            for (int i = 0; i < 3; i++) {
                var p = body.AppendChild(new Paragraph());
                var pPr = p.AppendChild(new ParagraphProperties());
                pPr.AppendChild(new Justification { Val = JustificationValues.Left });
                p.AppendChild(new Run(new Text($"Абзац {i + 1} выравнивание по левому краю.")));
            }
        });

        try {
            var rules = RuleSets.Universal;
            var issues = DocumentChecker.Check(tempFile, rules);
            var alignIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.Alignment);
            Assert.NotNull(alignIssue);
            Assert.NotNull(alignIssue.Items);
            Assert.Equal(3, alignIssue.Items.Count);

            int targetIndex = alignIssue.Items[1].Index;
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.Alignment, new HashSet<int> { targetIndex });
            Assert.Equal(1, changed);

            using var doc = WordprocessingDocument.Open(tempFile, false);
            var paras = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().ToList();
            Assert.Equal(JustificationValues.Left, paras[0].ParagraphProperties!.Justification!.Val!.Value);
            Assert.Equal(JustificationValues.Both, paras[1].ParagraphProperties!.Justification!.Val!.Value);
            Assert.Equal(JustificationValues.Left, paras[2].ParagraphProperties!.Justification!.Val!.Value);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_FirstLineIndent_FixesOnlySelectedParagraph()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            for (int i = 0; i < 3; i++) {
                var p = body.AppendChild(new Paragraph());
                var pPr = p.AppendChild(new ParagraphProperties());
                pPr.AppendChild(new Indentation { FirstLine = "0" });
                p.AppendChild(new Run(new Text($"Абзац {i + 1} без отступа красной строки.")));
            }
        });

        try {
            var rules = RuleSets.Universal;
            var issues = DocumentChecker.Check(tempFile, rules);
            var indIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.FirstLineIndent);
            Assert.NotNull(indIssue);
            Assert.NotNull(indIssue.Items);
            Assert.Equal(3, indIssue.Items.Count);

            int targetIndex = indIssue.Items[1].Index;
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.FirstLineIndent, new HashSet<int> { targetIndex });
            Assert.Equal(1, changed);

            using var doc = WordprocessingDocument.Open(tempFile, false);
            var paras = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().ToList();
            Assert.Equal("0", paras[0].ParagraphProperties!.Indentation!.FirstLine!.Value);
            Assert.NotEqual("0", paras[1].ParagraphProperties!.Indentation!.FirstLine!.Value);
            Assert.Equal("0", paras[2].ParagraphProperties!.Indentation!.FirstLine!.Value);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_SpacingBeforeAfter_FixesOnlySelectedParagraph()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            for (int i = 0; i < 3; i++) {
                var p = body.AppendChild(new Paragraph());
                var pPr = p.AppendChild(new ParagraphProperties());
                pPr.AppendChild(new SpacingBetweenLines { Before = "240", After = "240" });
                p.AppendChild(new Run(new Text($"Абзац {i + 1} с отступами до и после.")));
            }
        });

        try {
            var rules = RuleSets.Universal;
            var issues = DocumentChecker.Check(tempFile, rules);
            var spIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.SpacingBeforeAfter);
            Assert.NotNull(spIssue);
            Assert.NotNull(spIssue.Items);
            Assert.Equal(3, spIssue.Items.Count);

            int targetIndex = spIssue.Items[1].Index;
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.SpacingBeforeAfter, new HashSet<int> { targetIndex });
            Assert.Equal(1, changed);

            using var doc = WordprocessingDocument.Open(tempFile, false);
            var paras = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().ToList();
            Assert.Equal("240", paras[0].ParagraphProperties!.SpacingBetweenLines!.Before!.Value);
            Assert.Equal("0", paras[1].ParagraphProperties!.SpacingBetweenLines!.Before!.Value);
            Assert.Equal("0", paras[1].ParagraphProperties!.SpacingBetweenLines!.After!.Value);
            Assert.Equal("240", paras[2].ParagraphProperties!.SpacingBetweenLines!.Before!.Value);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_WidowControl_FixesOnlySelectedParagraph()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            for (int i = 0; i < 3; i++) {
                var p = body.AppendChild(new Paragraph());
                var pPr = p.AppendChild(new ParagraphProperties());
                pPr.AppendChild(new WidowControl { Val = false });
                p.AppendChild(new Run(new Text($"Абзац {i + 1} с отключенным контролем висячих строк.")));
            }
        });

        try {
            var rules = RuleSets.Universal;
            var issues = DocumentChecker.Check(tempFile, rules);
            var wcIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.WidowControl);
            Assert.NotNull(wcIssue);
            Assert.NotNull(wcIssue.Items);
            Assert.Equal(3, wcIssue.Items.Count);

            int targetIndex = wcIssue.Items[1].Index;
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.WidowControl, new HashSet<int> { targetIndex });
            Assert.Equal(1, changed);

            using var doc = WordprocessingDocument.Open(tempFile, false);
            var paras = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().ToList();
            Assert.False(paras[0].ParagraphProperties!.WidowControl!.Val!.Value);
            Assert.True(paras[1].ParagraphProperties!.WidowControl!.Val!.Value);
            Assert.False(paras[2].ParagraphProperties!.WidowControl!.Val!.Value);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_TableFormatting_FixesOnlySelectedCell()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            var table = body.AppendChild(new Table());
            for (int i = 0; i < 3; i++) {
                var tr = table.AppendChild(new TableRow());
                var tc = tr.AppendChild(new TableCell());
                var p = tc.AppendChild(new Paragraph());
                var r = p.AppendChild(new Run());
                var rPr = r.AppendChild(new RunProperties());
                rPr.AppendChild(new FontSize { Val = "20" }); // 10 pt
                r.AppendChild(new Text($"Ячейка {i + 1} таблицы"));
            }
        });

        try {
            var rules = RuleSets.Universal with { CheckTables = true, TableFontSizePt = 12.0 };
            var issues = DocumentChecker.Check(tempFile, rules);
            var tableIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.TableFormatting);
            Assert.NotNull(tableIssue);
            Assert.NotNull(tableIssue.Items);
            Assert.Equal(3, tableIssue.Items.Count);

            int targetIndex = tableIssue.Items[1].Index;
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.TableFormatting, new HashSet<int> { targetIndex });
            Assert.Equal(1, changed);

            using var doc = WordprocessingDocument.Open(tempFile, false);
            var runs = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().First().Descendants<Run>().ToList();
            Assert.Equal("20", runs[0].RunProperties!.FontSize!.Val!.Value);
            Assert.Equal("24", runs[1].RunProperties!.FontSize!.Val!.Value); // 12 pt (24 полупункта)
            Assert.Equal("20", runs[2].RunProperties!.FontSize!.Val!.Value);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_FigureFormatting_FixesOnlySelectedFigure()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            for (int i = 0; i < 3; i++) {
                var p = body.AppendChild(new Paragraph());
                var pPr = p.AppendChild(new ParagraphProperties());
                pPr.AppendChild(new Justification { Val = JustificationValues.Left });
                var r = p.AppendChild(new Run());
                r.AppendChild(new Drawing()); // Рисунок
            }
        });

        try {
            var rules = RuleSets.Universal with { FigureAlignment = "Center" };
            var issues = DocumentChecker.Check(tempFile, rules);
            var figIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.FigureFormatting);
            Assert.NotNull(figIssue);
            Assert.NotNull(figIssue.Items);
            Assert.Equal(3, figIssue.Items.Count);

            int targetIndex = figIssue.Items[1].Index;
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.FigureFormatting, new HashSet<int> { targetIndex });
            Assert.Equal(1, changed);

            using var doc = WordprocessingDocument.Open(tempFile, false);
            var paras = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().Where(p => p.Descendants<Drawing>().Any()).ToList();
            Assert.Equal(JustificationValues.Left, paras[0].ParagraphProperties!.Justification!.Val!.Value);
            Assert.Equal(JustificationValues.Center, paras[1].ParagraphProperties!.Justification!.Val!.Value);
            Assert.Equal(JustificationValues.Left, paras[2].ParagraphProperties!.Justification!.Val!.Value);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_CodeSnippets_FixesOnlySelectedSnippet()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            for (int i = 0; i < 3; i++) {
                var p = body.AppendChild(new Paragraph());
                var pPr = p.AppendChild(new ParagraphProperties());
                pPr.AppendChild(new ParagraphStyleId { Val = "Code" });
                var r = p.AppendChild(new Run());
                var rPr = r.AppendChild(new RunProperties());
                rPr.AppendChild(new RunFonts { Ascii = "Arial" });
                r.AppendChild(new Text($"console.log({i + 1});"));
            }
        });

        try {
            var rules = RuleSets.Universal with { CodeFontFamily = "Consolas" };
            var issues = DocumentChecker.Check(tempFile, rules);
            var codeIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.CodeSnippetFormatting);
            Assert.NotNull(codeIssue);
            Assert.NotNull(codeIssue.Items);
            Assert.Equal(3, codeIssue.Items.Count);

            int targetIndex = codeIssue.Items[1].Index;
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.CodeSnippetFormatting, new HashSet<int> { targetIndex });
            Assert.Equal(1, changed);

            using var doc = WordprocessingDocument.Open(tempFile, false);
            var paras = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().ToList();
            Assert.Equal("Arial", paras[0].Descendants<RunFonts>().First().Ascii!.Value);
            Assert.Equal("Consolas", paras[1].Descendants<RunFonts>().First().Ascii!.Value);
            Assert.Equal("Arial", paras[2].Descendants<RunFonts>().First().Ascii!.Value);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_Headings_FixesOnlySelectedHeading()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            for (int i = 0; i < 3; i++) {
                var p = body.AppendChild(new Paragraph());
                var pPr = p.AppendChild(new ParagraphProperties());
                pPr.AppendChild(new ParagraphStyleId { Val = "Heading1" });
                var r = p.AppendChild(new Run(new Text($"Заголовок {i + 1}."))); // Точка в конце
            }
        });

        try {
            var rules = RuleSets.Universal with { HeadingRemoveTrailingDot = true };
            var issues = DocumentChecker.Check(tempFile, rules);
            var headIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.HeadingFormatting);
            Assert.NotNull(headIssue);
            Assert.NotNull(headIssue.Items);
            Assert.Equal(3, headIssue.Items.Count);

            int targetIndex = headIssue.Items[1].Index;
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.HeadingFormatting, new HashSet<int> { targetIndex });
            Assert.Equal(1, changed);

            using var doc = WordprocessingDocument.Open(tempFile, false);
            var paras = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().ToList();
            Assert.EndsWith(".", paras[0].InnerText.Trim());
            Assert.False(paras[1].InnerText.Trim().EndsWith("."));
            Assert.EndsWith(".", paras[2].InnerText.Trim());
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_DecreasesCheckItemsCountByOne()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            for (int i = 0; i < 3; i++) {
                var p = body.AppendChild(new Paragraph());
                var r = p.AppendChild(new Run());
                var rPr = r.AppendChild(new RunProperties());
                rPr.AppendChild(new RunFonts { Ascii = "Arial" });
                r.AppendChild(new Text($"Текст абзаца {i + 1} для проверки уменьшения счетчика."));
            }
        });

        try {
            var rules = RuleSets.Universal;
            var issuesBefore = DocumentChecker.Check(tempFile, rules);
            var fontIssueBefore = issuesBefore.FirstOrDefault(i => i.Kind == IssueKind.FontName);
            Assert.NotNull(fontIssueBefore);
            Assert.NotNull(fontIssueBefore.Items);
            Assert.Equal(3, fontIssueBefore.Items.Count);

            // Исправляем один элемент
            int fixIndex = fontIssueBefore.Items[0].Index;
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.FontName, new HashSet<int> { fixIndex });
            Assert.Equal(1, changed);

            // Повторный Check возвращает на один элемент меньше
            var issuesAfter = DocumentChecker.Check(tempFile, rules);
            var fontIssueAfter = issuesAfter.FirstOrDefault(i => i.Kind == IssueKind.FontName);
            Assert.NotNull(fontIssueAfter);
            Assert.NotNull(fontIssueAfter.Items);
            Assert.Equal(2, fontIssueAfter.Items.Count);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_UnsupportedTypes_ThrowsNotSupportedException()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            body.AppendChild(new Paragraph(new Run(new Text("Тестовый документ."))));
        });

        try {
            var rules = RuleSets.Universal;
            Assert.Throws<NotSupportedException>(() =>
                DocumentChecker.FixItems(tempFile, rules, IssueKind.PageNumbers, new HashSet<int> { 0 }));

            Assert.Throws<NotSupportedException>(() =>
                DocumentChecker.FixItems(tempFile, rules, IssueKind.MissingSection, new HashSet<int> { 0 }));
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_MismatchedText_SkipsElement()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            var p = body.AppendChild(new Paragraph());
            var r = p.AppendChild(new Run());
            var rPr = r.AppendChild(new RunProperties());
            rPr.AppendChild(new RunFonts { Ascii = "Arial" });
            r.AppendChild(new Text("Реальный текст абзаца в документе."));
        });

        try {
            var rules = RuleSets.Universal;
            var issues = DocumentChecker.Check(tempFile, rules);
            var fontIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.FontName);
            Assert.NotNull(fontIssue);
            Assert.NotNull(fontIssue.Items);

            int idx = fontIssue.Items[0].Index;
            // Передаем несовпадающий текст "Совершенно другой текст"
            var dict = new Dictionary<int, string> { [idx] = "Совершенно другой текст" };
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.FontName, new HashSet<int> { idx }, dict);

            // Должен пропустить несовпавший элемент и вернуть 0
            Assert.Equal(0, changed);

            // Шрифт остался Arial
            using var doc = WordprocessingDocument.Open(tempFile, false);
            var p = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().First();
            Assert.Equal("Arial", p.Descendants<RunFonts>().First().Ascii!.Value);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_FontName_ParagraphWithTwoRuns_ModifiesOnlyNonCompliantRun()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            var p = body.AppendChild(new Paragraph());
            // Run 1: Arial (не соответствует правилу)
            var r1 = p.AppendChild(new Run());
            var rPr1 = r1.AppendChild(new RunProperties());
            rPr1.AppendChild(new RunFonts { Ascii = "Arial", HighAnsi = "Arial", ComplexScript = "Arial" });
            r1.AppendChild(new Text("Часть текста шрифтом Arial. "));

            // Run 2: Times New Roman (уже соответствует правилу, имеет курсив)
            var r2 = p.AppendChild(new Run());
            var rPr2 = r2.AppendChild(new RunProperties());
            rPr2.AppendChild(new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman", ComplexScript = "Times New Roman", EastAsia = "SimSun" });
            rPr2.AppendChild(new Italic());
            r2.AppendChild(new Text("Часть текста шрифтом Times New Roman."));
        });

        try {
            var rules = RuleSets.Universal; // FontName = "Times New Roman"
            var issues = DocumentChecker.Check(tempFile, rules);
            var fontIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.FontName);
            Assert.NotNull(fontIssue);
            Assert.NotNull(fontIssue.Items);
            Assert.Single(fontIssue.Items);

            int idx = fontIssue.Items[0].Index;
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.FontName, new HashSet<int> { idx });
            Assert.Equal(1, changed);

            using var doc = WordprocessingDocument.Open(tempFile, false);
            var p = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().First();
            var runs = p.Descendants<Run>().ToList();
            Assert.Equal(2, runs.Count);

            // Run 1 исправлен на Times New Roman
            Assert.Equal("Times New Roman", runs[0].RunProperties?.RunFonts?.Ascii?.Value);
            Assert.Equal("Times New Roman", runs[0].RunProperties?.RunFonts?.HighAnsi?.Value);

            // Run 2 не затронут: шрифт остался Times New Roman, курсив и EastAsia сохранены
            Assert.Equal("Times New Roman", runs[1].RunProperties?.RunFonts?.Ascii?.Value);
            Assert.Equal("SimSun", runs[1].RunProperties?.RunFonts?.EastAsia?.Value);
            Assert.NotNull(runs[1].RunProperties?.Italic);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_TableFormatting_TableCaptionWith10000Index_FixedSuccessfully()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            // Таблица
            var table = body.AppendChild(new Table());
            var tr = table.AppendChild(new TableRow());
            var tc = tr.AppendChild(new TableCell());
            tc.AppendChild(new Paragraph(new Run(new Text("Ячейка"))));

            // Подпись таблицы (не по центру, с красной строкой)
            var pCap = body.AppendChild(new Paragraph());
            var pPr = pCap.AppendChild(new ParagraphProperties());
            pPr.AppendChild(new Justification { Val = JustificationValues.Left });
            pPr.AppendChild(new Indentation { FirstLine = "720" });
            pCap.AppendChild(new Run(new Text("Таблица 1 — Результаты эксперимента")));
        });

        try {
            var rules = RuleSets.Universal with { 
                CheckTables = true, 
                TableCaptionAlignment = "Center", 
                TableCaptionRequireNoIndent = true 
            };
            var issues = DocumentChecker.Check(tempFile, rules);
            var tableIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.TableFormatting);
            Assert.NotNull(tableIssue);
            Assert.NotNull(tableIssue.Items);

            // Ищем элемент подписи с индексом >= 10000 (TableCaptionIndexBase)
            var capItem = tableIssue.Items.FirstOrDefault(i => i.Index >= DocumentChecker.TableCaptionIndexBase);
            Assert.NotNull(capItem);
            Assert.True(capItem.Index >= 10000);

            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.TableFormatting, new HashSet<int> { capItem.Index });
            Assert.Equal(1, changed);

            using var doc = WordprocessingDocument.Open(tempFile, false);
            var capP = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>()
                .First(p => p.InnerText.StartsWith("Таблица 1"));
            Assert.Equal(JustificationValues.Center, capP.ParagraphProperties?.Justification?.Val?.Value);
            Assert.Equal("0", capP.ParagraphProperties?.Indentation?.FirstLine?.Value);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_TableFormatting_NestedTable_NoDuplicates_FixingOneCellLeavesOthersIntact()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            var outerTable = body.AppendChild(new Table());
            var outerRow = outerTable.AppendChild(new TableRow());

            // Внешняя ячейка 1
            var outerCell1 = outerRow.AppendChild(new TableCell());
            var p1 = outerCell1.AppendChild(new Paragraph());
            var r1 = p1.AppendChild(new Run());
            var rPr1 = r1.AppendChild(new RunProperties());
            rPr1.AppendChild(new RunFonts { Ascii = "Arial" });
            r1.AppendChild(new Text("Внешняя ячейка 1"));

            // Внешняя ячейка 2 с вложенной таблицей
            var outerCell2 = outerRow.AppendChild(new TableCell());
            var innerTable = outerCell2.AppendChild(new Table());
            var innerRow = innerTable.AppendChild(new TableRow());

            // Вложенная ячейка 1
            var innerCell1 = innerRow.AppendChild(new TableCell());
            var p2 = innerCell1.AppendChild(new Paragraph());
            var r2 = p2.AppendChild(new Run());
            var rPr2 = r2.AppendChild(new RunProperties());
            rPr2.AppendChild(new RunFonts { Ascii = "Calibri" });
            r2.AppendChild(new Text("Вложенная ячейка 1"));

            // Вложенная ячейка 2
            var innerCell2 = innerRow.AppendChild(new TableCell());
            var p3 = innerCell2.AppendChild(new Paragraph());
            var r3 = p3.AppendChild(new Run());
            var rPr3 = r3.AppendChild(new RunProperties());
            rPr3.AppendChild(new RunFonts { Ascii = "Courier New" });
            r3.AppendChild(new Text("Вложенная ячейка 2"));
        });

        try {
            var rules = RuleSets.Universal with { 
                CheckTables = true, 
                TableFontFamily = "Times New Roman",
                TableFontSizePt = 12 
            };
            var issues = DocumentChecker.Check(tempFile, rules);
            var tableIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.TableFormatting);
            Assert.NotNull(tableIssue);
            Assert.NotNull(tableIssue.Items);

            // Должно быть ровно 3 ячейки без каких-либо дубликатов!
            var cellItems = tableIssue.Items.Where(i => i.Index < DocumentChecker.TableCaptionIndexBase).ToList();
            Assert.Equal(3, cellItems.Count);
            Assert.Equal(3, cellItems.Select(i => i.Index).Distinct().Count());

            // Точечно исправляем ТОЛЬКО вложенную ячейку 1 (индекс 1)
            int targetIndex = cellItems[1].Index;
            int changed = DocumentChecker.FixItems(tempFile, rules, IssueKind.TableFormatting, new HashSet<int> { targetIndex });
            Assert.Equal(1, changed);

            using var doc = WordprocessingDocument.Open(tempFile, false);
            var paras = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().ToList();
            // Парас 0: Внешняя ячейка 1 осталась Arial (не тронута)
            Assert.Equal("Arial", paras[0].Descendants<RunFonts>().First().Ascii!.Value);
            // Парас 1: Вложенная ячейка 1 исправлена на Times New Roman
            Assert.Equal("Times New Roman", paras[1].Descendants<RunFonts>().First().Ascii!.Value);
            // Парас 2: Вложенная ячейка 2 осталась Courier New (не тронута)
            Assert.Equal("Courier New", paras[2].Descendants<RunFonts>().First().Ascii!.Value);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FixItems_MismatchedText_SkippedForFontSizeAndTableFormatting()
    {
        var tempFile = CreateDocWithSection((body, secProps) => {
            // Абзац с неверным размером шрифта
            var p = body.AppendChild(new Paragraph());
            var r = p.AppendChild(new Run());
            var rPr = r.AppendChild(new RunProperties());
            rPr.AppendChild(new FontSize { Val = "20" }); // 10 pt
            r.AppendChild(new Text("Текст обычного абзаца для проверки размера."));

            // Таблица с неверным шрифтом ячейки
            var table = body.AppendChild(new Table());
            var tr = table.AppendChild(new TableRow());
            var tc = tr.AppendChild(new TableCell());
            var pCell = tc.AppendChild(new Paragraph());
            var rCell = pCell.AppendChild(new Run());
            var rPrCell = rCell.AppendChild(new RunProperties());
            rPrCell.AppendChild(new RunFonts { Ascii = "Arial" });
            rCell.AppendChild(new Text("Текст ячейки таблицы для проверки."));
        });

        try {
            var rules = RuleSets.Universal with { 
                FontSizePt = 14, 
                CheckTables = true, 
                TableFontFamily = "Times New Roman" 
            };
            var issues = DocumentChecker.Check(tempFile, rules);

            // 1. Проверяем FontSize с несовпадающим ожидаемым текстом
            var sizeIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.FontSize);
            Assert.NotNull(sizeIssue);
            Assert.NotNull(sizeIssue.Items);
            int sizeIdx = sizeIssue.Items[0].Index;
            var wrongSizeDict = new Dictionary<int, string> { [sizeIdx] = "Неправильный текст для FontSize" };
            int sizeChanged = DocumentChecker.FixItems(tempFile, rules, IssueKind.FontSize, new HashSet<int> { sizeIdx }, wrongSizeDict);
            Assert.Equal(0, sizeChanged); // Пропущен!

            // 2. Проверяем TableFormatting с несовпадающим ожидаемым текстом
            var tableIssue = issues.FirstOrDefault(i => i.Kind == IssueKind.TableFormatting);
            Assert.NotNull(tableIssue);
            Assert.NotNull(tableIssue.Items);
            int tableIdx = tableIssue.Items[0].Index;
            var wrongTableDict = new Dictionary<int, string> { [tableIdx] = "Неправильный текст для TableFormatting" };
            int tableChanged = DocumentChecker.FixItems(tempFile, rules, IssueKind.TableFormatting, new HashSet<int> { tableIdx }, wrongTableDict);
            Assert.Equal(0, tableChanged); // Пропущен!

            // Убеждаемся, что исходные свойства в документе не изменились
            using var doc = WordprocessingDocument.Open(tempFile, false);
            var p = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().First();
            Assert.Equal("20", p.Descendants<FontSize>().First().Val!.Value);

            var cellP = doc.MainDocumentPart!.Document.Body!.Descendants<TableCell>().First().Descendants<Paragraph>().First();
            Assert.Equal("Arial", cellP.Descendants<RunFonts>().First().Ascii!.Value);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
