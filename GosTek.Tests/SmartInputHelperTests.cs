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

    [Theory]
    [InlineData("1", "", "1")]
    [InlineData("15", "1", "15.")]
    [InlineData("15.0", "15.", "15.0")]
    [InlineData("15.03", "15.0", "15.03.")]
    [InlineData("15.03.2", "15.03.", "15.03.2")]
    [InlineData("15.03.2026", "15.03.202", "15.03.2026")]
    [InlineData("15032026", null, "15.03.2026")]
    [InlineData("15032026999", null, "15.03.2026")]
    [InlineData("2026-03-15", null, "15.03.2026")]
    [InlineData("15/03/2026", null, "15.03.2026")]
    [InlineData("", null, "")]
    [InlineData(null, null, "")]
    public void ApplyDateMask_VariousInputs_FormatsCorrectly(string? newText, string? oldText, string expected)
    {
        var result = SmartInputHelper.ApplyDateMask(newText, oldText);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ApplyDateMask_BackspaceOnDot_RemovesPrecedingDigit()
    {
        // Пользователь нажал Backspace, когда текст был "15." и стал "15"
        var result = SmartInputHelper.ApplyDateMask("15", "15.");
        Assert.Equal("1", result);

        // Пользователь нажал Backspace, когда текст был "15.03." и стал "15.03"
        var result2 = SmartInputHelper.ApplyDateMask("15.03", "15.03.");
        Assert.Equal("15.0", result2);
    }

    [Theory]
    [InlineData("15.03.2026", true, 2026, 3, 15)]
    [InlineData("2026-03-15", true, 2026, 3, 15)]
    [InlineData("32.01.2026", false, 0, 0, 0)]
    [InlineData("15.13.2026", false, 0, 0, 0)]
    [InlineData("не дата", false, 0, 0, 0)]
    [InlineData("", false, 0, 0, 0)]
    [InlineData(null, false, 0, 0, 0)]
    public void TryParseDate_ValidAndInvalid_WorksCorrectly(string? input, bool expectedSuccess, int y, int m, int d)
    {
        var success = SmartInputHelper.TryParseDate(input, out var date);
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Equal(y, date.Year);
            Assert.Equal(m, date.Month);
            Assert.Equal(d, date.Day);
        }
    }

    [Theory]
    [InlineData("15.03.2026", "15.03.2026")]
    [InlineData("2026-03-15", "15.03.2026")]
    [InlineData("15/03/2026", "15.03.2026")]
    [InlineData("просто текст", "просто текст")]
    public void FormatDateForStorage_NormalizesDatesForDraftStore(string input, string expected)
    {
        var result = SmartInputHelper.FormatDateForStorage(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveKeyboard_ForDateField_ReturnsNumericKeyboard()
    {
        var keyboard = SmartInputHelper.ResolveKeyboard("Дата выдачи паспорта");
        Assert.Equal(Microsoft.Maui.Keyboard.Numeric, keyboard);
    }
}
