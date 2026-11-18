using System.Net;
using System.Text;
using System.Text.Json;
using GosTek.Models;
using GosTek.Services;

namespace GosTek.Tests;

public class TextGenerationServiceTests
{
    [Fact]
    public async Task GenerateAsync_SendsOnlyChatTextAndKind()
    {
        using var handler = new CaptureHandler("""{"text":"Готовый текст.","question":null}""");
        using var client = new HttpClient(handler);
        var service = new TextGenerationService(client, new Uri("https://example.com/generate"));

        var result = await service.GenerateAsync("Опоздал из-за транспорта", TextGenerationKind.Explanation, null);

        Assert.Equal("Готовый текст.", result.Text);
        using var request = JsonDocument.Parse(handler.Body!);
        Assert.Equal(["message", "kind"], request.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("explanation", request.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task GenerateAsync_RevisionMasksBothPartsAndRestoresDistinctValues()
    {
        using var handler = new CaptureHandler("""{"text":"[ПАСПОРТ_1], [ПАСПОРТ_3]","question":null}""");
        using var client = new HttpClient(handler);
        var service = new TextGenerationService(client, new Uri("https://example.com/generate"));

        var result = await service.GenerateAsync("Добавь MP1234567", TextGenerationKind.DocumentText,
            "Первый документ: AB7654321");

        Assert.DoesNotContain("MP1234567", handler.Body);
        Assert.DoesNotContain("AB7654321", handler.Body);
        using var request = JsonDocument.Parse(handler.Body!);
        Assert.Contains("[ПАСПОРТ_3]", request.RootElement.GetProperty("previousText").GetString());
        Assert.Equal("MP1234567, AB7654321", result.Text);
    }

    [Theory]
    [InlineData("""{"text":"Текст","question":"Вопрос?"}""")]
    [InlineData("""{"text":null,"question":null}""")]
    public async Task GenerateAsync_RejectsAmbiguousServerResponses(string response)
    {
        using var handler = new CaptureHandler(response);
        using var client = new HttpClient(handler);
        var service = new TextGenerationService(client, new Uri("https://example.com/generate"));

        await Assert.ThrowsAsync<AiAssistantException>(() =>
            service.GenerateAsync("Опоздал", TextGenerationKind.Explanation, null));
    }

    [Fact]
    public async Task GenerateAsync_MissingEndpointDoesNotSendRequest()
    {
        using var handler = new CaptureHandler("{}");
        using var client = new HttpClient(handler);
        var service = new TextGenerationService(client, null);

        await Assert.ThrowsAsync<AiAssistantException>(() =>
            service.GenerateAsync("Опоздал", TextGenerationKind.Explanation, null));
        Assert.Null(handler.Body);
    }

    private sealed class CaptureHandler(string response) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
