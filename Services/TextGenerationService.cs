using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GosTek.Models;

namespace GosTek.Services;

public sealed record TextGenerationResult(string? Text, string? Question);

public sealed class TextGenerationService(HttpClient client, Uri? endpoint)
{
    public const int MaxMessageLength = 4000;
    public const int MaxPreviousTextLength = 8000;

    public static TextGenerationService Instance { get; } = new(
        CreateClient(), ReadEndpoint());

    public async Task<TextGenerationResult> GenerateAsync(
        string message, TextGenerationKind kind, string? previousText, CancellationToken cancellationToken = default)
    {
        if (endpoint is null || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new AiAssistantException("Сервер генерации пока не настроен. Поле можно заполнить вручную.");

        if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength ||
            previousText?.Length > MaxPreviousTextLength)
            throw new AiAssistantException("Сократите запрос и попробуйте ещё раз.");

        var safeMessage = PrivacyMasker.Mask(message, out var replacements, out var suspicious, numbered: true);
        string? safePrevious = null;
        if (!string.IsNullOrWhiteSpace(previousText))
        {
            safePrevious = PrivacyMasker.Mask(previousText, out var previousReplacements, out var previousSuspicious,
                numbered: true, placeholderOffset: replacements.Count);
            suspicious.AddRange(previousSuspicious);
            foreach (var (key, value) in previousReplacements)
                replacements.TryAdd(key, value);
        }

        if (suspicious.Count > 0 && !PrivacyMasker.MaskAmbiguousSilent)
            throw new AiAssistantException("Уберите имена из запроса или включите автоматическую маскировку в настройках.");
        if (safeMessage.Length > MaxMessageLength || safePrevious?.Length > MaxPreviousTextLength)
            throw new AiAssistantException("Сократите запрос и попробуйте ещё раз.");

        var request = new GenerationRequest(
            safeMessage, kind == TextGenerationKind.Explanation ? "explanation" : "document_text", safePrevious);

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(request, TextGenerationJsonContext.Default.GenerationRequest);
            using var content = new ByteArrayContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await client.PostAsync(endpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new AiAssistantException(DescribeHttpFailure(response));

            var result = await response.Content.ReadFromJsonAsync(TextGenerationJsonContext.Default.TextGenerationResult, cancellationToken);
            var text = result?.Text?.Trim();
            var question = result?.Question?.Trim();
            if (string.IsNullOrEmpty(text) == string.IsNullOrEmpty(question) ||
                text?.Length > MaxPreviousTextLength || question?.Length > 500)
                throw new AiAssistantException("Сервис вернул некорректный ответ. Попробуйте ещё раз.");

            return new TextGenerationResult(Restore(text, replacements), Restore(question, replacements));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiAssistantException("Сервис не ответил вовремя. Попробуйте ещё раз.");
        }
        catch (HttpRequestException)
        {
            throw new AiAssistantException("Не удалось связаться с сервером генерации. Проверьте подключение.");
        }
        catch (JsonException)
        {
            throw new AiAssistantException("Сервис вернул некорректный ответ. Попробуйте ещё раз.");
        }
    }

    private static string DescribeHttpFailure(HttpResponseMessage response)
    {
        var status = response.StatusCode;
        var message = status switch
        {
            HttpStatusCode.BadRequest => "Сервер отклонил запрос. Попробуйте короткое сообщение.",
            HttpStatusCode.Forbidden => "Доступ к серверу генерации отклонён.",
            HttpStatusCode.RequestEntityTooLarge => "Запрос слишком большой. Сократите текст.",
            HttpStatusCode.TooManyRequests => "Слишком много запросов. Повторите попытку позже.",
            HttpStatusCode.GatewayTimeout => "Сервер не дождался ответа модели. Попробуйте ещё раз.",
            _ => "Генерация текста сейчас недоступна. Поле можно заполнить вручную."
        };

        var ray = response.Headers.TryGetValues("CF-Ray", out var values) ? values.FirstOrDefault() : null;
        return ray is not null && Regex.IsMatch(ray, @"\A[0-9a-fA-F]{16}-[A-Za-z]{3}\z")
            ? $"{message} HTTP {(int)status}, запрос {ray}."
            : $"{message} HTTP {(int)status}.";
    }

    private static string? Restore(string? value, Dictionary<string, string> replacements)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        return Regex.Replace(value, @"\[[\p{L}]+_\d+\]", match =>
            replacements.TryGetValue(match.Value, out var original) ? original : match.Value);
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = ConnectWithIpv4PreferenceAsync
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    private static async ValueTask<Stream> ConnectWithIpv4PreferenceAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        using var ipv4Timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ipv4Timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            return await ConnectAsync(context.DnsEndPoint, AddressFamily.InterNetwork, ipv4Timeout.Token)
                .ConfigureAwait(false);
        }
        catch (SocketException) when (!cancellationToken.IsCancellationRequested)
        {
            // The host may only be reachable over IPv6.
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Reserve the remaining request time for IPv6.
        }

        return await ConnectAsync(context.DnsEndPoint, AddressFamily.InterNetworkV6, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endpoint, AddressFamily family, CancellationToken cancellationToken)
    {
        var socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new DnsEndPoint(endpoint.Host, endpoint.Port, family), cancellationToken)
                .ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static Uri? ReadEndpoint()
    {
        var configured = typeof(TextGenerationService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "GosTekTextGenerationEndpoint")?.Value;
        return Uri.TryCreate(configured, UriKind.Absolute, out var endpoint) ? endpoint : null;
    }

}

internal sealed record GenerationRequest(
    string Message,
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PreviousText);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GenerationRequest))]
[JsonSerializable(typeof(TextGenerationResult))]
internal partial class TextGenerationJsonContext : JsonSerializerContext;
