/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты локального маскировщика персональных данных
 * СОДЕРЖИМОЕ: PrivacyMaskerTests
 * ЗАВИСИМОСТИ: Xunit, GosTek.Services.PrivacyMasker
 * ==============================================================================
 */
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class PrivacyMaskerTests
{
    [Fact]
    public void Mask_NullOrEmpty_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, PrivacyMasker.Mask(null));
        Assert.Equal(string.Empty, PrivacyMasker.Mask(""));
        Assert.Equal(string.Empty, PrivacyMasker.Mask("   "));
    }

    [Fact]
    public void Mask_RegularTextWithoutPii_ReturnsOriginalText()
    {
        const string text = "Прошу предоставить мне трудовой отпуск на 14 календарных дней с 1 июня 2026 года.";
        var result = PrivacyMasker.Mask(text);
        Assert.Equal(text, result);
    }

    [Theory]
    [InlineData("Мой паспорт KN1234567 выдан РУВД", "Мой паспорт [ПАСПОРТ] выдан РУВД")]
    [InlineData("Серия и номер: MP 7654321", "Серия и номер: [ПАСПОРТ]")]
    [InlineData("Паспорт гражданина АВ 1234567", "Паспорт гражданина [ПАСПОРТ]")]
    [InlineData("Документ МВ-9876543 действителен", "Документ [ПАСПОРТ] действителен")]
    public void Mask_PassportData_MasksCorrectly(string input, string expected)
    {
        var result = PrivacyMasker.Mask(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Звоните на +375 29 123-45-67 после 18:00", "Звоните на [ТЕЛЕФОН] после 18:00")]
    [InlineData("Мой мобильный: +375291234567", "Мой мобильный: [ТЕЛЕФОН]")]
    [InlineData("Городской или велком 80 29 333-44-55", "Городской или велком [ТЕЛЕФОН]")]
    [InlineData("Телефон 80447654321 для связи", "Телефон [ТЕЛЕФОН] для связи")]
    [InlineData("Контакты: +375 (33) 987-65-43", "Контакты: [ТЕЛЕФОН]")]
    public void Mask_PhoneNumbers_MasksCorrectly(string input, string expected)
    {
        var result = PrivacyMasker.Mask(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Перечислить на счет BY20ALFA12345678901234567890 в банке", "Перечислить на счет [СЧЕТ] в банке")]
    [InlineData("Реквизиты: BY12 UNBS 0000 1234 5678 9012 3456 срочно", "Реквизиты: [СЧЕТ] срочно")]
    public void Mask_IbanAccount_MasksCorrectly(string input, string expected)
    {
        var result = PrivacyMasker.Mask(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Организация с УНП 192837465 зарегистрирована", "Организация с УНП [УНП] зарегистрирована")]
    [InlineData("УНП: 100123456 компании", "УНП: [УНП] компании")]
    public void Mask_Unp_MasksCorrectly(string input, string expected)
    {
        var result = PrivacyMasker.Mask(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Я, Иванов Иван Иванович, прошу предоставить", "Я, [ФИО], прошу предоставить")]
    [InlineData("Заявление от Сидоров Петр Сергеевич директору", "Заявление от [ФИО] директору")]
    [InlineData("Сотрудник Петров-Водкин Кузьма Сергеевич принят", "Сотрудник [ФИО] принят")]
    public void Mask_FullName_MasksCorrectly(string input, string expected)
    {
        var result = PrivacyMasker.Mask(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Mask_ComplexTextWithAllEntities_MasksAllSafely()
    {
        var input = "Я, Ковалев Сергей Дмитриевич, паспорт MP 1234567, телефон +375 29 999-88-77, счет BY20ALFA12345678901234567890, УНП 193456789, прошу выдать справку.";
        var expected = "Я, [ФИО], паспорт [ПАСПОРТ], телефон [ТЕЛЕФОН], счет [СЧЕТ], УНП [УНП], прошу выдать справку.";

        var result = PrivacyMasker.Mask(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Mask_NumberedPlaceholders_MasksWithSequentialNumbers()
    {
        var input = "Заявитель Иванов Иван Иванович передает полномочия Петров Петр Петрович. Телефон +375 29 111-22-33 или +375 44 222-33-44.";
        var result = PrivacyMasker.Mask(input, out var extracted, profile: null, numbered: true);

        Assert.Contains("[ФИО_1]", result);
        Assert.Contains("[ФИО_2]", result);
        Assert.Contains("[ТЕЛЕФОН_1]", result);
        Assert.Contains("[ТЕЛЕФОН_2]", result);
        Assert.Equal("Иванов Иван Иванович", extracted["[ФИО_1]"]);
        Assert.Equal("Петров Петр Петрович", extracted["[ФИО_2]"]);
    }

    [Fact]
    public void Mask_FuzzyProfileMatch_MasksLevenshteinTypo()
    {
        var profile = new ProfileData(FullName: "Даниил Александрович");
        var input = "Меня зовут Данил, прошу предоставить отпуск.";

        var result = PrivacyMasker.Mask(input, out var extracted, profile: profile, numbered: true);

        Assert.Contains("[ФИО_1]", result);
        Assert.DoesNotContain("Данил", result);
        Assert.Equal("Данил", extracted["[ФИО_1]"]);
    }

    [Fact]
    public void Mask_SuspiciousSingleName_DetectsAndMasks()
    {
        var profile = new ProfileData(FullName: "Сергей Петров");
        var input = "Разрешить доступ сотруднику Даниил на объект.";

        var result = PrivacyMasker.Mask(input, out var extracted, out var suspicious, profile: profile, numbered: true);

        Assert.Contains("[ФИО_1]", result);
        Assert.DoesNotContain("Даниил", result);
        Assert.Contains("Даниил", suspicious);
    }
}
