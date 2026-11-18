/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты движка шаблонизации DOCX (замена токенов в Run, многострочный ввод)
 * СОДЕРЖИМОЕ: TemplateEngineTests
 * ЗАВИСИМОСТИ: xUnit, GosTek.TemplateEngine, DocumentFormat.OpenXml
 * ==============================================================================
 */
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GosTek.Models;
using Xunit;

namespace GosTek.Tests;

public class TemplateEngineTests
{
    private static string CreateTestDocument(Action<Body> populateBody)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_in_{Guid.NewGuid():N}.docx");
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
    public void Scan_EnablesGenerationOnlyForTextMarkerAndBuiltInExplanation()
    {
        var custom = CreateTestDocument(body => body.AppendChild(new Paragraph(
            new Run(new Text("[ФИО] [Текст] {Текст} {Текст} {Дата}")))));
        var manual = CreateTestDocument(body => body.AppendChild(new Paragraph(
            new Run(new Text("[Текст] {{Текст}} [Текст объяснения обстоятельств]")))));
        var explanation = CreateTestDocument(body => body.AppendChild(new Paragraph(
            new Run(new Text("[Причина или событие] [Текст объяснения обстоятельств]")))));
        var folder = Directory.CreateTempSubdirectory("gostek-text-");
        try
        {
            File.Move(custom, Path.Combine(folder.FullName, "Custom.docx"));
            File.Move(manual, Path.Combine(folder.FullName, "Manual.docx"));
            File.Move(explanation, Path.Combine(folder.FullName, GosTek.Services.TemplateSeeder.ExplanationTargetName));
            var templates = TemplateEngine.Scan(folder.FullName);

            var generated = Assert.Single(templates.Single(t => t.Title == "Custom").Fields,
                f => f.TextGeneration is not null);
            Assert.Equal("текст", generated.Id);
            Assert.True(generated.Multiline);
            Assert.Equal(TextGenerationKind.DocumentText, generated.TextGeneration);
            Assert.All(templates.Single(t => t.Title == "Manual").Fields, f => Assert.Null(f.TextGeneration));
            var legacy = Assert.Single(templates.Single(t => t.Title == "Объяснительная_записка").Fields,
                f => f.TextGeneration is not null);
            Assert.Equal("текст объяснения обстоятельств", legacy.Id);
            Assert.Equal(TextGenerationKind.Explanation, legacy.TextGeneration);
        }
        finally
        {
            File.Delete(custom);
            File.Delete(manual);
            File.Delete(explanation);
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void FillTo_SingleRun_ReplacesToken()
    {
        var inputPath = CreateTestDocument(body => {
            var para = body.AppendChild(new Paragraph());
            var run = para.AppendChild(new Run());
            run.AppendChild(new Text("Здравствуйте, [ФИО]!"));
        });
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_out_{Guid.NewGuid():N}.docx");

        try {
            var template = new TemplateDef("1", "Тест", "Категория", inputPath, Array.Empty<TemplateField>());
            var values = new Dictionary<string, string> {
                [TemplateEngine.FieldId("ФИО")] = "Иванов Иван Иванович"
            };

            TemplateEngine.FillTo(template, values, outputPath);

            using var doc = WordprocessingDocument.Open(outputPath, false);
            var text = doc.MainDocumentPart?.Document?.Body?.InnerText;
            Assert.Equal("Здравствуйте, Иванов Иван Иванович!", text);
        } finally {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public void FillTo_SplitRun_ReplacesMarkerSpanningMultipleRuns()
    {
        // Маркер [ФИО] разбит Word'ом на три Run: "[Ф", "И", "О]"
        var inputPath = CreateTestDocument(body => {
            var para = body.AppendChild(new Paragraph());
            var run1 = para.AppendChild(new Run());
            run1.AppendChild(new Text("Клиент: [Ф"));
            var run2 = para.AppendChild(new Run());
            run2.AppendChild(new Text("И"));
            var run3 = para.AppendChild(new Run());
            run3.AppendChild(new Text("О]."));
        });
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_out_{Guid.NewGuid():N}.docx");

        try {
            var template = new TemplateDef("2", "Тест", "Категория", inputPath, Array.Empty<TemplateField>());
            var values = new Dictionary<string, string> {
                [TemplateEngine.FieldId("ФИО")] = "Петров Петр"
            };

            TemplateEngine.FillTo(template, values, outputPath);

            using var doc = WordprocessingDocument.Open(outputPath, false);
            var text = doc.MainDocumentPart?.Document?.Body?.InnerText;
            Assert.Equal("Клиент: Петров Петр.", text);
        } finally {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public void FillTo_MultilineInput_InsertsWordBreakElement()
    {
        var inputPath = CreateTestDocument(body => {
            var para = body.AppendChild(new Paragraph());
            var run = para.AppendChild(new Run());
            run.AppendChild(new Text("Текст: [Описание]."));
        });
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_out_{Guid.NewGuid():N}.docx");

        try {
            var template = new TemplateDef("3", "Тест", "Категория", inputPath, Array.Empty<TemplateField>());
            var values = new Dictionary<string, string> {
                [TemplateEngine.FieldId("Описание")] = "Первая строка\nВторая строка"
            };

            TemplateEngine.FillTo(template, values, outputPath);

            using var doc = WordprocessingDocument.Open(outputPath, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            Assert.NotNull(body);

            // Должен присутствовать элемент Break (<w:br>)
            var breaks = body.Descendants<Break>().ToList();
            Assert.Single(breaks);

            var fullText = body.InnerText;
            Assert.Contains("Первая строкаВторая строка", fullText);
        } finally {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public void FillTo_UnfilledField_ReplacesWithUnderscores()
    {
        var inputPath = CreateTestDocument(body => {
            var para = body.AppendChild(new Paragraph());
            var run = para.AppendChild(new Run());
            run.AppendChild(new Text("Директор: [ФИО руководителя]"));
        });
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_out_{Guid.NewGuid():N}.docx");

        try {
            var template = new TemplateDef("4", "Тест", "Категория", inputPath, Array.Empty<TemplateField>());
            var emptyValues = new Dictionary<string, string>();

            TemplateEngine.FillTo(template, emptyValues, outputPath);

            using var doc = WordprocessingDocument.Open(outputPath, false);
            var text = doc.MainDocumentPart?.Document?.Body?.InnerText;
            Assert.Equal("Директор: __________", text);
        } finally {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public void ParseTokenDetails_WithElaborateMode_ParsesCorrectly()
    {
        var (label, hint, mode) = TemplateEngine.ParseTokenDetails("Причина|расписать");
        Assert.Equal("Причина", label);
        Assert.Equal("", hint);
        Assert.Equal(FieldExpansionMode.Elaborate, mode);
    }

    [Fact]
    public void ParseTokenDetails_WithBriefMode_ParsesCorrectly()
    {
        var (label, hint, mode) = TemplateEngine.ParseTokenDetails("Причина|кратко");
        Assert.Equal("Причина", label);
        Assert.Equal("", hint);
        Assert.Equal(FieldExpansionMode.Brief, mode);
    }

    [Fact]
    public void ParseTokenDetails_WithHintAndElaborateMode_ParsesBoth()
    {
        var (label, hint, mode) = TemplateEngine.ParseTokenDetails("Причина-Укажите подробно|расписать");
        Assert.Equal("Причина", label);
        Assert.Equal("Укажите подробно", hint);
        Assert.Equal(FieldExpansionMode.Elaborate, mode);
    }
}
