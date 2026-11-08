/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты алгоритма извлечения реквизитов из распознанного текста паспорта
 * СОДЕРЖИМОЕ: DocumentDataExtractorTests
 * ЗАВИСИМОСТИ: xUnit, GosTek.Services.DocumentDataExtractor
 * ==============================================================================
 */
using GosTek.Services;
using GosTek.Views;
using Xunit;

namespace GosTek.Tests;

public class DocumentDataExtractorTests
{
    [Fact]
    public void Extract_FullNameFromOcrText_FindsThreeCapitalizedWords()
    {
        var ocrText = "РЕСПУБЛИКА БЕЛАРУСЬ\nПАСПОРТ\nФамилия / Имя / Отчество:\nИванов Иван Иванович\nДата рождения: 01.01.1990";
        var result = DocumentDataExtractor.Extract(ocrText);

        Assert.NotNull(result.FullName);
        Assert.Equal("Иванов Иван Иванович", result.FullName);
    }

    [Fact]
    public void Extract_PassportFromOcrText_FindsSeriesAndNumber()
    {
        var ocrText = "ПАСПОРТ ГРАЖДАНИНА РЕСПУБЛИКИ БЕЛАРУСЬ\nСерия и номер:\nKH 1234567\nИдентификационный номер: 1234567A001PB1";
        var result = DocumentDataExtractor.Extract(ocrText);

        Assert.NotNull(result.Passport);
        Assert.Equal("KH 1234567", result.Passport);
    }

    [Fact]
    public void Extract_PassportWithCyrillicSeries_CanBeNormalizedByProfileRules()
    {
        // OCR часто распознает КН как русскую КН
        var ocrText = "Документ: КН 7654321 выдан РУВД";
        var result = DocumentDataExtractor.Extract(ocrText);

        Assert.NotNull(result.Passport);
        Assert.Equal("КН 7654321", result.Passport);

        // ProfileRules.Normalize переводит серию в латиницу KH
        var normalized = ProfileRules.Normalize(ProfileField.Passport, result.Passport);
        Assert.Equal("KH 7654321", normalized);
    }

    [Fact]
    public void Extract_EmptyOrIrrelevantText_ReturnsNulls()
    {
        var ocrText = "Случайный текст без паспорта и без полных трех слов ФИО";
        var result = DocumentDataExtractor.Extract(ocrText);

        Assert.Null(result.FullName);
        Assert.Null(result.Passport);
    }
}
