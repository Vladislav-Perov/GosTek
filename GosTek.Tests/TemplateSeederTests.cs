/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты для сервиса генерации и сидирования эталонных шаблонов TemplateSeeder
 * СОДЕРЖИМОЕ: TemplateSeederTests
 * ЗАВИСИМОСТИ: xUnit, GosTek.Services.TemplateSeeder, GosTek.TemplateEngine, DocumentFormat.OpenXml
 * ==============================================================================
 */
using DocumentFormat.OpenXml.Packaging;
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class TemplateSeederTests
{
    [Fact]
    public void CreateTemplateDocument_GeneratesValidVacationTemplate()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"vacation_test_{Guid.NewGuid():N}.docx");
        try {
            TemplateSeeder.CreateTemplateDocument(tempPath, TemplateSeeder.VacationTargetName);
            Assert.True(File.Exists(tempPath));

            using var doc = WordprocessingDocument.Open(tempPath, false);
            string? text = doc.MainDocumentPart?.Document?.Body?.InnerText;

            Assert.NotNull(text);
            Assert.Contains("ЗАЯВЛЕНИЕ", text);
            Assert.Contains("[ФИО]", text);
            Assert.Contains("[Организация]", text);
        } finally {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void CreateTemplateDocument_GeneratesValidSalesContractTemplate()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"contract_test_{Guid.NewGuid():N}.docx");
        try {
            TemplateSeeder.CreateTemplateDocument(tempPath, TemplateSeeder.ContractTargetName);
            Assert.True(File.Exists(tempPath));

            using var doc = WordprocessingDocument.Open(tempPath, false);
            string? text = doc.MainDocumentPart?.Document?.Body?.InnerText;

            Assert.NotNull(text);
            Assert.Contains("ДОГОВОР КУПЛИ-ПРОДАЖИ", text);
            Assert.Contains("[Организация]", text);
            Assert.Contains("[УНП]", text);
            Assert.Contains("[Расчётный счёт]", text);
        } finally {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void CreateTemplateDocument_GeneratesValidExplanationTemplate()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"explanation_test_{Guid.NewGuid():N}.docx");
        try {
            TemplateSeeder.CreateTemplateDocument(tempPath, TemplateSeeder.ExplanationTargetName);
            Assert.True(File.Exists(tempPath));

            using var doc = WordprocessingDocument.Open(tempPath, false);
            string? text = doc.MainDocumentPart?.Document?.Body?.InnerText;

            Assert.NotNull(text);
            Assert.Contains("ОБЪЯСНИТЕЛЬНАЯ ЗАПИСКА", text);
            Assert.Contains("[ФИО]", text);
            Assert.Contains("[Организация]", text);
        } finally {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void SeedResourcesRaw_EnsuresBundledTemplatesExist()
    {
        // Создаем физические файлы в папке Resources/Raw, чтобы они компилировались как MauiAsset
        string solutionDir = FindSolutionDir();
        string rawDir = Path.Combine(solutionDir, "Resources", "Raw");

        if (Directory.Exists(rawDir)) {
            foreach (var (rawAsset, targetFileName) in TemplateSeeder.DefaultTemplates) {
                string targetPath = Path.Combine(rawDir, rawAsset);
                if (!File.Exists(targetPath)) {
                    TemplateSeeder.CreateTemplateDocument(targetPath, targetFileName);
                }
                Assert.True(File.Exists(targetPath));
            }
        }
    }

    private static string FindSolutionDir()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current)) {
            if (File.Exists(Path.Combine(current, "GosTek.sln")))
                return current;
            current = Path.GetDirectoryName(current)!;
        }
        return Directory.GetCurrentDirectory();
    }
}
