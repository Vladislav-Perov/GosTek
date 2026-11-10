/**
 * ==============================================================================
 * Cloudflare Worker: GosTek AI Proxy & Smart Document Assistant
 * НАЗНАЧЕНИЕ: Шлюз каскадного вызова LLM (Gemini -> Gemini Fallback -> Groq Llama)
 * БЕЗОПАСНОСТЬ: Строго запрещено логировать тексты пользователя и ответы.
 * ==============================================================================
 */

export default {
  async fetch(request, env, ctx) {
    // Поддержка CORS preflight
    if (request.method === "OPTIONS") {
      return new Response(null, {
        status: 204,
        headers: {
          "Access-Control-Allow-Origin": "*",
          "Access-Control-Allow-Methods": "POST, OPTIONS",
          "Access-Control-Allow-Headers": "Content-Type, Authorization",
        },
      });
    }

    if (request.method !== "POST") {
      return jsonResponse({ success: false, error: "Only POST method allowed" }, 405);
    }

    let payload;
    try {
      payload = await request.json();
    } catch {
      return jsonResponse({ success: false, error: "Invalid JSON body" }, 400);
    }

    const startTime = Date.now();

    // Разбор входных данных (поддержка нового структурированного формата и обратная совместимость)
    const userText = payload.userText || payload.prompt || "";
    const templateName = payload.templateName || "Официальный документ";
    const fieldsContext = payload.fieldsContext || "";
    const memory = Array.isArray(payload.memory) ? payload.memory : [];
    const targetField = payload.targetField || null;

    // Сборка системного промпта
    const systemPrompt = buildSystemPrompt({
      templateName,
      fieldsContext,
      memory,
      targetField,
    });

    // Каскад моделей (Fallback)
    // Попытка 1: Gemini 2.5 Flash Lite (или 1.5 Flash)
    // Попытка 2: Gemini 2.0 Flash
    // Попытка 3: Groq Llama 3.1 8B Instant
    const cascade = [
      {
        provider: "gemini",
        model: "gemini-2.5-flash-lite",
        fallbackModel: "gemini-1.5-flash",
      },
      {
        provider: "gemini",
        model: "gemini-2.0-flash",
      },
      {
        provider: "groq",
        model: "llama-3.1-8b-instant",
      },
    ];

    let lastError = null;

    for (const step of cascade) {
      const stepStart = Date.now();
      try {
        let rawAnswer = null;

        if (step.provider === "gemini") {
          rawAnswer = await callGemini(env, step.model, systemPrompt, userText);
        } else if (step.provider === "groq") {
          rawAnswer = await callGroq(env, step.model, systemPrompt, userText);
        }

        const durationMs = Date.now() - stepStart;
        // ⚠️ Безопасность: логируем ТОЛЬКО метаданные (модель, время, статус), никакого текста!
        console.log(`[AI Proxy] Provider: ${step.provider}, Model: ${step.model}, Duration: ${durationMs}ms, Status: 200 OK`);

        if (rawAnswer) {
          const parsed = parseModelOutput(rawAnswer, memory);
          return jsonResponse({
            success: true,
            data: {
              fields: parsed.fields || {},
              question: parsed.question || null,
              status: parsed.question ? "need_info" : "success",
            },
            memory_ops: parsed.memory_ops || [],
            // Для обратной совместимости со старыми клиентами GosTek
            text: JSON.stringify({
              fields: parsed.fields || {},
              question: parsed.question || null,
            }),
            model: step.model,
            duration_ms: Date.now() - startTime,
          });
        }
      } catch (err) {
        const durationMs = Date.now() - stepStart;
        console.warn(`[AI Proxy Fallback] Provider: ${step.provider}, Model: ${step.model}, Duration: ${durationMs}ms, Error: ${err.message}`);
        lastError = err;
      }
    }

    console.error(`[AI Proxy Failure] All cascade attempts exhausted in ${Date.now() - startTime}ms`);
    return jsonResponse(
      {
        success: false,
        error: "Сервис ИИ временно недоступен. Все резервные модели исчерпаны.",
        details: lastError ? lastError.message : "Cascade exhausted",
      },
      503
    );
  },
};

/**
 * Формирует строгий системный промпт для роли секретаря-делопроизводителя.
 */
function buildSystemPrompt({ templateName, fieldsContext, memory, targetField }) {
  const memoryBlock = memory.length > 0
    ? `\nИзвестные факты из контекста диалога (память):\n${memory.map((m) => `- ${m}`).join("\n")}`
    : "";

  const targetBlock = targetField
    ? `\nФОКУСИРОВКА: В первую очередь пользователь просит обработать/заполнить поле "${targetField}". Сосредоточься на качественном формировании значения именно для этого поля.`
    : "";

  return `Ты — строгий секретарь-делопроизводитель, эксперт по официально-деловому стилю (ГОСТ Р 7.0.97-2016).
Твоя задача — извлекать и грамотно формулировать данные для документа "${templateName}".

${fieldsContext}${memoryBlock}${targetBlock}

ПРАВИЛА ОБРАБОТКИ:
1. (Расширение смысла без домыслов): Если причина или описание ситуации короткое (1-3 слова), разверни её в грамотный официальный текст из 2-3 предложений.
   НО СТРОГО: НЕ ДОБАВЛЯЙ обязательств ("обязуюсь впредь...", "предоставлю справку..."), если пользователь прямо о них не писал! Описывай только сам факт и уважительную причину.
2. (Нумерованные плейсхолдеры): В тексте пользователя могут присутствовать защитные нумерованные плейсхолдеры: [ФИО_1], [ФИО_2], [ПАСПОРТ_1], [ТЕЛЕФОН_1], [СЧЕТ_1], [УНП_1].
   Ты ОБЯЗАН возвращать их ровно в том виде, в котором получил, не заменяя на пустоту и не меняя цифры индекса.
3. (Уточняющие вопросы): Если данные слишком размыты ("плохо", "уехал") или не хватает ключевых реквизитов для пустых полей, задай вежливый уточняющий вопрос в поле "question".
4. (Память): Если пользователь сообщает важный контекстный факт (например, "документ на брата", "отпуск за 2025 год"), предложи добавить его в массив memory_ops: [{"action": "add", "key": "краткий_ключ", "value": "факт"}].

ФОРМАТ ОТВЕТА (СТРОГО JSON без markdown и без пояснений):
{
  "fields": {
    "Название поля": "Значение"
  },
  "question": "Текст вопроса если данных не хватает, иначе null",
  "memory_ops": [
    { "action": "add", "key": "тема/фактор", "value": "краткое описание" }
  ]
}`;
}

/**
 * Вызов Google Gemini API
 */
async function callGemini(env, model, systemPrompt, userText) {
  const apiKey = env.GEMINI_API_KEY;
  if (!apiKey) throw new Error("GEMINI_API_KEY is not set");

  const url = `https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent?key=${apiKey}`;

  const response = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      contents: [
        {
          role: "user",
          parts: [{ text: `${systemPrompt}\n\nТекст пользователя:\n${userText}` }],
        },
      ],
      generationConfig: {
        temperature: 0.2,
        responseMimeType: "application/json",
      },
    }),
  });

  if (!response.ok) {
    const errText = await response.text().catch(() => "");
    throw new Error(`Gemini HTTP ${response.status}: ${errText.slice(0, 150)}`);
  }

  const data = await response.json();
  const text = data?.candidates?.[0]?.content?.parts?.[0]?.text;
  if (!text) throw new Error("Gemini returned empty candidate text");
  return text;
}

/**
 * Вызов Groq Cloud API (Llama 3.1 8B)
 */
async function callGroq(env, model, systemPrompt, userText) {
  const apiKey = env.GROQ_API_KEY;
  if (!apiKey) throw new Error("GROQ_API_KEY is not set");

  const url = "https://api.groq.com/openai/v1/chat/completions";

  const response = await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${apiKey}`,
    },
    body: JSON.stringify({
      model: model || "llama-3.1-8b-instant",
      messages: [
        { role: "system", content: systemPrompt },
        { role: "user", content: userText },
      ],
      temperature: 0.2,
      response_format: { type: "json_object" },
    }),
  });

  if (!response.ok) {
    const errText = await response.text().catch(() => "");
    throw new Error(`Groq HTTP ${response.status}: ${errText.slice(0, 150)}`);
  }

  const data = await response.json();
  const text = data?.choices?.[0]?.message?.content;
  if (!text) throw new Error("Groq returned empty choice content");
  return text;
}

/**
 * Парсер JSON-вывода LLM с защитой от артефактов и markdown.
 */
function parseModelOutput(rawText, existingMemory) {
  let cleaned = rawText.trim();
  if (cleaned.startsWith("```json")) cleaned = cleaned.slice(7);
  else if (cleaned.startsWith("```")) cleaned = cleaned.slice(3);
  if (cleaned.endsWith("```")) cleaned = cleaned.slice(0, -3);
  cleaned = cleaned.trim();

  const firstBrace = cleaned.indexOf("{");
  const lastBrace = cleaned.lastIndexOf("}");
  if (firstBrace >= 0 && lastBrace > firstBrace) {
    cleaned = cleaned.substring(firstBrace, lastBrace + 1);
  }

  try {
    const parsed = JSON.parse(cleaned);
    const fields = parsed.fields && typeof parsed.fields === "object" ? parsed.fields : {};
    let question = parsed.question;
    if (typeof question === "string") {
      question = question.trim();
      if (question.toLowerCase() === "null" || question.length === 0) question = null;
    } else {
      question = null;
    }

    const memoryOps = Array.isArray(parsed.memory_ops) ? parsed.memory_ops : [];

    return {
      fields,
      question,
      memory_ops: memoryOps,
    };
  } catch {
    return {
      fields: {},
      question: null,
      memory_ops: [],
    };
  }
}

function jsonResponse(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Access-Control-Allow-Origin": "*",
    },
  });
}
