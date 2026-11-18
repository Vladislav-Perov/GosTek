const maxBodyBytes = 64 * 1024;
const groqUrl = "https://api.groq.com/openai/v1/chat/completions";

const outputSchema = {
  type: "object",
  additionalProperties: false,
  properties: {
    text: { type: ["string", "null"] },
    question: { type: ["string", "null"] },
  },
  required: ["text", "question"],
};

export default {
  async fetch(request, env) {
    if (new URL(request.url).pathname !== "/generate") return error(404, "not_found");
    if (request.method !== "POST") return error(405, "method_not_allowed", { Allow: "POST" });
    if (!env.GROQ_API_KEY || !env.GENERATION_LIMIT) return error(503, "configuration");

    const ip = request.headers.get("CF-Connecting-IP") || "local";
    const { success } = await env.GENERATION_LIMIT.limit({ key: ip });
    if (!success) return error(429, "rate_limit", { "Retry-After": "60" });

    let payload;
    try {
      payload = await readJson(request);
    } catch (cause) {
      return cause instanceof RangeError ? error(413, "too_large") : error(400, "invalid_json");
    }

    if (!isValidRequest(payload)) return error(400, "invalid_request");
    const requestId = crypto.randomUUID();
    const signal = AbortSignal.timeout(12000);
    try {
      const response = await fetch(groqUrl, {
        method: "POST",
        headers: { Authorization: "Bearer " + env.GROQ_API_KEY, "Content-Type": "application/json" },
        redirect: "manual",
        signal,
        body: JSON.stringify({
          model: "openai/gpt-oss-20b",
          reasoning_effort: "low",
          temperature: 0.2,
          max_completion_tokens: 1800,
          messages: [
            { role: "system", content: buildPrompt(payload.kind) },
            { role: "user", content: JSON.stringify({
              message: payload.message,
              previousText: payload.previousText ?? null,
            }) },
          ],
          response_format: {
            type: "json_schema",
            json_schema: { name: "document_text", strict: true, schema: outputSchema },
          },
        }),
      });
      if (!response.ok) {
        const upstream = await response.json().catch(() => null);
        const rawCode = upstream?.error?.code;
        const providerCode = [
          "model_permission_blocked_org", "model_permission_blocked_project",
          "country_territory_not_supported", "invalid_api_key", "rate_limit_exceeded",
        ].includes(rawCode) ? rawCode : undefined;
        console.warn(JSON.stringify({
          requestId, category: "provider_http", status: response.status,
          colo: request.cf?.colo, providerCode,
        }));
        return error(503, "provider_unavailable");
      }

      const providerResult = await response.json();
      const result = normalizeResult(JSON.parse(providerResult.choices?.[0]?.message?.content ?? "null"));
      if (!result) {
        console.warn(JSON.stringify({ requestId, category: "invalid_output" }));
        return error(502, "invalid_output");
      }

      return Response.json(result, { headers: { "Cache-Control": "no-store" } });
    } catch {
      console.warn(JSON.stringify({ requestId, category: signal.aborted ? "timeout" : "provider_error" }));
      return error(signal.aborted ? 504 : 502, signal.aborted ? "timeout" : "provider_error");
    }
  },
};

function error(status, code, headers = {}) {
  return Response.json({ code }, { status, headers: { ...headers, "Cache-Control": "no-store" } });
}

async function readJson(request) {
  if (Number(request.headers.get("Content-Length")) > maxBodyBytes) throw new RangeError();
  if (!request.body) throw new Error("Missing body");

  const reader = request.body.getReader();
  const decoder = new TextDecoder("utf-8", { fatal: true });
  let size = 0;
  let text = "";
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > maxBodyBytes) {
      await reader.cancel();
      throw new RangeError();
    }
    text += decoder.decode(value, { stream: true });
  }
  return JSON.parse(text + decoder.decode());
}

function isValidRequest(value) {
  return value && typeof value === "object" && !Array.isArray(value) &&
    Object.keys(value).every(key => ["message", "kind", "previousText"].includes(key)) &&
    typeof value.message === "string" && value.message.trim() && value.message.length <= 4000 &&
    ["explanation", "document_text"].includes(value.kind) &&
    (value.previousText === undefined ||
      (typeof value.previousText === "string" && value.previousText.length <= 8000));
}

function buildPrompt(kind) {
  const type = kind === "explanation"
    ? "Напиши основной текст объяснительной от первого лица."
    : "Напиши только запрошенный фрагмент для поля документа.";
  return [
    type,
    "Сохраняй язык пользователя и нейтральный деловой тон.",
    "Поле text должно содержать только готовый фрагмент. Без приветствий, заголовков, подписи, Markdown и комментариев о твоей работе.",
    "Используй факты пользователя. Не выдумывай обстоятельства, даты, имена, обязательства и извинения.",
    "Если пользователь просит правку и есть previousText, переработай его по инструкции. Новый самостоятельный запрос выполняй без привязки к предыдущему варианту.",
    "Сохраняй защитные плейсхолдеры, например [ФИО_1], посимвольно и не создавай новых.",
    "Если без уточнения пришлось бы придумывать саму ситуацию, верни один конкретный вопрос в question и null в text.",
    "Если можно составить фрагмент по известным фактам, верни его в text и null в question.",
  ].join("\n");
}

function normalizeResult(value) {
  if (!value || typeof value !== "object" || Array.isArray(value) ||
      (value.text !== null && typeof value.text !== "string") ||
      (value.question !== null && typeof value.question !== "string")) return null;
  const text = value.text?.trim() || null;
  const question = value.question?.trim() || null;
  if ((text === null) === (question === null) || text?.length > 8000 || question?.length > 500) return null;
  return { text, question };
}
