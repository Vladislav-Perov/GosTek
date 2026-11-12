<div align="center">

# 📄 GosTek

**Кроссплатформенное privacy-first приложение для проверки оформления по ГОСТ, генерации документов и OCR/MRZ-сканирования паспортов РБ**

[![Версия](https://img.shields.io/badge/версия-v0.45.0--stable-blue)](./GosTek.csproj)
[![Платформа](https://img.shields.io/badge/платформа-.NET%2010%20MAUI-512BD4)](https://dotnet.microsoft.com/)
[![Сборка](https://img.shields.io/badge/Windows%20%7C%20Android-green-success)](./GosTek.sln)
[![Тесты](https://img.shields.io/badge/тесты-255%20passed-brightgreen)](./GosTek.Tests/)
[![Лицензия](https://img.shields.io/badge/лицензия-Proprietary-red)](#лицензия)

</div>

---

## 📌 О проекте

**GosTek** решает проблему рутинного заполнения и проверки служебных, учебных и деловых документов. Приложение работает в строгом соответствии с концепцией **Privacy-First**: персональные данные граждан, реквизиты организаций и файлы документов хранятся локально в аппаратно зашифрованном виде и никогда не передаются во внешнюю сеть в открытом виде.

### Ключевые возможности

1. **Аудит оформления по ГОСТ (детерминированный движок на базе OpenXML):**
   - Прямой анализ структуры `.docx` через OpenXML SDK (поля страницы, гарнитура, кегль, межстрочный интервал, нумерация страниц, абзацный отступ).
   - Автоматическое устранение замечаний в один клик с сохранением чистой исправленной копии.
2. **Шаблонизатор документов Word и PDF экспорт:**
   - Парсинг плейсхолдеров вида `[Название поля-подсказка]` и `{Название поля}`.
   - Подстановка значений с сохранением оригинального форматирования, поддержкой многострочных вставок и автозаполнением из профиля.
   - Высококачественный экспорт в PDF с блочной версткой QuestPDF.
3. **OCR и ICAO 9303 MRZ сканирование паспортов РБ и ID-карт:**
   - Распознавание фото и сканов через нативные оффлайн-движки: Tesseract (Android) и `Windows.Media.Ocr` (Windows).
   - Полнофункциональный парсер Machine Readable Zone (MRZ) биометрических паспортов (TD3, 2x44) и ID-карт (TD1, 3x30) с проверкой контрольных сумм по алгоритму ICAO 9303 (веса 7-3-1).
   - Интерактивный Diff-экран подтверждения перед сохранением в профиль: «Было в профиле» vs «Распознано с фото» с независимыми чекбоксами выбора полей.
   - Связка сканера со структурой шаблонов: перенос распознанного текста в поля любого выбранного шаблона через ИИ-чат.
4. **Централизованный ИИ-помощник с Outbound Gateway защитой:**
   - Понимание свободных текстовых запросов пользователя и автозаполнение сложных документов.
   - Сквозное локальное маскирование персональных данных (`PrivacyMasker`) перед сетевым вызовом: белорусские адреса, email, ФИО во всех падежах, телефоны, УНП, IBAN и паспорта заменяются безопасными плейсхолдерами.
   - Строгий шлюз `AssertPayloadSafe`: исключение `AiAssistantSecurityException` при попытке отправки сырых ПДн в сеть.
   - Серверный Cloudflare Worker с каскадным fallback моделей, общим дедлайном 15 секунд, вторичным Regex-скруббером и политикой нулевого логирования текста (Zero-Text Logging).
5. **Защищённый профиль организации и гражданина:**
   - Хранение реквизитов (УНП, IBAN с проверкой mod 97, паспорт, адрес) в зашифрованном системном хранилище `SecureStorage` (Android Keystore / Windows DPAPI).
   - Полное исключение из облачного бэкапа (`data_extraction_rules.xml`).
6. **Надежность жизненного цикла и платформенная интеграция:**
   - Детерминированная машина состояний камеры `CameraLifecycleController` (Stopped, Starting, Running, Disposed) с защитой от гонок при смене вкладок.
   - Защита от параллельной навигации `NavigationGuard` (устранение белого экрана при кликах).
   - Аппаратное ограничение габаритов окна Windows 800x600 (Win32 Subclassing).
   - Нативная область перетаскивания `SetDragRectangles`, смещенная вправо от Navigation Rail для свободных кликов по логотипу и кнопкам панели.

---

## 🛠️ Технологический стек

| Компонент | Технологии |
|---|---|
| **Платформа** | [.NET 10 MAUI](https://learn.microsoft.com/dotnet/maui/) (Single Project, `net10.0-windows10.0.19041.0`, `net10.0-android`) |
| **Работа с DOCX** | [DocumentFormat.OpenXml 3.5.1](https://github.com/dotnet/Open-XML-SDK) (валидация архивов, прямое редактирование WordprocessingML) |
| **Генерация PDF** | [QuestPDF 2026.9.1](https://www.questpdf.com/) (нативная блочная вёрстка по ГОСТ, Community License) |
| **Распознавание (OCR)** | [TesseractOcrMaui 1.5.1](https://github.com/maui-ocr/TesseractOcrMaui) (языковая модель `rus.traineddata`, Android), `Windows.Media.Ocr` (Windows) |
| **Камера и превью** | [CommunityToolkit.Maui.Camera 5.0.0](https://learn.microsoft.com/dotnet/communitytoolkit/maui/) |
| **Облачный ИИ-прокси** | Cloudflare Workers (JavaScript, AbortSignal 15s, Model Cascade, Zero-Text Logging, WAF Rate Limiting) |
| **Безопасность** | `Microsoft.Maui.Storage.SecureStorage` (Android Keystore, Windows DPAPI), Outbound Gateway |
| **Тестирование** | xUnit 2.9, 255 модульных тестов, синтетический корпус ПДн |

---

## 🚀 Сборка и запуск

### Требования к окружению
- **.NET SDK:** `10.0.403` или новее.
- **Рабочие нагрузки (workloads):** `maui-windows`, `android`.
- **ОС:** Windows 10/11 (для сборки Windows), Android 10+ (API 29+).

### Команды сборки и проверки

```bash
# 1. Запуск всех unit-тестов (255 тестов)
dotnet test GosTek.Tests/GosTek.Tests.csproj

# 2. Сборка под Windows (Debug)
dotnet build GosTek.csproj -f net10.0-windows10.0.19041.0 -p:TreatWarningsAsErrors=true -warnaserror

# 3. Сборка под Windows (Release)
dotnet build GosTek.csproj -f net10.0-windows10.0.19041.0 -c Release -p:TreatWarningsAsErrors=true -warnaserror

# 4. Сборка APK под Android (Debug)
dotnet build GosTek.csproj -f net10.0-android -p:TreatWarningsAsErrors=true -warnaserror

# 5. Сборка под Android (Release с R8/AOT оптимизацией)
dotnet build GosTek.csproj -f net10.0-android -c Release -p:TreatWarningsAsErrors=true -warnaserror

# 6. Проверка форматирования кода
dotnet format GosTek.sln --verify-no-changes
```

---

## 📂 Структура проекта

- **[`GosTek.sln`](./GosTek.sln)** — главное решение проекта.
- **[`GosTek.csproj`](./GosTek.csproj)** — исходный код клиентского приложения:
  - `Views/` — экраны ContentPage (`MainPage`, `TemplatesPage`, `FieldsPage`, `CheckPage`, `ScanPage`, `ProfilePage`, `SettingsPanel`).
  - `Services/` — бизнес-логика (`DocumentChecker`, `TemplateEngine`, `PdfExportService`, `DocumentDataExtractor`, `MrzParser`, `PrivacyMasker`, `AiAssistantService`, `CameraLifecycleController`, `NavigationGuard`).
  - `Models/` — структуры данных и правила валидации (`CheckModels`, `TemplateDef`, `ProfileData`, `DraftStore`).
  - `Platforms/` — платформенный код Windows (`WindowSizing.cs`) и Android (`MainActivity.cs`, правила бэкапа).
  - `Resources/` — векторные иконки, шрифты PT Sans и языковая база Tesseract.
- **[`server/`](./server/)** — серверная инфраструктура:
  - `worker.js` — Cloudflare Worker со шлюзом моделей, 15с дедлайном и Zero-Text Logging.
  - `DEPLOY.md` — подробная инструкция по развертыванию воркера и настройке Cloudflare WAF.
- **[`GosTek.Tests/`](./GosTek.Tests/)** — 255 модульных тестов:
  - `OutboundGatewayTests.cs` — сквозное маскирование ПДн и `AssertPayloadSafe`.
  - `PrivacyMaskerCorpusTests.cs` — синтетический корпус из 215 примеров (False Positives < 3%, False Negatives < 1%).
  - `Stage4LifecycleTests.cs` — машина состояний камеры и `NavigationGuard`.
  - `WindowDragRegionTests.cs` — геометрия перетаскивания окна Windows при разных DPI и состояниях rail.
  - `MrzParserTests.cs` — ICAO 9303 алгоритм 7-3-1, TD3/TD1 форматы, нормализация OCR.

---

## 📚 Документация

- [`PRIVACY.md`](./PRIVACY.md) — политика конфиденциальности, локальное шифрование и Outbound Gateway.
- [`RELEASE.md`](./RELEASE.md) — инструкции по сборке, развертыванию воркера и чек-лист перед конкурсом.
- [`MANUAL_CHECKLIST.md`](./MANUAL_CHECKLIST.md) — пошаговый регламент ручной проверки для экспертов и жюри конкурса.
- [`STAGE_REPORT.md`](./STAGE_REPORT.md) — детальный отчет по всем этапам разработки и аудита.
- [`DECISIONS.md`](./DECISIONS.md) — реестр архитектурных решений (D-001 — D-010).
- [`FIXES_LOG.md`](./FIXES_LOG.md) — журнал внесенных изменений и устраненных замечаний.

---

## 📄 Лицензия

**License: Proprietary. All rights reserved.**  
Проприетарное программное обеспечение. Все права защищены. Использование, копирование, модификация и распространение исходного кода и бинарных файлов без предварительного письменного согласия правообладателя запрещены.
