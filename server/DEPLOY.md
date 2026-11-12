# Руководство по развертыванию Cloudflare Worker (GosTek AI Proxy)

В данном документе приведена пошаговая инструкция по развертыванию серверного прокси-шлюза **GosTek AI Proxy** в бессерверной среде Cloudflare Workers.

---

## 1. Архитектура и требования

Серверный воркер `server/worker.js` выполняет роль защищённого шлюза:
- Поддерживает каскадный вызов языковых моделей (Google Gemini 2.5/2.0, DeepSeek Chat, Groq Llama 3.1 8B).
- Осуществляет вторичное маскирование персональных данных (Regex Scrubber).
- Гарантирует нулевое логирование текстов пользователя (Zero-Text Logging).
- Ограничивает размер запроса до 32 КБ и контролирует общий дедлайн ответа (15 секунд).

### Необходимые инструменты:
- [Node.js](https://nodejs.org/) версии 18+ (включает `npm`).
- Бесплатный или платный аккаунт [Cloudflare](https://dash.cloudflare.com/).
- API-ключи провайдеров (хотя бы один из списка):
  - **Google Gemini:** [Google AI Studio](https://aistudio.google.com/)
  - **Groq Cloud:** [Groq Console](https://console.groq.com/)
  - **DeepSeek:** [DeepSeek Platform](https://platform.deepseek.com/)

---

## 2. Установка и авторизация Wrangler

Установите официальный CLI Cloudflare Wrangler:

```bash
npm install -g wrangler
```

Авторизуйтесь в своей учётной записи Cloudflare:

```bash
wrangler login
```

В браузере откроется окно подтверждения доступа. Нажмите **Allow**.

---

## 3. Конфигурация проекта (`wrangler.toml`)

Перейдите в каталог `server/` и создайте или проверьте файл `wrangler.toml`:

```toml
name = "gostek-proxy"
main = "worker.js"
compatibility_date = "2024-04-01"

[vars]
# Необязательно: каскадная конфигурация моделей в формате JSON
# Если не задано, используются все провайдеры, для которых заданы секреты
# MODELS_CASCADE = '[{"provider":"gemini","model":"gemini-2.5-flash"},{"provider":"groq","model":"llama-3.1-8b-instant"}]'
```

---

## 4. Добавление секретов (API Keys)

Никогда не храните API-ключи в открытом виде в коде или репозитории git. Добавьте их через зашифрованное хранилище секретов Cloudflare:

```bash
# 1. Секрет Google Gemini API:
wrangler secret put GEMINI_API_KEY
# (введите ключ и нажмите Enter)

# 2. Секрет Groq Cloud API:
wrangler secret put GROQ_API_KEY
# (введите ключ и нажмите Enter)

# 3. Секрет DeepSeek API (опционально):
wrangler secret put DEEPSEEK_API_KEY
# (введите ключ и нажмите Enter)
```

---

## 5. Развертывание (Deploy)

Выполните команду развертывания:

```bash
wrangler deploy
```

После завершения вывода в консоли отобразится публичный URL вашего воркера, например:
```
https://gostek-proxy.<ваш-субдомен>.workers.dev
```

При необходимости укажите этот URL в константе `AiAssistantService.DefaultEndpoint` в клиентском приложении GosTek (`Services/AiAssistantService.cs`).

---

## 6. Настройка Cloudflare WAF и Rate Limiting (Рекомендуется)

Для защиты от злоупотреблений и DDoS-атак настройте ограничение частоты запросов в панели Cloudflare:

1. Откройте панель Cloudflare Dashboard: **Security** -> **WAF** -> **Rate limiting rules**.
2. Создайте правило **"GosTek AI Rate Limit"**:
   - **If incoming requests match:** `URI Path equals "/"`
   - **Rate limit:** 15–20 запросов в минуту на один IP-адрес.
   - **Action:** Block на 60 секунд или JS Challenge.
3. Сохраните правило.

---

## 7. Проверка работоспособности

Проверьте работу развернутого шлюза с помощью `curl`:

```bash
curl -X POST https://gostek-proxy.<ваш-субдомен>.workers.dev \
  -H "Content-Type: application/json" \
  -d '{
    "userText": "проспал",
    "templateName": "Объяснительная записка",
    "fieldsContext": "Текущее состояние полей: [ФИО: [ФИО_1], Причина: [пусто]]"
  }'
```

Ожидаемый ответ:
```json
{
  "success": true,
  "data": {
    "fields": {
      "Причина": "Опоздал на рабочее место в связи с непредвиденными личными обстоятельствами (проспал)."
    },
    "question": null,
    "status": "success"
  },
  "memory_ops": [],
  "model": "gemini-2.5-flash",
  "duration_ms": 720
}
```
