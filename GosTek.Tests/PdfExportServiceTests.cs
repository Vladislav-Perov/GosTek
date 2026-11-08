/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты генератора документов PDF на базе QuestPDF
 * СОДЕРЖИМОЕ: PdfExportServiceTests
 * ЗАВИСИМОСТИ: xUnit, GosTek.Services.PdfExportService, QuestPDF
 * ==============================================================================
 */
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GosTek.Models;
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class PdfExportServiceTests
{
    private static string CreateTestDocx(Action<Body> populateBody)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_pdf_src_{Guid.NewGuid():N}.docx");
        using (var doc = WordprocessingDocument.Create(tempFile, WordprocessingDocumentType.Document)) {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            populateBody(body);
            mainPart.Document.Save();
        }
        return tempFile;
    }

    [Fact]
    public void GeneratePdf_FromTemplateDef_CreatesValidPdfFile()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_pdf_out_{Guid.NewGuid():N}.pdf");
        var fields = new[] {
            new TemplateField("fio", "ФИО", "Иванов И.И.", ProfileSource.FullName, false),
            new TemplateField("unp", "УНП", "123456789", ProfileSource.None, false),
        };
        var template = new TemplateDef("t1", "Тестовое заявление", "Категория", "", fields);
        var values = new Dictionary<string, string> {
            ["fio"] = "Сидоров Сидор Сидорович",
            ["unp"] = "192837465"
        };

        try {
            PdfExportService.GeneratePdf(template, values, outputPath);

            Assert.True(File.Exists(outputPath), "PDF файл должен существовать после генерации.");
            var fileInfo = new FileInfo(outputPath);
            Assert.True(fileInfo.Length > 0, "Размер созданного PDF файла должен быть больше 0 байт.");

            // Проверка сигнатуры заголовка PDF (%PDF-)
            var bytes = File.ReadAllBytes(outputPath);
            Assert.True(bytes.Length >= 4);
            var header = Encoding.ASCII.GetString(bytes, 0, 4);
            Assert.Equal("%PDF", header);
        } finally {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public void GeneratePdf_FromRealDocxTemplate_GeneratesValidPdf()
    {
        var inputPath = CreateTestDocx(body => {
            var p1 = body.AppendChild(new Paragraph());
            var pProps = p1.AppendChild(new ParagraphProperties(new Justification { Val = JustificationValues.Center }));
            var r1 = p1.AppendChild(new Run());
            r1.AppendChild(new RunProperties(new Bold()));
            r1.AppendChild(new Text("ЗАЯВЛЕНИЕ"));

            var p2 = body.AppendChild(new Paragraph());
            var r2 = p2.AppendChild(new Run());
            r2.AppendChild(new Text("Я, [ФИО], проживающий по адресу [Адрес], настоящим заявляю..."));
        });

        var outputPath = Path.Combine(Path.GetTempPath(), $"test_docx_pdf_out_{Guid.NewGuid():N}.pdf");
        var fields = new[] {
            new TemplateField(TemplateEngine.FieldId("ФИО"), "ФИО", "", ProfileSource.FullName, false),
            new TemplateField(TemplateEngine.FieldId("Адрес"), "Адрес", "", ProfileSource.Address, false),
        };
        var template = new TemplateDef("t2", "Заявление гражданина", "Общее", inputPath, fields);
        var values = new Dictionary<string, string> {
            [TemplateEngine.FieldId("ФИО")] = "Козлов Петр Сергеевич",
            [TemplateEngine.FieldId("Адрес")] = "г. Минск, пр. Независимости, д. 4"
        };

        try {
            PdfExportService.GeneratePdf(template, values, outputPath);

            Assert.True(File.Exists(outputPath));
            var fileInfo = new FileInfo(outputPath);
            Assert.True(fileInfo.Length > 100, "Размер сгенерированного из DOCX PDF должен превышать 100 байт.");

            var bytes = File.ReadAllBytes(outputPath);
            var header = Encoding.ASCII.GetString(bytes, 0, 4);
            Assert.Equal("%PDF", header);
        } finally {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public void GeneratePdf_ToStream_WritesPdfContent()
    {
        var fields = new[] {
            new TemplateField("reason", "Причина обращения", "", ProfileSource.None, true),
        };
        var template = new TemplateDef("t3", "Служебная записка", "Внутренние", "", fields);
        var values = new Dictionary<string, string> {
            ["reason"] = "Производственная необходимость обновления документации."
        };

        using var memoryStream = new MemoryStream();
        PdfExportService.GeneratePdf(template, values, memoryStream);

        Assert.True(memoryStream.Length > 0, "Поток памяти не должен быть пустым.");
        var bytes = memoryStream.ToArray();
        var header = Encoding.ASCII.GetString(bytes, 0, 4);
        Assert.Equal("%PDF", header);
    }

    [Fact]
    public void GeneratePdf_FromPreviewParagraphs_RendersFormattedElements()
    {
        var paras = new List<PreviewPara> {
            new(
                new List<PreviewPart> { new("Заголовок документа", null) },
                Microsoft.Maui.TextAlignment.Center,
                Bold: true,
                Italic: false,
                Pt: 16,
                Blank: false
            ),
            new(new List<PreviewPart>(), Microsoft.Maui.TextAlignment.Start, false, false, 14, Blank: true),
            new(
                new List<PreviewPart> {
                    new("Уважаемый ", null),
                    new("[ФИО]", "fio"),
                    new("!", null)
                },
                Microsoft.Maui.TextAlignment.Start,
                Bold: false,
                Italic: false,
                Pt: 14,
                Blank: false
            )
        };

        var values = new Dictionary<string, string> {
            ["fio"] = "Алексей Николаевич"
        };

        using var ms = new MemoryStream();
        PdfExportService.GeneratePdf("Тестовый документ", paras, values, ms);

        Assert.True(ms.Length > 0);
        var bytes = ms.ToArray();
        var header = Encoding.ASCII.GetString(bytes, 0, 4);
        Assert.Equal("%PDF", header);
    }

    [Fact]
    public void SafeFileName_StripsPdfExtension()
    {
        var result = ExportService.SafeFileName("document.pdf", "fallback");
        Assert.Equal("document", result);

        var resultCase = ExportService.SafeFileName("report.PDF", "fallback");
        Assert.Equal("report", resultCase);
    }
}
