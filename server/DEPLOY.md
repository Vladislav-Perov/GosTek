# Прежний сервис ИИ GosTek

Текущая версия GosTek использует отдельный [сервис генерации текста](text/DEPLOY.md). Ниже описано развёртывание для ранее выпущенных клиентов.

Прежние клиенты GosTek обращаются к Worker `gostek-proxy` по действующему адресу. Ключи провайдеров хранятся в Cloudflare Secrets. Для проверки используйте вымышленные данные.

## Проверка перед публикацией

Из каталога `server` выполните:

```powershell
node --test worker.test.mjs
npx wrangler whoami
npx wrangler secret list --name gostek-proxy
```

Если Wrangler не авторизован, войдите через `npx wrangler login` в аккаунт, владеющий текущим Worker. Убедитесь, что выбран нужный аккаунт и его адрес совпадает с `AiAssistantService.DefaultEndpoint`.

## Первый провайдер

Для Groq укажите секрет `GROQ_API_KEY` и модель `GROQ_MODEL=openai/gpt-oss-20b`. Доступность модели и квоту проверьте в аккаунте Groq. Добавить новый секрет можно командой `npx wrangler secret put GROQ_API_KEY --name gostek-proxy`. Wrangler запросит его значение интерактивно.

Откройте настройки переменных опубликованного Worker. Если уже задан `MODELS_CASCADE`, он имеет приоритет над `GROQ_MODEL` и `GEMINI_MODEL`. Для первого запуска установите:

```json
[{"provider":"groq","model":"openai/gpt-oss-20b"}]
```

Для Gemini укажите `GEMINI_API_KEY` и доступную аккаунту модель, например `gemini-3.6-flash`. Элемент каскада имеет вид `{"provider":"gemini","model":"gemini-3.6-flash"}`. Добавляйте его после проверки основного провайдера. Если `MODELS_CASCADE` отсутствует, Worker использует настроенные секреты в порядке Groq, Gemini, DeepSeek.

При публикации Wrangler учитывает конфигурацию в репозитории. Параметр `keep_vars` сохраняет переменные, настроенные в панели. Проверьте настройки аккаунта, затем из каталога `server` выполните `npx wrangler deploy`.

## Контрольный запрос

```powershell
$endpoint = 'https://gostek-proxy.d13225135.workers.dev'
$payload = @{
    userText = 'Scheduled maintenance. Write a short formal reason.'
    templateName = 'Request'
    fieldsContext = 'field_1 (Reason): empty'
    fieldIds = @('field_1')
    memory = @()
} | ConvertTo-Json -Compress
Invoke-RestMethod -Method Post -Uri $endpoint -ContentType 'application/json' -Body $payload
```

Успешный ответ содержит `success = true` и строковое значение в `data.fields.field_1` либо `data.question`. В приложении проверьте предложение для пустого поля, уточняющий вопрос при недостатке информации и запрос подтверждения перед заменой заполненного поля.

При `503` откройте живые логи Cloudflare или выполните `npx wrangler tail gostek-proxy`. Найдите запись по `request_id` из ответа. Код `configuration` означает, что подходящих настроенных провайдеров нет. Код `providers_unavailable` означает, что каскад завершился без допустимого ответа. Логи содержат модель, категорию, длительность и HTTP-статус провайдера при его наличии. Проверьте ключ или доступ к модели при `401/403`, её идентификатор при `404` и квоту при `429`.

- [Справочник моделей Groq](https://console.groq.com/docs/models)
- [Список моделей Gemini](https://aistudio.google.com/docs/models)
- [Живые логи Cloudflare Workers](https://developers.cloudflare.com/workers/observability/logs/real-time-logs/)
