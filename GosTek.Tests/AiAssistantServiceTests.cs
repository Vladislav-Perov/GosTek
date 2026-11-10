/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты сервиса облачного ИИ-помощника, системного промпта и восстановления плейсхолдеров
 * СОДЕРЖИМОЕ: AiAssistantServiceTests, MockHttpMessageHandler
 * ЗАВИСИМОСТИ: Xunit, System.Net.Http, GosTek.Services.AiAssistantService, GosTek.ProfileData
 * ==============================================================================
 */
using System.Net;
using System.Text;
using GosTek.Models;
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class AiAssistantServiceTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public int CallsCount { get; private set; }

        public MockHttpMessageHandler(IEnumerable<HttpResponseMessage> responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallsCount++;
            if (_responses.Count > 0)
                return Task.FromResult(_responses.Dequeue());

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    [Fact]
    public void ExtractJson_StripsMarkdownBackticksAndSurroundingText()
    {
        const string raw = "Вот результат:\n```json\n{\n  \"fields\": {\"FIO\": \"Иванов\"},\n  \"question\": null\n}\n```\nНадеюсь помогло.";
        var json = AiAssistantService.ExtractJson(raw);

        Assert.StartsWith("{", json);
        Assert.EndsWith("}", json);
        Assert.Contains("\"FIO\"", json);
    }

    [Fact]
    public void ParseAiResponse_ValidJson_ReturnsPopulatedFields()
    {
        const string json = "{\"fields\": {\"ФИО\": \"Петров П.П.\", \"Должность\": \"Инженер\"}, \"question\": null}";
        var result = AiAssistantService.ParseAiResponse(json);

        Assert.Equal(2, result.Fields.Count);
        Assert.Equal("Петров П.П.", result.Fields["ФИО"]);
        Assert.Equal("Инженер", result.Fields["Должность"]);
        Assert.Null(result.Question);
    }

    [Fact]
    public void ParseAiResponse_WithQuestion_ReturnsQuestionProperty()
    {
        const string json = "{\"fields\": {}, \"question\": \"Уточните, с какого числа начинается отпуск?\"}";
        var result = AiAssistantService.ParseAiResponse(json);

        Assert.Empty(result.Fields);
        Assert.Equal("Уточните, с какого числа начинается отпуск?", result.Question);
    }

    [Fact]
    public void ParseAiResponse_WithNullStringQuestion_ReturnsNullQuestion()
    {
        const string json = "{\"fields\": {\"Причина\": \"Отпуск\"}, \"question\": \"null\"}";
        var result = AiAssistantService.ParseAiResponse(json);

        Assert.Single(result.Fields);
        Assert.Null(result.Question);
    }

    [Fact]
    public void ParseAiResponse_InvalidJson_ThrowsAiAssistantException()
    {
        const string invalid = "Просто текст без JSON структуры";
        Assert.Throws<AiAssistantException>(() => AiAssistantService.ParseAiResponse(invalid));
    }

    [Fact]
    public void BuildPrompt_ContainsSecretaryRoleAndOfficialStyleRules()
    {
        var testDate = new DateTime(2026, 11, 10);
        var currentFields = new Dictionary<string, string>
        {
            ["ФИО"] = "Иванов Иван Иванович",
            ["Причина"] = ""
        };

        var prompt = AiAssistantService.BuildPrompt(
            "проспал",
            new List<string> { "ФИО", "Причина", "Дата" },
            currentFormFields: currentFields,
            today: testDate);

        Assert.Contains("строгий секретарь-делопроизводитель", prompt);
        Assert.Contains("ГОСТ Р 7.0.97-2016", prompt);
        Assert.Contains("ПРАВИЛО 1 (Расширение смысла)", prompt);
        Assert.Contains("проспал", prompt);
        Assert.Contains("ПРАВИЛО 2 (Уточняющие вопросы)", prompt);
        Assert.Contains("Сегодня 10.11.2026", prompt);
        Assert.Contains("вчера было 09.11.2026", prompt);
        Assert.Contains("[ФИО]", prompt);
        Assert.Contains("[ПАСПОРТ]", prompt);
        Assert.Contains("Текущее состояние полей в форме: [ФИО: Иванов Иван Иванович, Причина: [пусто]]", prompt);
        Assert.Contains("Если поле УЖЕ имеет значение, не проси пользователя его вводить", prompt);
    }

    [Fact]
    public async Task RestorePlaceholdersAsync_WithExtractedPlaceholders_RestoresOriginalValues()
    {
        var fields = new Dictionary<string, string>
        {
            ["ФИО"] = "[ФИО]",
            ["Паспортные данные"] = "[ПАСПОРТ]",
            ["Телефон"] = "[ТЕЛЕФОН]"
        };

        var extracted = new Dictionary<string, string>
        {
            [PrivacyMasker.MaskFullName] = "Сидоров Сидор Сидорович",
            [PrivacyMasker.MaskPassport] = "MP1234567",
            [PrivacyMasker.MaskPhone] = "+375 29 111-22-33"
        };

        var restored = await AiAssistantService.RestorePlaceholdersAsync(fields, extracted);

        Assert.Equal("Сидоров Сидор Сидорович", restored["ФИО"]);
        Assert.Equal("MP1234567", restored["Паспортные данные"]);
        Assert.Equal("+375 29 111-22-33", restored["Телефон"]);
    }

    [Fact]
    public async Task RestorePlaceholdersAsync_WithProfileData_RestoresValuesFromProfile()
    {
        var fields = new Dictionary<string, string>
        {
            ["ФИО заявителя"] = "[ФИО]",
            ["Счет"] = "[СЧЕТ]"
        };

        var profile = new ProfileData(
            FullName: "Ковалев Алексей Сергеевич",
            Account: "BY20UNBS88880000111122223333"
        );

        var restored = await AiAssistantService.RestorePlaceholdersAsync(fields, profileOverride: profile);

        Assert.Equal("Ковалев Алексей Сергеевич", restored["ФИО заявителя"]);
        Assert.Equal("BY20UNBS88880000111122223333", restored["Счет"]);
    }

    [Fact]
    public async Task ProcessRequestAsync_SuccessResponse_ReturnsResult()
    {
        var responsePayload = "{\"text\": \"{\\\"fields\\\": {\\\"Причина\\\": \\\"Семейные обстоятельства\\\"}, \\\"question\\\": null}\"}";
        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responsePayload, Encoding.UTF8, "application/json")
        };

        var handler = new MockHttpMessageHandler(new[] { mockResponse });
        using var client = new HttpClient(handler);
        var service = new AiAssistantService(client);

        var result = await service.ProcessRequestAsync("Заявление на отпуск", new List<string> { "Причина" });

        Assert.Single(result.Fields);
        Assert.Equal("Семейные обстоятельства", result.Fields["Причина"]);
        Assert.Equal(1, handler.CallsCount);
    }

    [Fact]
    public async Task ProcessRequestAsync_WithCurrentFormFields_PassesFormContext()
    {
        var responsePayload = "{\"text\": \"{\\\"fields\\\": {\\\"Причина\\\": \\\"Опоздал в связи с личными обстоятельствами\\\"}, \\\"question\\\": null}\"}";
        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responsePayload, Encoding.UTF8, "application/json")
        };

        var handler = new MockHttpMessageHandler(new[] { mockResponse });
        using var client = new HttpClient(handler);
        var service = new AiAssistantService(client);

        var formFields = new Dictionary<string, string> { ["ФИО"] = "Иванов Иван" };
        var result = await service.ProcessRequestAsync("опоздал", new List<string> { "ФИО", "Причина" }, currentFormFields: formFields);

        Assert.Equal("Опоздал в связи с личными обстоятельствами", result.Fields["Причина"]);
    }

    [Fact]
    public async Task ProcessRequestAsync_WithExtractedPlaceholders_RestoresPlaceholdersInResult()
    {
        var responsePayload = "{\"text\": \"{\\\"fields\\\": {\\\"ФИО\\\": \\\"[ФИО]\\\"}, \\\"question\\\": null}\"}";
        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responsePayload, Encoding.UTF8, "application/json")
        };

        var handler = new MockHttpMessageHandler(new[] { mockResponse });
        using var client = new HttpClient(handler);
        var service = new AiAssistantService(client);

        var extracted = new Dictionary<string, string>
        {
            [PrivacyMasker.MaskFullName] = "Кузнецов Петр Васильевич"
        };

        var result = await service.ProcessRequestAsync("[ФИО]", new List<string> { "ФИО" }, extractedPlaceholders: extracted);

        Assert.Equal("Кузнецов Петр Васильевич", result.Fields["ФИО"]);
    }

    [Fact]
    public async Task ProcessRequestAsync_503RetrySuccess_RetriesAndSucceeds()
    {
        var response503 = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var response200 = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"text\": \"{\\\"fields\\\": {\\\"Город\\\": \\\"Минск\\\"}}\"}", Encoding.UTF8, "application/json")
        };

        var handler = new MockHttpMessageHandler(new[] { response503, response200 });
        using var client = new HttpClient(handler);
        var service = new AiAssistantService(client);

        var result = await service.ProcessRequestAsync("Город проживания", new List<string> { "Город" });

        Assert.Equal("Минск", result.Fields["Город"]);
        Assert.Equal(2, handler.CallsCount);
    }

    [Fact]
    public async Task ProcessRequestAsync_503ExhaustedRetries_ThrowsAiAssistantException()
    {
        var responses = new[]
        {
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        };

        var handler = new MockHttpMessageHandler(responses);
        using var client = new HttpClient(handler);
        var service = new AiAssistantService(client);

        var ex = await Assert.ThrowsAsync<AiAssistantException>(() =>
            service.ProcessRequestAsync("Тестовый запрос", new List<string> { "Поле" }));

        Assert.Contains("503", ex.Message);
        Assert.Equal(3, handler.CallsCount);
    }

    [Fact]
    public void AiMemory_AddRemoveClear_MaintainsStateAndInvokesChanged()
    {
        var memory = new AiMemory();
        int changedCount = 0;
        memory.Changed += () => changedCount++;

        memory.AddFact("subject", "документ на брата");
        Assert.Single(memory.Facts);
        Assert.Equal("документ на брата", memory.Facts[0].Value);
        Assert.Equal(1, changedCount);

        var promptList = memory.FormatForPrompt();
        Assert.Contains("subject: документ на брата", promptList);

        memory.RemoveFact("subject");
        Assert.Empty(memory.Facts);
        Assert.Equal(2, changedCount);

        memory.AddFact("reason", "проспал");
        memory.Clear();
        Assert.Empty(memory.Facts);
        Assert.Equal(4, changedCount);
    }

    [Fact]
    public void ParseServerResponse_StructuredJsonWithMemoryOps_ParsesCorrectly()
    {
        const string json = @"{
            ""success"": true,
            ""data"": {
                ""fields"": { ""Причина"": ""Отсутствовал по уважительной причине"" },
                ""question"": null,
                ""status"": ""success""
            },
            ""memory_ops"": [
                { ""action"": ""add"", ""key"": ""subject"", ""value"": ""документ на брата"" }
            ]
        }";

        var result = AiAssistantService.ParseServerResponse(json);

        Assert.Equal("success", result.Status);
        Assert.Null(result.Question);
        Assert.Equal("Отсутствовал по уважительной причине", result.Fields["Причина"]);
        Assert.NotNull(result.MemoryOps);
        Assert.Single(result.MemoryOps);
        Assert.Equal("subject", result.MemoryOps[0].Key);
    }

    [Fact]
    public async Task RestorePlaceholdersAsync_WithNumberedPlaceholders_RestoresNumberedMatches()
    {
        var fields = new Dictionary<string, string>
        {
            ["Заявитель"] = "[ФИО_1]",
            ["Представитель"] = "[ФИО_2]"
        };

        var extracted = new Dictionary<string, string>
        {
            ["[ФИО_1]"] = "Иванов Иван",
            ["[ФИО_2]"] = "Петров Петр"
        };

        var restored = await AiAssistantService.RestorePlaceholdersAsync(fields, extracted);

        Assert.Equal("Иванов Иван", restored["Заявитель"]);
        Assert.Equal("Петров Петр", restored["Представитель"]);
    }
}
