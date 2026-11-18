<div align="center">

# 📄 GosTek

**Кроссплатформенное privacy-first приложение для проверки оформления по ГОСТ, генерации документов и OCR/MRZ-сканирования**

[![Версия](https://img.shields.io/badge/версия-v0.45.0--stable-blue)](./GosTek.csproj)
[![Платформа](https://img.shields.io/badge/платформа-.NET%2010%20MAUI-512BD4)](https://dotnet.microsoft.com/)
[![Сборка](https://img.shields.io/badge/Windows%20%7C%20Android-green-success)](./GosTek.sln)
[![Тесты](https://img.shields.io/badge/тесты-355%20passed-brightgreen)](./GosTek.Tests/)
[![Лицензия](https://img.shields.io/badge/лицензия-Proprietary-red)](#лицензия)

</div>

---

## 📌 О проекте

**GosTek** — это современное приложение для автоматизации работы со служебными, деловыми и учебными документами. Оно помогает быстро заполнять типовые шаблоны, проверять готовые файлы на соответствие стандартам ГОСТ и оцифровывать данные с помощью встроенного оптического сканера.

Приложение построено по принципу **Privacy-First (приватность прежде всего)**: все персональные данные, реквизиты организаций и создаваемые файлы хранятся исключительно локально на устройстве в зашифрованном виде (Android Keystore / Windows DPAPI) и никогда не передаются в облако в открытом виде.

### 🌟 Ключевые возможности

1. **Проверка документов по ГОСТ (OpenXML):**
   - Автоматический анализ структуры `.docx` файлов: поля страницы, шрифты, кегль, межстрочные интервалы, абзацные отступы и нумерация страниц.
   - Устранение всех замечаний в один клик с созданием исправленной копии.

2. **Заполнение шаблонов Word и экспорт в PDF:**
   - Поддержка умных меток вида `[Название поля-подсказка]` и `{Название поля}`.
   - Мгновенная генерация Word-документов с сохранением исходного оформления и автоподстановкой реквизитов из профиля.
   - Быстрый экспорт в PDF с блочной вёрсткой на движке QuestPDF.

3. **Оптический сканер документов (OCR и MRZ):**
   - Распознавание печатных документов и машиночитаемой зоны (MRZ) через нативные оффлайн-движки: двухъязычный Tesseract `rus+eng` на Android и системный `Windows.Media.Ocr` на Windows.
   - Умная фильтрация оптических артефактов и исправление типичных ошибок сканирования (гомоглифов).
   - Автоматический перевод имён и фамилий с латиницы на русский язык (`TransliterationHelper`).
   - Перенос распознанного текста в поля любого выбранного шаблона.

4. **Встроенный ИИ-ассистент с защитным шлюзом:**
   - Понимание запросов на естественном языке для заполнения сложных форм.
   - Локальная маскировка персональных данных (`PrivacyMasker`) перед обращением к облаку: адреса, телефоны, email и паспортные данные заменяются защитными токенами.
   - Аппаратный контроль `AssertPayloadSafe`: невозможность утечки реальных данных пользователя во внешнюю сеть.
   - Серверный Cloudflare Worker с каскадом нейросетей, быстрым ответом и нулевым сохранением текстов (Zero-Text Logging).

5. **Защищённый локальный профиль:**
   - Безопасное хранение реквизитов (ФИО, наименование, УНП, IBAN с валидацией mod 97, адрес).
   - Защита от утечек и исключение из облачного резервного копирования ОС.

---

## 🛠️ Технологический стек

| Компонент | Технологии и библиотеки |
|---|---|
| **Платформа** | [.NET 10 MAUI](https://learn.microsoft.com/dotnet/maui/) (Single Project: Windows `net10.0-windows10.0.19041.0`, Android `net10.0-android`) |
| **Обработка Word (.docx)** | [DocumentFormat.OpenXml 3.5.1](https://github.com/dotnet/Open-XML-SDK) |
| **Генерация PDF** | [QuestPDF 2026.9.1](https://www.questpdf.com/) (Community License) |
| **Оптическое распознавание** | [TesseractOcrMaui 1.5.1](https://github.com/maui-ocr/TesseractOcrMaui) (`rus+eng`, Android), `Windows.Media.Ocr` (Windows) |
| **Камера видоискателя** | [CommunityToolkit.Maui.Camera 5.0.0](https://learn.microsoft.com/dotnet/communitytoolkit/maui/) |
| **Локальная безопасность** | `Microsoft.Maui.Storage.SecureStorage` (Windows DPAPI / Android Keystore) |
| **Облачный ИИ-прокси** | Cloudflare Workers (Edge JS, Zero-Text Logging, Model Cascade) |
| **Тестирование** | [xUnit 2.9](https://xunit.net/) — 355 автоматизированных юнит-тестов |

---

## 🚀 Сборка и запуск проекта

### Требования к окружению
- **.NET SDK:** `10.0` или новее.
- **Рабочие нагрузки .NET MAUI:**
  ```bash
  dotnet workload install maui-windows android
  ```
- **Операционная система:** Windows 10/11 (для сборки Windows-версии) или любая ОС с установленным Android SDK.

### Основные команды

```bash
# 1. Запуск полного набора юнит-тестов (355 тестов)
dotnet test GosTek.Tests/GosTek.Tests.csproj

# 2. Сборка приложения для Windows (Release)
dotnet build GosTek.csproj -f net10.0-windows10.0.19041.0 -c Release -p:TreatWarningsAsErrors=true -warnaserror

# 3. Сборка APK для Android (Release с R8/AOT линковкой)
dotnet build GosTek.csproj -f net10.0-android -c Release -p:TreatWarningsAsErrors=true -warnaserror

# 4. Проверка чистоты стиля и форматирования кода
dotnet format GosTek.sln --verify-no-changes
```

---

## 📂 Структура каталогов

```text
GosTek/
├── App.xaml, AppShell.xaml     # Каркас приложения, навигация (Navigation Rail / TabBar)
├── MauiProgram.cs              # Точка входа, регистрация сервисов и шрифтов
├── Controls/                   # Базовые UI-контролы (ChipBar, BounceEffect)
├── Helpers/                    # Вспомогательные утилиты (AnimationHelper)
├── Models/                     # Модели данных (CheckModels, TemplateDef, DraftStore, AiMemory)
├── Platforms/                  # Платформенный код для Windows (Subclassing, DragRegion) и Android (OcrEngine)
├── Resources/                  # Шрифты PT Sans, векторные иконки, языковые модели OCR (rus+eng)
├── Services/                   # Ядро бизнес-логики:
│   ├── DocumentChecker.cs      # Проверка оформления документов по ГОСТ
│   ├── TemplateEngine.cs       # Разбор и заполнение Word-шаблонов
│   ├── FileSaveService.cs      # Кроссплатформенное сохранение файлов и MediaStore
│   ├── SettingsService.cs      # Управление настройками и путями
│   ├── ThemeService.cs         # Цветовые палитры и тёмная тема Slate Dark
│   ├── MrzParserService.cs     # Распознавание и детерминированный разбор паспортов
│   ├── MrzSanitizer.cs         # Санация оптических искажений и гомоглифов
│   ├── DocumentDataExtractor.cs# Извлечение структурированных реквизитов из OCR
│   ├── SmartInputHelper.cs     # Умный ввод (TitleCase, маски дат, подбор клавиатуры)
│   ├── TransliterationHelper.cs# Транслитерация имён и фамилий с латиницы на русский
│   ├── PrivacyMasker.cs        # Сквозное маскирование ПДн перед отправкой в сеть
│   └── AiAssistantService.cs   # Клиент ИИ-помощника и Outbound Gateway
├── Views/                      # Страницы интерфейса:
│   ├── MainPage.xaml           # Главный экран и быстрый старт
│   ├── TemplatesPage.xaml      # Каталог шаблонов с категориями и поиском
│   ├── FieldsPage.xaml         # Заполнение полей документа и ИИ-чат
│   ├── CheckPage.xaml          # Проверка файлов на соответствие ГОСТ
│   ├── ScanPage.xaml           # Видоискатель камеры и сканер документов
│   ├── ProfilePage.xaml        # Реквизиты организации и пользователя
│   ├── SettingsPanel.xaml      # Боковая шторка настроек
│   └── Controls/               # Специализированные элементы (DateInputField, IconView, Toast)
├── server/                     # Облачный прокси (Cloudflare Worker)
└── GosTek.Tests/               # 355 модульных тестов xUnit
```

---

## 📚 Документация проекта

- [`README.md`](./README.md) — обзор архитектуры, запуск и стек технологий.
- [`PRIVACY.md`](./PRIVACY.md) — детальная архитектура приватности, защита ПДн и Outbound Gateway.
- [`FIXES_LOG.md`](./FIXES_LOG.md) — сквозной хронологический журнал архитектурных решений и исправлений.
- [`docs/RELEASE.md`](./docs/RELEASE.md) — руководство по подготовке и публикации релизов (Windows MSIX, Android AAB/APK).
- [`docs/MANUAL_CHECKLIST.md`](./docs/MANUAL_CHECKLIST.md) — пошаговый регламент ручной проверки пользовательских сценариев.
- [`docs/STAGE_REPORT.md`](./docs/STAGE_REPORT.md) — детальный отчет по этапам разработки и аудита.
- [`docs/DECISIONS.md`](./docs/DECISIONS.md) — реестр архитектурных решений (D-001 — D-010).
- [`docs/PROJECT_AUDIT.md`](./docs/PROJECT_AUDIT.md) — комплексный отчёт аудита безопасности и архитектуры.
- [`docs/PROJECT_OVERVIEW.md`](./docs/PROJECT_OVERVIEW.md) — технический обзор модулей и структуры проекта.

---

## 📄 Лицензия

**License: Proprietary. All rights reserved.**  
Проприетарное программное обеспечение. Все права защищены. Использование, копирование, модификация и распространение исходного кода и бинарных файлов без предварительного письменного согласия правообладателя запрещены.
