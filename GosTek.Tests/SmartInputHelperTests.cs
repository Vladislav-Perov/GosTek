/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты алгоритмов форматирования пользовательского ввода (SmartInputHelper)
 * СОДЕРЖИМОЕ: SmartInputHelperTests
 * ЗАВИСИМОСТИ: GosTek.Services, Xunit
 * ==============================================================================
 */
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class SmartInputHelperTests
{
    [Theory]
    [InlineData("иванов иван иванович", "Иванов Иван Иванович")]
    [InlineData("петров-водкин кузьма", "Петров-Водкин Кузьма")]
    [InlineData("иванов", "Иванов")]
    [InlineData("ИВАНОВ ИВАН", "ИВАНОВ ИВАН")] // Сохранение существующего капса
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ToTitleCase_VariousNames_FormatsCorrectly(string? input, string expected)
    {
        var result = SmartInputHelper.ToTitleCase(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Дата составления", true)]
    [InlineData("Срок действия (число)", true)]
    [InlineData("ФИО директора", false)]
    [InlineData("Город", false)]
    public void IsDateField_Labels_DetectsCorrectly(string label, bool expected)
    {
        var result = SmartInputHelper.IsDateField(label);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("ФИО", true)]
    [InlineData("Фамилия и инициалы", true)]
    [InlineData("Имя представителя", true)]
    [InlineData("Адрес", false)]
    public void IsFioField_Labels_DetectsCorrectly(string label, bool expected)
    {
        var result = SmartInputHelper.IsFioField(label);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatToday_ReturnsValidDateFormat()
    {
        var today = SmartInputHelper.FormatToday();
        Assert.Matches(@"^\d{2}\.\d{2}\.\d{4}$", today);
    }
}
