/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты для MrzParserService, MrzSanitizer и TransliterationHelper на реальных шумных данных
 * СОДЕРЖИМОЕ: MrzParserServiceTests
 * ЗАВИСИМОСТИ: xUnit, GosTek.Services
 * ==============================================================================
 */
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class MrzParserServiceTests
{
    [Fact]
    public void Parse_RealNoiseDataFromPrompt_ExtractsExpectedFieldsCorrectly()
    {
        // Реальный шум с фото из задачи 6
        string noiseText = "F3CI : REl'L'BLfC OF BLR SAYAPIN ANDRE1 REPUBUC 0F BELARUS 26 04 1983 KРИ17879 ешмм_пттн.• REPUBLIC OF BELARUS 07 05 2008 •,ш-ПТ-ГЧ MlNlSTRY 0F lNJERNAL AFFAlRS МРГ41 78795BLR83DQ65M28042683260483A01 1PB648";

        var result = MrzParserService.Parse(noiseText);

        Assert.True(result.Success);
        Assert.Equal("SAYAPIN", result.Surname);
        Assert.Equal("САЯПИН", result.SurnameCyrillic);
        Assert.Equal("ANDREI", result.Name);
        Assert.Equal("АНДРЕЙ", result.NameCyrillic);
        Assert.Equal("MP2417879", result.PassportNumber);
        Assert.Equal("26.04.1983", result.BirthDateString);
        Assert.Equal("3260483A011PB6", result.PersonalNumber);
    }

    [Fact]
    public void DocumentDataExtractor_Extract_RealNoiseData_ExtractsAllPassportFields()
    {
        string noiseText = "F3CI : REl'L'BLfC OF BLR SAYAPIN ANDRE1 REPUBUC 0F BELARUS 26 04 1983 KРИ17879 ешмм_пттн.• REPUBLIC OF BELARUS 07 05 2008 •,ш-ПТ-ГЧ MlNlSTRY 0F lNJERNAL AFFAlRS МРГ41 78795BLR83DQ65M28042683260483A01 1PB648";

        var extracted = DocumentDataExtractor.Extract(noiseText);

        Assert.True(extracted.IsValid);
        Assert.Equal("MP2417879", extracted.Passport);
        Assert.Equal("3260483A011PB6", extracted.PersonalNumber);
        Assert.Equal(new DateTime(1983, 4, 26), extracted.BirthDate);
        Assert.Equal("САЯПИН АНДРЕЙ", extracted.FullName);
    }

    [Fact]
    public void MrzSanitizer_Sanitize_LeavesVisualTextUntouched()
    {
        string visualText = "Иванов Иван Иванович 26 04 1983";
        string result = MrzSanitizer.Sanitize(visualText);

        Assert.Equal(visualText, result);
    }

    [Fact]
    public void MrzSanitizer_Sanitize_ProcessesMrzWithHomoglyphsAndRemovesSpaces()
    {
        string mrzLine = "МРГ41 78795BLR83DQ65M";
        string sanitized = MrzSanitizer.Sanitize(mrzLine);

        // 'М' -> 'M', 'Р' -> 'P', пробел удален
        Assert.StartsWith("MP", sanitized);
        Assert.DoesNotContain(" ", sanitized);
    }

    [Fact]
    public void MrzSanitizer_SanitizeNumbers_ConvertsHomoglyphsToDigits()
    {
        string messyNumbers = "O1D l Z Г З Ч S B Б В";
        string result = MrzSanitizer.SanitizeNumbers(messyNumbers);

        Assert.Equal("010122345888", result);
    }

    [Fact]
    public void TransliterationHelper_TransliteratesNamesUsingDictionary()
    {
        Assert.Equal("СЕРГЕЙ", TransliterationHelper.TransliterateName("siarhei"));
        Assert.Equal("АЛЕКСАНДР", TransliterationHelper.TransliterateName("ALIAKSANDR"));
        Assert.Equal("ДМИТРИЙ", TransliterationHelper.TransliterateName("Dzmitry"));
        Assert.Equal("ЕВГЕНИЙ", TransliterationHelper.TransliterateName("YAUHENI"));
        Assert.Equal("АНДРЕЙ", TransliterationHelper.TransliterateName("ANDREI"));
        Assert.Equal("АНДРЕЙ", TransliterationHelper.TransliterateName("ANDRE1"));
    }

    [Fact]
    public void TransliterationHelper_TransliteratesSurnamesWithMultiCharRules()
    {
        Assert.Equal("САЯПИН", TransliterationHelper.TransliterateSurname("SAYAPIN"));
        Assert.Equal("ШЕВЧЕНКО", TransliterationHelper.TransliterateSurname("SHEVCHENKO"));
        Assert.Equal("ЧЕРНЫШ", TransliterationHelper.TransliterateSurname("CHERNYSH"));
        Assert.Equal("ЖУКОВ", TransliterationHelper.TransliterateSurname("ZHUKOV"));
        Assert.Equal("ХРОМОВ", TransliterationHelper.TransliterateSurname("KHROMOV"));
        Assert.Equal("ЦВЕТКОВ", TransliterationHelper.TransliterateSurname("TSVETKOV"));
        Assert.Equal("ДЗЯМЕНЦЕУ", TransliterationHelper.TransliterateSurname("DZYAMYENTSYEU"));
    }
}
