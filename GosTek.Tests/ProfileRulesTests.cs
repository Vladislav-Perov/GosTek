/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты валидаторов реквизитов РБ (IBAN mod 97, УНП, серия паспорта, телефоны, ФИО)
 * СОДЕРЖИМОЕ: ProfileRulesTests
 * ЗАВИСИМОСТИ: xUnit, GosTek.Views.ProfileRules
 * ==============================================================================
 */
using GosTek.Views;
using Xunit;

namespace GosTek.Tests;

public class ProfileRulesTests
{
    public ProfileRulesTests()
    {
        ProfileRules.PreferencesProvider = new FakePreferences();
    }

    // ===== IBAN mod 97 =====
    [Fact]
    public void ValidateAccount_ValidIban_ReturnsNull()
    {
        // Valid Belarusian IBAN: BY + 2 check digits + 4 bank code + 4 balance + 16 account
        var validIban = "BY96AKBB36029170100040000000";
        var error = ProfileRules.Validate(ProfileField.Account, validIban);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("BY95AKBB36029170100040000000", "Неверная контрольная сумма счёта")]
    [InlineData("RU96AKBB36029170100040000000", "Счёт должен начинаться с BY")]
    [InlineData("BY96AKBB3602", "Счёт (IBAN) — 28 символов")]
    [InlineData("BY96AKBB36029170100040000000123", "Счёт (IBAN) — 28 символов")]
    public void ValidateAccount_InvalidIban_ReturnsExpectedError(string iban, string errorSubstring)
    {
        var error = ProfileRules.Validate(ProfileField.Account, iban);
        Assert.NotNull(error);
        Assert.Contains(errorSubstring, error);
    }

    // ===== УНП (9 цифр) =====
    [Theory]
    [InlineData("123456789", true)]
    [InlineData("12345678", false)]
    [InlineData("1234567890", false)]
    [InlineData("12345A789", false)]
    public void ValidateUnp_ChecksExact9Digits(string unp, bool isValid)
    {
        var error = ProfileRules.Validate(ProfileField.Unp, unp);
        if (isValid)
            Assert.Null(error);
        else
            Assert.NotNull(error);
    }

    // ===== Паспорт (2 буквы + 7 цифр, транслитерация) =====
    [Theory]
    [InlineData("KH1234567", null)]
    [InlineData("KH 1234567", null)]
    [InlineData("КН 1234567", null)] // Кириллица К->K, Н->H
    [InlineData("МР 1234567", null)] // Кириллица М->M, Р->P
    [InlineData("ВМ 1234567", null)] // Кириллица В->B, М->M
    [InlineData("1234567", "Формат: 2 буквы и 7 цифр")]
    [InlineData("K1234567", "Формат: 2 буквы и 7 цифр")]
    [InlineData("AB123456", "Формат: 2 буквы и 7 цифр")]
    public void ValidatePassport_ChecksFormatAndTransliteration(string passport, string? expectedErrorSubstring)
    {
        var error = ProfileRules.Validate(ProfileField.Passport, passport);
        if (expectedErrorSubstring is null)
            Assert.Null(error);
        else {
            Assert.NotNull(error);
            Assert.Contains(expectedErrorSubstring, error);
        }
    }

    [Theory]
    [InlineData("КН 1234567", "KH 1234567")]
    [InlineData("вм 7654321", "BM 7654321")]
    [InlineData("ав 0000001", "AB 0000001")]
    [InlineData("ех 2222222", "EX 2222222")]
    public void NormalizePassport_ConvertsCyrillicToLatinAndFormats(string raw, string expected)
    {
        var normalized = ProfileRules.Normalize(ProfileField.Passport, raw);
        Assert.Equal(expected, normalized);
    }

    // ===== Телефон (+375 / 80) =====
    [Theory]
    [InlineData("+375 29 123-45-67", true)]
    [InlineData("+375 (29) 1234567", true)]
    [InlineData("80 29 123-45-67", true)]
    [InlineData("80291234567", true)]
    [InlineData("+7 999 123-45-67", false)]
    [InlineData("+375 29 123", false)]
    [InlineData("invalid-phone", false)]
    public void ValidatePhone_RequiresBelarusFormat(string phone, bool isValid)
    {
        var error = ProfileRules.Validate(ProfileField.Phone, phone);
        if (isValid)
            Assert.Null(error);
        else
            Assert.NotNull(error);
    }

    // ===== ФИО (ровно 3 слова, минимум 3 буквы, дефис) =====
    [Theory]
    [InlineData("Иванов Иван Иванович", true)]
    [InlineData("Иванов-Петров Иван Иванович", true)]
    [InlineData("Иванов Иван", false)]                // 2 слова
    [InlineData("Иванов Иван Иванович Младший", false)]// 4 слова
    [InlineData("Ив Иван Иванович", false)]            // Первое слово < 3 букв
    [InlineData("Иванов И. И.", false)]                // Сокращения / точки
    public void ValidateFullName_RequiresThreeFullWords(string fullName, bool isValid)
    {
        var error = ProfileRules.Validate(ProfileField.FullName, fullName);
        if (isValid)
            Assert.Null(error);
        else
            Assert.NotNull(error);
    }
}
