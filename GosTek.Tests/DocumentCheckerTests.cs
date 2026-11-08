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

            Assert.Contains(issues, i => i.Kind == IssueKind.MarginLeft);
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

            Assert.Contains(issues, i => i.Kind == IssueKind.FontName);
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

            Assert.Contains(issues, i => i.Kind == IssueKind.FontSize);
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

            Assert.Contains(issues, i => i.Kind == IssueKind.LineSpacing);
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

            Assert.Contains(issues, i => i.Kind == IssueKind.PageNumbers);
        } finally {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
