/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Клиентский сервис интеграции с облачным прокси ИИ (Cloudflare Worker)
 * СОДЕРЖИМОЕ: AiAssistantService, AiAssistantResponse, AiProxyRequest, MemoryOp, AiAssistantException
 * ЗАВИСИМОСТИ: System.Net.Http, System.Text.Json, GosTek.Services.PrivacyMasker, GosTek.ProfileStorage, GosTek.Models.AiMemory
 * ==============================================================================
 */
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GosTek;
using GosTek.Models;

namespace GosTek.Services;

/// <summary>
/// Операция изменения структурированной памяти (добавление или удаление факта).
/// </summary>
public class MemoryOp
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "add";

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

/// <summary>
/// Результат обработки запроса ассистентом ИИ: извлеченные поля, возможный уточняющий вопрос,
/// статус диалога и операции обновления памяти.
/// </summary>
public record AiAssistantResponse(
    Dictionary<string, string> Fields,
    string? Question,
    string RawText,
    string Status = "success",
    List<MemoryOp>? MemoryOps = null);

/// <summary>
/// Модель запроса к облачному прокси Cloudflare.
/// </summary>
public class AiProxyRequest
{
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    [JsonPropertyName("userText")]
    public string? UserText { get; set; }

    [JsonPropertyName("templateName")]
    public string? TemplateName { get; set; }

    [JsonPropertyName("fieldsContext")]
    public string? FieldsContext { get; set; }

    [JsonPropertyName("memory")]
    public List<string>? Memory { get; set; }

    [JsonPropertyName("targetField")]
    public string? TargetField { get; set; }
}

/// <summary>
/// Вложенный объект данных ответа Cloudflare Worker.
/// </summary>
public class AiServerData
{
    [JsonPropertyName("fields")]
    public Dictionary<string, string>? Fields { get; set; }

    [JsonPropertyName("question")]
    public string? Question { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

/// <summary>
/// Модель ответа от облачного прокси Cloudflare (поддерживает строгий JSON и fallback-text).
/// </summary>
public class AiProxyResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("data")]
    public AiServerData? Data { get; set; }

    [JsonPropertyName("memory_ops")]
    public List<MemoryOp>? MemoryOps { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Внутренняя структура распарсенного JSON-ответа от модели.
/// </summary>
public class AiParsedPayload
{
    [JsonPropertyName("fields")]
    public Dictionary<string, string>? Fields { get; set; }

    [JsonPropertyName("question")]
    public string? Question { get; set; }

    [JsonPropertyName("memory_ops")]
    public List<MemoryOp>? MemoryOps { get; set; }
}

/// <summary>
/// Пользовательское исключение сервиса ИИ с понятным описанием для отображения в UI.
/// </summary>
public class AiAssistantException : Exception
{
    public AiAssistantException(string message) : base(message) { }
    public AiAssistantException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Исключение нарушения локальной политики безопасности и конфиденциальности данных при обращении к ИИ.
/// </summary>
public class AiAssistantSecurityException : AiAssistantException
{
    public AiAssistantSecurityException(string message) : base(message) { }
}

/// <summary>
/// Сервис облачного ИИ-помощника для разбора текста и автозаполнения полей шаблонов документов.
/// Поддерживает повторные попытки (retry) при временной перегрузке (HTTP 503),
/// нумерованные плейсхолдеры и операции над контекстной памятью.
/// </summary>
public class AiAssistantService
{
    public const string DefaultEndpoint = "https://gostek-proxy.d13225135.workers.dev";
    public const int MaxRetryAttempts = 3;
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static AiAssistantService Instance { get; set; } = new();

    public AiAssistantService(HttpClient? httpClient = null, string endpoint = DefaultEndpoint)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _endpoint = endpoint;
    }

    private static readonly Regex RawPassportRegex = new(
        @"(?<!\[)\b[A-Za-zА-Яа-яЁё]{2}\s*[-]?\s*\d{7}\b(?!\])",
        RegexOptions.Compiled);

    private static readonly Regex RawPhoneRegex = new(
        @"(?<!\[)(?:\+375|80)[\s\-]?(?:\(\d{2}\)|\d{2})?[\s\-]?\d{3}[\s\-]?\d{2}[\s\-]?\d{2}\b(?!\])",
        RegexOptions.Compiled);

    private static readonly Regex RawIbanRegex = new(
        @"(?<!\[)\bBY\d{2}[\s]?[A-Za-z0-9]{4}(?:[\s]?[A-Za-z0-9]{4}){5}\b(?!\])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Шлюз исходящих запросов к ИИ (Outbound Gateway).
    /// Обеспечивает сквозное маскирование пользовательского текста, всех полей формы,
    /// названия шаблона, фактов памяти и целевого поля.
    /// Выполняет строгую предпроверку (AssertPayloadSafe) перед сериализацией и отправкой в сеть.
    /// </summary>
    public static (AiProxyRequest SafeRequest, Dictionary<string, string> AllPlaceholders) SanitizeOutgoingPayload(
        string userText,
        List<string> fieldNames,
        Dictionary<string, string>? currentFormFields = null,
        Dictionary<string, string>? extractedPlaceholders = null,
        string? targetField = null,
        string? templateName = null,
        List<string>? memory = null,
        ProfileData? profile = null,
        DateTime? today = null)
    {
        var allPlaceholders = extractedPlaceholders != null
            ? new Dictionary<string, string>(extractedPlaceholders, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. Санитизируем UserText
        var safeUserText = PrivacyMasker.Mask(
            userText,
            out var uExtracted,
            out _,
            profile: profile,
            numbered: true);

        foreach (var (k, v) in uExtracted)
            allPlaceholders[k] = v;

        // 2. Санитизируем поля текущей формы (currentFormFields)
        var safeFormFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (currentFormFields != null)
        {
            foreach (var (fName, fVal) in currentFormFields)
            {
                if (string.IsNullOrWhiteSpace(fVal))
                {
                    safeFormFields[fName] = string.Empty;
                    continue;
                }

                var maskedVal = PrivacyMasker.Mask(
                    fVal,
                    out var fExtracted,
                    out _,
                    profile: profile,
                    numbered: true);

                foreach (var (k, v) in fExtracted)
                    allPlaceholders[k] = v;

                safeFormFields[fName] = maskedVal;
            }
        }

        // 3. Санитизируем название шаблона и целевое поле
        var safeTemplateName = PrivacyMasker.Mask(
            templateName ?? "Документ",
            out var tExtracted,
            out _,
            profile: profile,
            numbered: false);

        foreach (var (k, v) in tExtracted)
            allPlaceholders[k] = v;

        string? safeTargetField = null;
        if (!string.IsNullOrWhiteSpace(targetField))
        {
            safeTargetField = PrivacyMasker.Mask(
                targetField,
                out var tgExtracted,
                out _,
                profile: profile,
                numbered: false);

            foreach (var (k, v) in tgExtracted)
                allPlaceholders[k] = v;
        }

        // 4. Санитизируем факты памяти
        List<string>? safeMemory = null;
        if (memory != null && memory.Count > 0)
        {
            safeMemory = new List<string>(memory.Count);
            foreach (var m in memory)
            {
                var safeM = PrivacyMasker.Mask(
                    m,
                    out var mExtracted,
                    out _,
                    profile: profile,
                    numbered: false);

                foreach (var (k, v) in mExtracted)
                    allPlaceholders[k] = v;

                safeMemory.Add(safeM);
            }
        }

        // 5. Формируем безопасный системный промпт и контекст полей
        var safePrompt = BuildPrompt(
            safeUserText,
            fieldNames,
            safeFormFields,
            today: today,
            targetField: safeTargetField,
            memory: safeMemory);

        var request = new AiProxyRequest
        {
            Prompt = safePrompt,
            UserText = safeUserText,
            TemplateName = safeTemplateName,
            FieldsContext = FormatFieldsContext(safeFormFields, fieldNames),
            Memory = safeMemory,
            TargetField = safeTargetField
        };

        // 6. Строгая валидация перед отправкой
        AssertPayloadSafe(request, profile);

        return (request, allPlaceholders);
    }

    /// <summary>
    /// Проверяет, что подготовленный запрос не содержит открытых персональных данных.
    /// В случае обнаружения PII бросает AiAssistantSecurityException, предотвращая утечку.
    /// </summary>
    public static void AssertPayloadSafe(AiProxyRequest request, ProfileData? profile)
    {
        var parts = new List<string?>
        {
            request.Prompt,
            request.UserText,
            request.TemplateName,
            request.FieldsContext,
            request.TargetField
        };

        if (request.Memory != null)
            parts.AddRange(request.Memory);

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            // 1. Проверка на сырой паспорт (исключая плейсхолдеры)
            if (RawPassportRegex.IsMatch(part))
                throw new AiAssistantSecurityException("Обнаружена попытка отправки незамаскированных паспортных данных.");

            // 2. Проверка на телефон
            if (RawPhoneRegex.IsMatch(part))
                throw new AiAssistantSecurityException("Обнаружена попытка отправки незамаскированного номера телефона.");

            // 3. Проверка на IBAN
            if (RawIbanRegex.IsMatch(part))
                throw new AiAssistantSecurityException("Обнаружена попытка отправки незамаскированного номера банковского счёта.");

            // 4. Проверка на открытые данные профиля пользователя
            if (profile != null)
            {
                if (!string.IsNullOrWhiteSpace(profile.Passport) &&
                    profile.Passport.Length >= 7 &&
                    part.Contains(profile.Passport, StringComparison.OrdinalIgnoreCase))
                {
                    throw new AiAssistantSecurityException("Обнаружена попытка отправки паспорта пользователя из профиля в открытом виде.");
                }

                if (!string.IsNullOrWhiteSpace(profile.Phone) &&
                    profile.Phone.Length >= 9 &&
                    part.Contains(profile.Phone.Replace(" ", "").Replace("-", ""), StringComparison.OrdinalIgnoreCase))
                {
                    throw new AiAssistantSecurityException("Обнаружена попытка отправки телефона пользователя из профиля в открытом виде.");
                }

                if (!string.IsNullOrWhiteSpace(profile.Account) &&
                    profile.Account.Length >= 10 &&
                    part.Contains(profile.Account.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                {
                    throw new AiAssistantSecurityException("Обнаружена попытка отправки счёта пользователя из профиля в открытом виде.");
                }

                if (!string.IsNullOrWhiteSpace(profile.FullName))
                {
                    var words = profile.FullName
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => w.Length >= 4)
                        .ToList();

                    if (words.Count >= 2 && words.All(w => part.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new AiAssistantSecurityException("Обнаружена попытка отправки ФИО пользователя из профиля в открытом виде.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Отправляет безопасный (замаскированный) текст, список полей, состояние формы и память в ИИ.
    /// Восстанавливает нумерованные персональные данные и обновляет локальную память при необходимости.
    /// </summary>
    public async Task<AiAssistantResponse> ProcessRequestAsync(
        string maskedUserText,
        List<string> fieldNames,
        Dictionary<string, string>? currentFormFields = null,
        Dictionary<string, string>? extractedPlaceholders = null,
        string? targetField = null,
        string? templateName = null,
        List<string>? memory = null,
        CancellationToken cancellationToken = default)
    {
        ProfileData? profile = null;
        try { profile = await ProfileStorage.LoadAsync(); } catch { }

        var activeMemory = memory ?? AiMemory.Instance.FormatForPrompt();

        var (requestBody, allPlaceholders) = SanitizeOutgoingPayload(
            maskedUserText,
            fieldNames,
            currentFormFields: currentFormFields,
            extractedPlaceholders: extractedPlaceholders,
            targetField: targetField,
            templateName: templateName,
            memory: activeMemory,
            profile: profile);

        for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
                {
                    Content = JsonContent.Create(requestBody)
                };

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                // Обработка 503 (перегрузка сервера): до 3 попыток с задержкой 2 сек
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    if (attempt < MaxRetryAttempts)
                    {
                        await Task.Delay(RetryDelay, cancellationToken);
                        continue;
                    }

                    throw new AiAssistantException("Сервер ИИ временно перегружен (ошибка 503). Пожалуйста, повторите запрос через несколько секунд.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new AiAssistantException($"Ошибка сервиса ИИ: {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var parsed = ParseServerResponse(rawJson);

                // Применяем операции памяти с предварительным обязательным маскированием фактов
                if (parsed.MemoryOps != null && parsed.MemoryOps.Count > 0)
                {
                    foreach (var op in parsed.MemoryOps)
                    {
                        if (string.Equals(op.Action, "add", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(op.Value))
                        {
                            var safeFact = PrivacyMasker.Mask(op.Value, out _, out _, profile: profile, numbered: false);
                            AiMemory.Instance.AddFact(op.Key, safeFact);
                        }
                        else if (string.Equals(op.Action, "remove", StringComparison.OrdinalIgnoreCase))
                        {
                            AiMemory.Instance.RemoveFact(op.Key);
                        }
                    }
                }

                var restoredFields = await RestorePlaceholdersAsync(parsed.Fields, allPlaceholders, profile);
                return parsed with { Fields = restoredFields };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new AiAssistantException("Запрос к сервису ИИ был отменен.");
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AiAssistantException("Истекло время ожидания ответа от сервиса ИИ (превышен лимит 20 сек).", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new AiAssistantException("Не удалось связаться с сервером ИИ. Проверьте подключение к сети.", ex);
            }
        }

        throw new AiAssistantException("Сервис ИИ временно перегружен или недоступен.");
    }

    /// <summary>
    /// Перегрузка для совместимости со старыми вызовами с CancellationToken без словарей.
    /// </summary>
    public Task<AiAssistantResponse> ProcessRequestAsync(
        string maskedUserText,
        List<string> fieldNames,
        CancellationToken cancellationToken)
    {
        return ProcessRequestAsync(maskedUserText, fieldNames, currentFormFields: null, extractedPlaceholders: null, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Восстанавливает персональные данные вместо масок [ФИО_1], [ФИО], [ПАСПОРТ_1], [ТЕЛЕФОН_1] и т.д.
    /// из словаря извлеченных значений и локального профиля пользователя.
    /// </summary>
    public static async Task<Dictionary<string, string>> RestorePlaceholdersAsync(
        Dictionary<string, string>? fields,
        Dictionary<string, string>? extractedPlaceholders = null,
        ProfileData? profileOverride = null)
    {
        if (fields == null || fields.Count == 0)
            return fields ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ProfileData? profile = profileOverride;
        if (profile == null)
        {
            try
            {
                profile = await ProfileStorage.LoadAsync();
            }
            catch
            {
                profile = new ProfileData();
            }
        }

        var restored = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in fields)
        {
            var val = kvp.Value;
            if (string.IsNullOrWhiteSpace(val))
                continue;

            // 1. Сначала проверяем точные ключи из словаря извлеченных плейсхолдеров
            if (extractedPlaceholders != null && extractedPlaceholders.Count > 0)
            {
                foreach (var (placeholder, original) in extractedPlaceholders)
                {
                    if (val.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
                    {
                        val = val.Replace(placeholder, original, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }

            // 2. Обрабатываем любые оставшиеся нумерованные или ненанумерованные плейсхолдеры
            // Регулярное выражение для [ФИО], [ФИО_1], [ПАСПОРТ_1] и т.п.
            val = Regex.Replace(val, @"\[(ФИО|ПАСПОРТ|ТЕЛЕФОН|СЧЕТ|УНП)(?:_\d+)?\]", match =>
            {
                var fullPlaceholder = match.Value;
                var baseCategory = match.Groups[1].Value.ToUpperInvariant();

                // Проверяем словарь
                if (extractedPlaceholders != null)
                {
                    if (extractedPlaceholders.TryGetValue(fullPlaceholder, out var ext) && !string.IsNullOrWhiteSpace(ext))
                        return ext;
                    if (extractedPlaceholders.TryGetValue($"[{baseCategory}]", out var extBase) && !string.IsNullOrWhiteSpace(extBase))
                        return extBase;
                }

                // Фоллбэк на данные профиля
                return baseCategory switch
                {
                    "ФИО" when !string.IsNullOrWhiteSpace(profile?.FullName) => profile.FullName,
                    "ПАСПОРТ" when !string.IsNullOrWhiteSpace(profile?.Passport) => profile.Passport,
                    "ТЕЛЕФОН" when !string.IsNullOrWhiteSpace(profile?.Phone) => profile.Phone,
                    "СЧЕТ" when !string.IsNullOrWhiteSpace(profile?.Account) => profile.Account,
                    "УНП" when !string.IsNullOrWhiteSpace(profile?.Unp) => profile.Unp,
                    _ => fullPlaceholder
                };
            }, RegexOptions.IgnoreCase);

            restored[kvp.Key] = val;
        }

        return restored;
    }

    /// <summary>
    /// Форматирует текстовый контекст текущих полей формы.
    /// </summary>
    public static string FormatFieldsContext(Dictionary<string, string>? currentFormFields, List<string> fieldNames)
    {
        if (currentFormFields != null && currentFormFields.Count > 0)
        {
            var items = currentFormFields.Select(kv =>
                string.IsNullOrWhiteSpace(kv.Value)
                    ? $"{kv.Key}: [пусто]"
                    : $"{kv.Key}: {kv.Value}");
            return $"Текущее состояние полей в форме: [{string.Join(", ", items)}]";
        }
        else if (fieldNames.Count > 0)
        {
            var items = fieldNames.Select(f => $"{f}: [пусто]");
            return $"Текущее состояние полей в форме: [{string.Join(", ", items)}]";
        }

        return "Текущее состояние полей в форме: [поля не указаны]";
    }

    /// <summary>
    /// Формирует строгий системный промпт для роли секретаря-делопроизводителя (ГОСТ Р 7.0.97-2016),
    /// с расширением формулировок без домыслов обязательств, контекстом формы, памятью и целевым полем.
    /// </summary>
    public static string BuildPrompt(
        string maskedUserText,
        List<string> fieldNames,
        Dictionary<string, string>? currentFormFields = null,
        DateTime? today = null,
        string? targetField = null,
        List<string>? memory = null)
    {
        var now = today ?? DateTime.Now;
        var currentDate = now.ToString("dd.MM.yyyy");
        var yesterdayDate = now.AddDays(-1).ToString("dd.MM.yyyy");

        string formStateText = FormatFieldsContext(currentFormFields, fieldNames);

        var fieldsList = fieldNames.Count > 0
            ? string.Join("\n", fieldNames.Select(f => $"- {f}"))
            : "(поля шаблона не указаны)";

        string memoryText = memory != null && memory.Count > 0
            ? $"\nИзвестные факты из памяти:\n{string.Join("\n", memory.Select(m => $"- {m}"))}"
            : "";

        string targetText = !string.IsNullOrWhiteSpace(targetField)
            ? $"\nФОКУСИРОВКА: Пользователь просит заполнить/расширить именно целевое поле: \"{targetField}\"."
            : "";

        return $@"Ты — строгий секретарь-делопроизводитель, эксперт по официально-деловому стилю (ГОСТ Р 7.0.97-2016). Твоя задача — превращать бытовые фразы пользователя в грамотные тексты для документов.

{formStateText}{memoryText}{targetText}

ПРАВИЛО 1 (Расширение смысла): Если пользователь вводит короткую причину (1-3 слова), ты ОБЯЗАН развернуть её в официальную формулировку из 2-3 предложений, добавив стандартные канцелярские обороты.
Но НЕ ДОБАВЛЯЙ обязательств (""обязуюсь"", ""предоставлю справку""), если пользователь прямо о них не писал.
Пример 1: Пользователь пишет ""проспал"". Ты генерируешь: ""Опоздал на рабочее место в связи с непредвиденными личными обстоятельствами (проспал).""
Пример 2: Пользователь пишет ""болел"". Ты генерируешь: ""Отсутствовал на рабочем месте по причине временной нетрудоспособности.""
Пример 3: Пользователь пишет ""отпуск"". Ты генерируешь: ""Прошу предоставить мне ежегодный оплачиваемый отпуск согласно утвержденному графику отпусков.""

ПРАВИЛО 2 (Уточняющие вопросы): Если фраза пользователя слишком размыта для официального документа (например, ""плохо себя чувствовал"" или ""уехал""), ты не выдумывай факты. Вместо этого верни в поле `question` уточняющий вопрос.
Пример: Пользователь пишет ""плохо"". Ты возвращаешь `question`: ""Уточните, пожалуйста, обращались ли вы к врачу, и есть ли у вас справка?""

ПРАВИЛО 3 (Даты): Сегодня {currentDate}, вчера было {yesterdayDate}. Если пользователь говорит ""сегодня"" или ""вчера"", переводи это в формат ДД.ММ.ГГГГ.

ПРАВИЛО 4 (Плейсхолдеры): Возвращай плейсхолдеры и нумерованные плейсхолдеры (например, [ФИО], [ФИО_1], [ФИО_2], [ПАСПОРТ], [ПАСПОРТ_1], [ТЕЛЕФОН_1], [СЧЕТ_1], [УНП_1]) ровно в том виде, в котором получил, не заменяй их пустотой.

ПРАВИЛО 5 (Контекст формы): Если поле УЖЕ имеет значение, не проси пользователя его вводить и используй это значение. Задавай вопросы ТОЛЬКО по пустым полям.

Верни СТРОГО JSON без markdown-блоков (```json) и без пояснений:
{{
  ""fields"": {{
    ""Название поля"": ""Значение""
  }},
  ""question"": ""Текст уточняющего вопроса, если данных не хватает, иначе null"",
  ""memory_ops"": [
    {{ ""action"": ""add"", ""key"": ""тема"", ""value"": ""факт"" }}
  ]
}}

Список всех полей шаблона:
{fieldsList}

Описание ситуации пользователем:
{maskedUserText}";
    }

    /// <summary>
    /// Перегрузка для совместимости с тестами без параметров памяти и фокуса.
    /// </summary>
    public static string BuildPrompt(
        string maskedUserText,
        List<string> fieldNames,
        DateTime? today)
    {
        return BuildPrompt(maskedUserText, fieldNames, currentFormFields: null, today: today);
    }

    /// <summary>
    /// Очищает строку от markdown-тегов и извлекает чистый JSON-объект.
    /// </summary>
    public static string ExtractJson(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return "{}";

        var text = rawText.Trim();

        // Срезаем ```json и ```
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            text = text[7..];
        else if (text.StartsWith("```"))
            text = text[3..];

        if (text.EndsWith("```"))
            text = text[..^3];

        text = text.Trim();

        int firstBrace = text.IndexOf('{');
        int lastBrace = text.LastIndexOf('}');

        if (firstBrace >= 0 && lastBrace > firstBrace)
            return text.Substring(firstBrace, lastBrace - firstBrace + 1);

        return text;
    }

    /// <summary>
    /// Парсит структурированный ответ от сервера (Cloudflare Worker) либо fallback-ответ.
    /// </summary>
    public static AiAssistantResponse ParseServerResponse(string rawJson)
    {
        var json = ExtractJson(rawJson);

        try
        {
            var serverResp = JsonSerializer.Deserialize<AiProxyResponse>(json, JsonOptions);

            // Если пришел новый формат { success: true, data: { fields: {...}, question: ... } }
            if (serverResp?.Data != null)
            {
                var fields = serverResp.Data.Fields ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var rawQuestion = serverResp.Data.Question?.Trim();
                var question = string.IsNullOrWhiteSpace(rawQuestion) || string.Equals(rawQuestion, "null", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : rawQuestion;

                var status = serverResp.Data.Status ?? (question != null ? "need_info" : "success");
                return new AiAssistantResponse(fields, question, rawJson, status, serverResp.MemoryOps);
            }

            // Если пришел ответ со свойством text { text: "..." }
            if (!string.IsNullOrWhiteSpace(serverResp?.Text))
            {
                return ParseAiResponse(serverResp.Text);
            }

            // Если сам json — это { "fields": { ... }, "question": ... }
            return ParseAiResponse(json);
        }
        catch
        {
            return ParseAiResponse(rawJson);
        }
    }

    /// <summary>
    /// Парсит очищенный текст ответа ИИ в структуру AiAssistantResponse.
    /// </summary>
    public static AiAssistantResponse ParseAiResponse(string rawText)
    {
        var json = ExtractJson(rawText);

        try
        {
            var payload = JsonSerializer.Deserialize<AiParsedPayload>(json, JsonOptions);
            var fields = payload?.Fields ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var rawQuestion = payload?.Question?.Trim();
            var question = string.IsNullOrWhiteSpace(rawQuestion) || string.Equals(rawQuestion, "null", StringComparison.OrdinalIgnoreCase)
                ? null
                : rawQuestion;

            var status = question != null ? "need_info" : "success";
            return new AiAssistantResponse(fields, question, rawText, status, payload?.MemoryOps);
        }
        catch (JsonException ex)
        {
            throw new AiAssistantException("Не удалось разобрать структуру данных ответа ИИ.", ex);
        }
    }
}
