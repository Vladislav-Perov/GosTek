/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Unit-тесты генератора документов DOCX по ГОСТ (GostDocxExporter)
 * ==============================================================================
 */
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GosTek.Models;
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class GostDocxExporterTests : IDisposable
{
    private readonly string _testDir;

    public GostDocxExporterTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"gost_export_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void ExportToDocx_CreatesValidFileWithGostProperties()
    {
        string filePath = Path.Combine(_testDir, "test_doc.docx");
        string rawText = @"ПРИКАЗ
17 ноября 2026 г. № 102
О назначении ответственных лиц

В соответствии с правилами пожарной безопасности
приказываю:
1. Назначить Иванова И.И. ответственным за пожарную безопасность.
2. Контроль возложить на заместителя директора.

Директор";

        var options = new GostFormattingOptions
        {
            FontFamily = "Times New Roman",
            FontSizePt = 14.0,
            LineSpacing = 1.5,
            FirstLineIndentCm = 1.25,
            MarginLeftMm = 30.0,
            MarginRightMm = 15.0
        };

        GostDocxExporter.ExportToDocx(filePath, rawText, options);

        Assert.True(File.Exists(filePath));
        var fileInfo = new FileInfo(filePath);
        Assert.True(fileInfo.Length > 0);

        // Проверяем структуру OpenXML
        using var doc = WordprocessingDocument.Open(filePath, false);
        Assert.NotNull(doc.MainDocumentPart);
        Assert.NotNull(doc.MainDocumentPart.Document);
        Assert.NotNull(doc.MainDocumentPart.Document.Body);

        var sectionProps = doc.MainDocumentPart.Document.Body.Elements<SectionProperties>().FirstOrDefault();
        Assert.NotNull(sectionProps);

        var margin = sectionProps.Elements<PageMargin>().FirstOrDefault();
        Assert.NotNull(margin);
        // 30 мм * 56.7 = ~1701 dxa
        Assert.True(margin.Left?.Value >= 1700);

        // Проверяем параграфы
        var paragraphs = doc.MainDocumentPart.Document.Body.Elements<Paragraph>().ToList();
        Assert.NotEmpty(paragraphs);
    }
}
