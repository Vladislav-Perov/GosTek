/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты исходящего шлюза безопасности ИИ (Outbound Gateway)
 * СОДЕРЖИМОЕ: OutboundGatewayTests
 * ЗАВИСИМОСТИ: Xunit, GosTek.Services.AiAssistantService, GosTek.ProfileData, GosTek.Views.ProfileRules
 * ==============================================================================
 */
using System.Text.RegularExpressions;
using GosTek.Models;
using GosTek.Services;
using GosTek.Views;
using Xunit;

namespace GosTek.Tests;

public class OutboundGatewayTests
{
    [Fact]
    public void SanitizeOutgoingPayload_MasksCurrentFormFields_PreventsPiiLeak()
    {
        var rawFields = new Dictionary<string, string>
        {
            ["ФИО"] = "Иванов Иван Иванович",
            ["Паспорт"] = "MP1234567",
            ["Телефон"] = "+375 29 123-45-67",
            ["Счёт"] = "BY96AKBB36029170100040000000",
            ["Причина"] = "болел"
        };

        var (safeRequest, placeholders) = AiAssistantService.SanitizeOutgoingPayload(
            userText: "Заполни форму",
            fieldNames: rawFields.Keys.ToList(),
            currentFormFields: rawFields,
            templateName: "Заявление Иванова Ивана");

        // Ни один сырой PII не должен попасть в FieldsContext или Prompt
        Assert.DoesNotContain("Иванов Иван Иванович", safeRequest.FieldsContext);
        Assert.DoesNotContain("MP1234567", safeRequest.FieldsContext);
        Assert.DoesNotContain("+375 29 123-45-67", safeRequest.FieldsContext);
        Assert.DoesNotContain("BY96AKBB36029170100040000000", safeRequest.FieldsContext);

        Assert.DoesNotContain("Иванов Иван Иванович", safeRequest.Prompt);
        Assert.DoesNotContain("MP1234567", safeRequest.Prompt);

        // Все плейсхолдеры зарегистрированы для последующего восстановления
        Assert.Contains(placeholders.Values, v => v == "Иванов Иван Иванович");
        Assert.Contains(placeholders.Values, v => v == "MP1234567");
        Assert.Contains(placeholders.Values, v => v == "+375 29 123-45-67");
        Assert.Contains(placeholders.Values, v => v == "BY96AKBB36029170100040000000");
    }

    [Fact]
    public void SanitizeOutgoingPayload_MasksMemoryFacts()
    {
        var memoryFacts = new List<string>
        {
            "заявитель: Петров Петр Петрович",
            "телефон для связи: +375 44 987-65-43"
        };

        var (safeRequest, placeholders) = AiAssistantService.SanitizeOutgoingPayload(
            userText: "Повтори заявку",
            fieldNames: new List<string> { "ФИО", "Телефон" },
            memory: memoryFacts);

        Assert.NotNull(safeRequest.Memory);
        foreach (var fact in safeRequest.Memory)
        {
            Assert.DoesNotContain("Петров Петр Петрович", fact);
            Assert.DoesNotContain("+375 44 987-65-43", fact);
        }
    }

    [Fact]
    public void AssertPayloadSafe_ThrowsSecurityException_WhenRawPassportPresent()
    {
        var request = new AiProxyRequest
        {
            Prompt = "Данные заявителя: AB1234567",
            UserText = "Тест",
            FieldsContext = "Тест"
        };

        Assert.Throws<AiAssistantSecurityException>(() =>
            AiAssistantService.AssertPayloadSafe(request, profile: null));
    }

    [Fact]
    public void AssertPayloadSafe_ThrowsSecurityException_WhenRawPhonePresent()
    {
        var request = new AiProxyRequest
        {
            Prompt = "Позвонить по номеру +375 29 555-44-33",
            UserText = "Тест",
            FieldsContext = "Тест"
        };

        Assert.Throws<AiAssistantSecurityException>(() =>
            AiAssistantService.AssertPayloadSafe(request, profile: null));
    }

    [Fact]
    public void AssertPayloadSafe_ThrowsSecurityException_WhenRawIbanPresent()
    {
        var request = new AiProxyRequest
        {
            Prompt = "Перечислить на счет BY96AKBB36029170100040000000",
            UserText = "Тест",
            FieldsContext = "Тест"
        };

        Assert.Throws<AiAssistantSecurityException>(() =>
            AiAssistantService.AssertPayloadSafe(request, profile: null));
    }

    [Fact]
    public void AssertPayloadSafe_ThrowsSecurityException_WhenProfileFullNameLeaked()
    {
        var profile = new ProfileData(
            FullName: "Сидорова Анна Павловна",
            Passport: "KH7654321");

        var request = new AiProxyRequest
        {
            Prompt = "Документ согласован с Сидорова Анна Павловна",
            UserText = "Тест",
            FieldsContext = "Тест"
        };

        Assert.Throws<AiAssistantSecurityException>(() =>
            AiAssistantService.AssertPayloadSafe(request, profile: profile));
    }

    [Fact]
    public void ClearTemp_RemovesImagesAndTemporaryFiles()
    {
        var testCacheDir = Path.Combine(Path.GetTempPath(), $"GosTek_TestCache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testCacheDir);
        FileSaveService.CacheDirectoryResolver = () => testCacheDir;

        var tempJpg = Path.Combine(testCacheDir, $"test_{Guid.NewGuid():N}.jpg");
        var tempDocx = Path.Combine(testCacheDir, $"test_{Guid.NewGuid():N}.docx");
        var tempPng = Path.Combine(testCacheDir, $"test_{Guid.NewGuid():N}.png");

        File.WriteAllText(tempJpg, "fake image");
        File.WriteAllText(tempDocx, "fake doc");
        File.WriteAllText(tempPng, "fake png");

        try
        {
            int deleted = FileSaveService.ClearTemp();
            Assert.True(deleted >= 3);
            Assert.False(File.Exists(tempJpg));
            Assert.False(File.Exists(tempDocx));
            Assert.False(File.Exists(tempPng));
        }
        finally
        {
            FileSaveService.CacheDirectoryResolver = null;
            try { Directory.Delete(testCacheDir, true); } catch { }
        }
    }
}
