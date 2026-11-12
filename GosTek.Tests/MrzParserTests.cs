/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты для MRZ парсера ICAO 9303 и DocumentDataExtractor (Этап 7)
 * СОДЕРЖИМОЕ: MrzParserTests
 * ЗАВИСИМОСТИ: GosTek.Services, Xunit
 * ==============================================================================
 */

using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class MrzParserTests
{
    [Theory]
    [InlineData("1234567", 4)] // 1*7 + 2*3 + 3*1 + 4*7 + 5*3 + 6*1 + 7*7 = 7+6+3+28+15+6+49 = 114 % 10 = 4
    [InlineData("850101", 9)]  // 8*7 + 5*3 + 0*1 + 1*7 + 0*3 + 1*1 = 56+15+0+7+0+1 = 79 % 10 = 9
    [InlineData("MC1234567", 8)] // 22*7 + 12*3 + 1*1 + 2*7 + 3*3 + 4*1 + 5*7 + 6*3 + 7*1 = 278 % 10 = 8
    public void CalculateCheckDigit_ComputesAccordingToIcao9303(string value, int expected)
    {
        int actual = MrzParser.CalculateCheckDigit(value);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseTd3_ValidPassport_ExtractsAllFieldsAndValidatesSums()
    {
        // Строка 1: Паспорт РБ, фамилия IVANOV, имя IVAN
        string line1 = "P<BLRIVANOV<<IVAN<<<<<<<<<<<<<<<<<<<<<<<<<<<";
        // Строка 2:
        // MC1234567 + check 4
        // BLR
        // 850101 + check 9
        // M
        // 300101 + check 9 (3*7 + 0*3 + 0*1 + 1*7 + 0*3 + 1*1 = 21+0+0+7+0+1 = 29 % 10 = 9)
        // 3010185A001PB4 + check <
        // composite check
        string doc = "MC1234567";
        int docCheck = MrzParser.CalculateCheckDigit(doc); // 4

        string birth = "850101";
        int birthCheck = MrzParser.CalculateCheckDigit(birth); // 9

        string expiry = "300101";
        int expiryCheck = MrzParser.CalculateCheckDigit(expiry); // 9

        string personal = "3010185A001PB4";

        string compositeSource = doc + docCheck + birth + birthCheck + expiry + expiryCheck + personal + "<";
        int overallCheck = MrzParser.CalculateCheckDigit(compositeSource);

        string line2 = $"{doc}{docCheck}BLR{birth}{birthCheck}M{expiry}{expiryCheck}{personal}<{overallCheck}";

        var result = MrzParser.ParseTd3(line1, line2);

        Assert.NotNull(result);
        Assert.Equal(MrzFormat.Td3, result.Format);
        Assert.Equal("IVANOV", result.LastName);
        Assert.Equal("IVAN", result.FirstName);
        Assert.Equal("IVANOV IVAN", result.FullName);
        Assert.Equal("MC 1234567", result.DocumentNumber);
        Assert.True(result.DocumentNumberValid);
        Assert.Equal("3010185A001PB4", result.PersonalNumber);
        Assert.True(result.PersonalNumberValid);
        Assert.Equal("Мужской", result.Sex);
        Assert.Equal(new DateTime(1985, 1, 1), result.BirthDate);
        Assert.True(result.BirthDateValid);
        Assert.Equal(new DateTime(2030, 1, 1), result.ExpiryDate);
        Assert.True(result.ExpiryDateValid);
        Assert.True(result.OverallValid);
    }

    [Fact]
    public void ParseTd3_CorruptedCheckDigit_FlagsAsInvalid()
    {
        string line1 = "P<BLRIVANOV<<IVAN<<<<<<<<<<<<<<<<<<<<<<<<<<<";
        // Неверная контрольная цифра номера документа (вместо 4 стоит 0)
        string line2 = "MC12345670BLR8501019M30010193010185A001PB4<1";

        var result = MrzParser.ParseTd3(line1, line2);

        Assert.NotNull(result);
        Assert.False(result.DocumentNumberValid);
    }

    [Fact]
    public void ParseTd3_OcrReplacements_NormalizesZeroAndOne()
    {
        // Буква 'O' вместо '0', буква 'I' вместо '1' в числовых зонах
        string rawLine1 = "P<BLRIVANOV<<IVAN<<<<<<<<<<<<<<<<<<<<<<<<<<<";
        string rawLine2 = "MC12345678BLR85OIOI9M3OOIOI93OIOI85AOOIPB4<6";

        var result = MrzParser.ParseTd3(rawLine1, rawLine2);

        Assert.NotNull(result);
        Assert.Equal("MC 1234567", result.DocumentNumber);
        Assert.Equal(new DateTime(1985, 1, 1), result.BirthDate);
        Assert.True(result.BirthDateValid);
    }

    [Fact]
    public void ParseTd1_ValidIdCard_ExtractsAllFields()
    {
        // ID-карта РБ: 3 строки по 30 символов
        string line1 = "I<BLR00123456743010185A001PB4<";
        string line2 = "8501019M3001019BLR<<<<<<<<<<<5";
        string line3 = "IVANOV<<IVAN<<<<<<<<<<<<<<<<<<";

        var result = MrzParser.ParseTd1(line1, line2, line3);

        Assert.NotNull(result);
        Assert.Equal(MrzFormat.Td1, result.Format);
        Assert.Equal("IVANOV IVAN", result.FullName);
        Assert.Equal("001234567", result.DocumentNumber);
        Assert.Equal("3010185A001PB4", result.PersonalNumber);
        Assert.Equal(new DateTime(1985, 1, 1), result.BirthDate);
        Assert.True(result.BirthDateValid);
        Assert.Equal("Мужской", result.Sex);
    }

    [Fact]
    public void DocumentDataExtractor_PrefersMrzOverRegex()
    {
        string ocrText = @"РЕСПУБЛИКА БЕЛАРУСЬ
ПАСПОРТ
P<BLRIVANOV<<IVAN<<<<<<<<<<<<<<<<<<<<<<<<<<<
MC12345678BLR8501019M30010193010185A001PB4<6
Минское РУВД";

        var data = DocumentDataExtractor.Extract(ocrText);

        Assert.True(data.IsMrz);
        Assert.True(data.IsValid);
        Assert.Equal("IVANOV IVAN", data.FullName);
        Assert.Equal("MC 1234567", data.Passport);
        Assert.Equal("3010185A001PB4", data.PersonalNumber);
    }

    [Fact]
    public void DocumentDataExtractor_FallbackToRegex_WhenNoMrz()
    {
        string ocrText = @"Паспорт выдан гражданину
Сидоров Алексей Михайлович
Серия и номер документа: AB 7654321
Дата выдачи 12.05.2018";

        var data = DocumentDataExtractor.Extract(ocrText);

        Assert.False(data.IsMrz);
        Assert.Equal("Сидоров Алексей Михайлович", data.FullName);
        Assert.Equal("AB 7654321", data.Passport);
    }
}
