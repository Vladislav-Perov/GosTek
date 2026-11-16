/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Тестирование генератора пользовательских ГОСТ-шаблонов из скана (ScanTemplateBuilder)
 * ==============================================================================
 */
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class ScanTemplateBuilderTests
{
    [Fact]
    public void DetectDocumentTitle_DetectsKeywordHeadings()
    {
        string text1 = "Ректору БГУ\nИванова И.И.\nЗАЯВЛЕНИЕ\nПрошу предоставить...";
        Assert.Equal("Заявление", ScanTemplateBuilder.DetectDocumentTitle(text1));

        string text2 = "ДОГОВОР АРЕНДЫ ЖИЛОГО ПОМЕЩЕНИЯ\nг. Минск\n12.05.2026 г.";
        Assert.Equal("Договор", ScanTemplateBuilder.DetectDocumentTitle(text2));

        string text3 = "Декану факультета\nОБЪЯСНИТЕЛЬНАЯ ЗАПИСКА\nЯ отсутствовал...";
        Assert.Equal("Объяснительная записка", ScanTemplateBuilder.DetectDocumentTitle(text3));
    }

    [Fact]
    public void ParameterizeText_ReplacesIdentAndDatesAndMoney()
    {
        string raw = "Я, гражданин РБ с личным номером 3010190A001PB1 и паспортом MP1234567, обязуюсь уплатить 150 бел. руб. до 25.12.2026.";
        var (parameterized, fields) = ScanTemplateBuilder.ParameterizeText(raw);

        Assert.Contains("{{ЛичныйНомер}}", parameterized);
        Assert.Contains("{{Паспорт}}", parameterized);
        Assert.Contains("[Сумма]", parameterized);
        Assert.Contains("{{Дата}}", parameterized);
        Assert.NotEmpty(fields);
    }

    [Fact]
    public void BuildAndSaveTemplate_CreatesValidDocxFile()
    {
        string tempTemplates = Path.Combine(Path.GetTempPath(), $"scan_tpl_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempTemplates);

        try
        {
            string sampleText = "СПРАВКА\nВыдана Иванову И.И. в том, что он действительно обучается на дневном отделении.\nДата: 15.09.2026 г.";
            var result = ScanTemplateBuilder.BuildAndSaveTemplate(sampleText, "Тестовая справка", "Тесты", tempTemplates);

            Assert.True(File.Exists(result.FilePath));
            Assert.Equal("Тестовая справка", result.Title);
            Assert.True(new FileInfo(result.FilePath).Length > 1000);
        }
        finally
        {
            if (Directory.Exists(tempTemplates))
                Directory.Delete(tempTemplates, true);
        }
    }
}
