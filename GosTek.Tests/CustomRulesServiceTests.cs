/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты сервиса CustomRulesService (сохранение, загрузка JSON, парсинг категорий, автоподбор правил)
 * СОДЕРЖИМОЕ: CustomRulesServiceTests
 * ЗАВИСИМОСТИ: xUnit, GosTek.Services.CustomRulesService, GosTek.Models
 * ==============================================================================
 */
using GosTek.Models;
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class CustomRulesServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempFile;

    public CustomRulesServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "gostek_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempFile = Path.Combine(_tempDir, "custom_rules.json");
    }

    public void Dispose()
    {
        try {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        } catch {
            // Игнорируем ошибки очистки временных файлов тестов
        }
    }

    [Fact]
    public async Task SaveRuleAsync_ValidCustomRule_PersistsToJsonAndCanBeLoaded()
    {
        // Arrange
        var service = new CustomRulesService(_tempFile);
        var rule = new RuleSet(
            Id: "custom_1",
            Title: "Моё специальное правило",
            Left: new MarginRule(2.5, true),
            Right: new MarginRule(1.5, false),
            Top: new MarginRule(2.0, false),
            Bottom: new MarginRule(2.0, false),
            FontName: "Calibri",
            FontSizePt: 12.0,
            LineSpacing: 1.15,
            RequirePageNumbers: false,
            RequiredSections: Array.Empty<SectionRule>(),
            SectionsMinParagraphs: 50,
            FirstLineIndentCm: 1.0,
            Alignment: "Left",
            Category: "Инструкция",
            IsCustom: true,
            Description: "Правило для внутренних инструкций");

        // Act
        bool saved = await service.SaveRuleAsync(rule);
        var loaded = await service.LoadRulesAsync();

        // Assert
        Assert.True(saved);
        Assert.Single(loaded);
        var retrieved = loaded[0];
        Assert.Equal("custom_1", retrieved.Id);
        Assert.Equal("Моё специальное правило", retrieved.Title);
        Assert.Equal(2.5, retrieved.Left.Cm);
        Assert.Equal(1.5, retrieved.Right.Cm);
        Assert.Equal("Calibri", retrieved.FontName);
        Assert.Equal(12.0, retrieved.FontSizePt);
        Assert.Equal(1.15, retrieved.LineSpacing);
        Assert.False(retrieved.RequirePageNumbers);
        Assert.Equal(1.0, retrieved.FirstLineIndentCm);
        Assert.Equal("Left", retrieved.Alignment);
        Assert.Equal("Инструкция", retrieved.Category);
        Assert.True(retrieved.IsCustom);
    }

    [Fact]
    public async Task SaveRuleAsync_NullOrEmptyTitle_ThrowsException()
    {
        // Arrange
        var service = new CustomRulesService(_tempFile);
        var invalidRule = RuleSets.Universal with { Title = "   " };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SaveRuleAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveRuleAsync(invalidRule));
    }

    [Fact]
    public async Task SaveRuleAsync_UpdatesExistingRule_WhenIdMatches()
    {
        // Arrange
        var service = new CustomRulesService(_tempFile);
        var rule1 = RuleSets.Universal with { Id = "update_test", Title = "Версия 1", Category = "Акт" };
        var rule2 = RuleSets.Universal with { Id = "update_test", Title = "Версия 2", Category = "Акт" };

        // Act
        await service.SaveRuleAsync(rule1);
        await service.SaveRuleAsync(rule2);
        var loaded = await service.LoadRulesAsync();

        // Assert
        Assert.Single(loaded);
        Assert.Equal("Версия 2", loaded[0].Title);
    }

    [Fact]
    public async Task DeleteRuleAsync_CustomRule_RemovesFromStorage()
    {
        // Arrange
        var service = new CustomRulesService(_tempFile);
        var rule = RuleSets.Universal with { Id = "to_delete", Title = "Удаляемое правило" };
        await service.SaveRuleAsync(rule);

        // Act
        bool deleted = await service.DeleteRuleAsync("to_delete");
        var loaded = await service.LoadRulesAsync();

        // Assert
        Assert.True(deleted);
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task DeleteRuleAsync_BuiltInRule_ReturnsFalseAndPreservesBuiltInRule()
    {
        // Arrange
        var service = new CustomRulesService(_tempFile);

        // Act
        bool deletedUniversal = await service.DeleteRuleAsync("universal");
        bool deletedStudy = await service.DeleteRuleAsync("study_bgtu");

        // Assert
        Assert.False(deletedUniversal);
        Assert.False(deletedStudy);
    }

    [Fact]
    public async Task LoadRulesAsync_CorruptedJson_RecoversGracefullyWithoutThrowing()
    {
        // Arrange
        var service = new CustomRulesService(_tempFile);
        await File.WriteAllTextAsync(_tempFile, "{ broken json content: invalid syntax ???");

        // Act
        var loaded = await service.LoadRulesAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.Empty(loaded);

        // Проверяем создание резервной копии повреждённого файла
        var files = Directory.GetFiles(_tempDir, "custom_rules.json.corrupted.*");
        Assert.Single(files);
    }

    [Theory]
    [InlineData("Заявление_На_отпуск.docx", "Заявление")]
    [InlineData("Договор - Поставка товаров.docx", "Договор")]
    [InlineData("Служебная записка на закупку.docx", "Служебная")]
    [InlineData("Отчет.docx", "Отчет")]
    [InlineData("  Акт_приемки.docx  ", "Акт")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ExtractCategoryFromFileName_ExtractsExpectedCategory(string? fileName, string expectedCategory)
    {
        // Arrange
        var service = new CustomRulesService(_tempFile);

        // Act
        string category = service.ExtractCategoryFromFileName(fileName);

        // Assert
        Assert.Equal(expectedCategory, category);
    }

    [Fact]
    public async Task MatchRuleSetForTemplate_CustomRuleMatchesCategory_ReturnsCustomRule()
    {
        // Arrange
        var service = new CustomRulesService(_tempFile);
        var customRule = RuleSets.Universal with {
            Id = "spravka_rule",
            Title = "Правило справок",
            Category = "Справка",
            FontSizePt = 12.0,
        };
        await service.SaveRuleAsync(customRule);

        // Act
        var matched = service.MatchRuleSetForTemplate("Справка_С_места_работы.docx");

        // Assert
        Assert.NotNull(matched);
        Assert.Equal("spravka_rule", matched.Id);
        Assert.Equal(12.0, matched.FontSizePt);
    }

    [Fact]
    public void MatchRuleSetForTemplate_BuiltInRuleMatchesCategory_ReturnsBuiltInRule()
    {
        // Arrange
        var service = new CustomRulesService(_tempFile);

        // Act
        var matchApp = service.MatchRuleSetForTemplate("Заявление_На_отпуск.docx");
        var matchContract = service.MatchRuleSetForTemplate("Договор - Оказание услуг.docx");
        var matchStudy = service.MatchRuleSetForTemplate("Учебная_Курсовая работа.docx");

        // Assert
        Assert.Equal("applications", matchApp.Id);
        Assert.Equal("contracts", matchContract.Id);
        Assert.Equal("study_bgtu", matchStudy.Id);
    }

    [Fact]
    public void MatchRuleSetForTemplate_UnknownCategory_FallsBackToUniversal()
    {
        // Arrange
        var service = new CustomRulesService(_tempFile);

        // Act
        var matched = service.MatchRuleSetForTemplate("НеизвестныйФайл_2026.docx");

        // Assert
        Assert.Equal(RuleSets.Universal.Id, matched.Id);
    }

    [Fact]
    public async Task GetAllRules_IncludesBuiltInPresetsAndCustomRules()
    {
        // Arrange
        var service = new CustomRulesService(_tempFile);
        var custom = RuleSets.Universal with { Id = "custom_x", Title = "Пользовательское", Category = "Тест" };
        await service.SaveRuleAsync(custom);

        // Act
        var all = service.GetAllRules();

        // Assert
        Assert.Equal(RuleSets.Presets.Count + 1, all.Count);
        Assert.Contains(all, r => r.Id == "custom_x");
    }
}
