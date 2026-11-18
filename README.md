<div align="center">

# 📄 GosTek

**Кроссплатформенное privacy-first приложение для проверки оформления по ГОСТ, генерации документов и OCR/MRZ-сканирования**

[![Версия](https://img.shields.io/badge/версия-v0.45.0--stable-blue)](./GosTek.csproj)
[![Платформа](https://img.shields.io/badge/платформа-.NET%2010%20MAUI-512BD4)](https://dotnet.microsoft.com/)
[![Сборка](https://img.shields.io/badge/Windows%20%7C%20Android-green-success)](./GosTek.sln)
[![Лицензия](https://img.shields.io/badge/лицензия-Proprietary-red)](#лицензия)

</div>

---

## 📌 О проекте

**GosTek** — это современное приложение для автоматизации работы со служебными, деловыми и учебными документами. Оно помогает быстро заполнять типовые шаблоны, проверять готовые файлы на соответствие стандартам ГОСТ (ГОСТ 7.32, ГОСТ 2.105, СТБ 6.38-2016) и оцифровывать данные с помощью встроенного оптического сканера.

Профиль хранится локально в защищённом хранилище ОС (Android Keystore / Windows DPAPI). Инспектор работает локально и сохраняет рабочую копию DOCX в кэше приложения без дополнительного шифрования на уровне приложения. Экспортированные файлы сохраняются в выбранное пользователем место. Перед отправкой запросов облачному ИИ-ассистенту приложение маскирует распознаваемые персональные данные.

### 🌟 Ключевые возможности

1. **Проверка документов по ГОСТ (OpenXML):**
   - Автоматический анализ структуры `.docx` файлов: поля страницы, гарнитура шрифта, кегль, межстрочные интервалы, абзацные отступы, нумерация страниц, подписи таблиц, рисунков и листингов кода.
   - Встроенный визуальный конструктор правил ГОСТ (`RuleEditorPage`) с экспертными панелями и предпросмотром «Было / Стало».
   - Устранение всех замечаний в один клик по кнопке «Принять все» с созданием исправленной копии (`_gost.docx`). Оригинал не перезаписывается.

2. **Заполнение шаблонов Word и экспорт в PDF:**
   - 9 встроенных эталонных белорусских шаблонов в 4 категориях («Заявления», «Договоры», «Служебные», «Учёба» — 6 студенческих бланков).
   - Поддержка умных меток вида `[Название поля-подсказка]` и `{Название поля}`.
   - Мгновенная генерация Word-документов с сохранением исходного оформления и автоподстановкой реквизитов из локального профиля.
   - Быстрый экспорт в PDF с блочной вёрсткой на движке QuestPDF.

3. **Оптический сканер документов (OCR и MRZ):**
   - Распознавание печатных документов и машиночитаемой зоны (MRZ) паспортов РБ и ID-карт по стандарту ICAO 9303 (TD3/TD1) с расчётом контрольных сумм 7-3-1.
   - Нативные оффлайн-движки: двухъязычный Tesseract `rus+eng` на Android и системный `Windows.Media.Ocr` на Windows.
   - Умная фильтрация оптических артефактов и исправление типичных ошибок сканирования (гомоглифов) через `MrzSanitizer`.
   - Автоматический перевод имён и фамилий с латиницы на русский язык (`TransliterationHelper`).
   - Интерактивный Diff-экран подтверждения полей и перенос распознанного текста в каталог шаблонов.

4. **Генерация текста для документа:**
   - Чат открывается из поля объяснительной или поля `{Текст}` в пользовательском DOCX. Готовый текст вставляется в пустое поле, а замена введённого текста требует подтверждения.
   - Приложение маскирует распознанные реквизиты в сообщении и предыдущем варианте при правке. Профиль и остальные поля документа остаются в приложении.
   - Cloudflare Worker использует GPT-OSS 20B через Groq и резервную модель Llama 3.3 70B через Cloudflare Workers AI.

5. **Защищённый локальный профиль:**
   - Безопасное хранение реквизитов (ФИО, паспортные данные, 14-значный личный номер, УНП, IBAN с валидацией mod 97, адрес, телефон) в `SecureStorage`.
   - Поддержка произвольных пользовательских полей (`CustomFields`).
   - Полное исключение из облачного резервного копирования ОС Android 12+ (`data_extraction_rules.xml`).

6. **Адаптивность и поддержка Android 15 Edge-to-Edge:**
   - Поддержка системных панелей и вырезов экрана через `SafeAreaEdges` на всех экранах Android.
   - Сворачиваемый десктопный Navigation Rail на Windows (220px ↔ 64px) со смещением области перетаскивания окна и нативный TabBar на Android.

---

## 🛠️ Технологический стек

| Компонент | Технологии и библиотеки |
|---|---|
| **Платформа** | [.NET 10 MAUI](https://learn.microsoft.com/dotnet/maui/) (Single Project: Windows `net10.0-windows10.0.19041.0`, Android `net10.0-android`) |
| **Обработка Word (.docx)** | [DocumentFormat.OpenXml 3.5.1](https://github.com/dotnet/Open-XML-SDK) |
| **Генерация PDF** | [QuestPDF 2026.9.1](https://www.questpdf.com/) (Community License) |
| **Графика и предобработка фото** | [SkiaSharp 4.155.0](https://github.com/mono/SkiaSharp) (бинаризация Оцу, устранение перекоса Deskew) |
| **Оптическое распознавание** | [TesseractOcrMaui 1.5.1](https://github.com/maui-ocr/TesseractOcrMaui) (`rus+eng`, Android), `Windows.Media.Ocr` (Windows) |
| **Камера видоискателя** | [CommunityToolkit.Maui.Camera 5.0.0](https://learn.microsoft.com/dotnet/communitytoolkit/maui/) |
| **Локальная безопасность** | `Microsoft.Maui.Storage.SecureStorage` (Windows DPAPI / Android Keystore) |
| **Облачный ИИ-прокси** | Cloudflare Workers, Groq и Workers AI |
| **Логирование и отладка** | `Microsoft.Extensions.Logging.Debug 10.0.0` |
| **Тестирование** | [xUnit 2.9](https://xunit.net/) — автоматизированные юнит-тесты |

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
# 1. Запуск полного набора юнит-тестов
dotnet test GosTek.Tests/GosTek.Tests.csproj

# 2. Сборка приложения для Windows (Release с -warnaserror)
dotnet build GosTek.csproj -f net10.0-windows10.0.19041.0 -c Release -p:TreatWarningsAsErrors=true -warnaserror

# 3. Сборка APK для Android (Release с R8/AOT линковкой и -warnaserror)
dotnet build GosTek.csproj -f net10.0-android -c Release -p:TreatWarningsAsErrors=true -warnaserror

# 4. Проверка чистоты стиля и форматирования кода
dotnet format GosTek.sln --verify-no-changes
```

---

## 📂 Структура каталогов

```text
GosTek/
├── App.xaml, AppShell.xaml     # Каркас приложения, навигация (Navigation Rail / TabBar)
├── MauiProgram.cs              # Точка входа, регистрация сервисов, шрифтов и обработчиков
├── Controls/                   # Базовые UI-контролы (ChipBar, BounceEffect)
├── Helpers/                    # Вспомогательные утилиты (AnimationHelper)
├── Models/                     # Модели данных:
│   ├── CheckModels.cs          # Модели правил ГОСТ (MasterRule), замечаний и пресетов
│   ├── TemplateCatalog.cs      # Каталог шаблонов, хеш-сигнатуры папок и группировка
│   ├── TemplateDef.cs          # Описание шаблона и элемента каталога
│   ├── TemplateField.cs        # Поля шаблона, маски и источники подстановки
│   ├── DraftStore.cs           # Черновики сессии и буфер передачи OCR-текста
│   └── AiMemory.cs             # Долговременная память контекста диалога
├── Platforms/                  # Платформенный код:
│   ├── Android/                # MainActivity, OcrEngine (Tesseract rus+eng), backup_rules.xml
│   └── Windows/                # WindowSizing (Drag Region, MinSize 800x600), OcrEngine (WinRT)
├── Resources/                  # Шрифты PT Sans, векторные иконки, языковые модели OCR (rus+eng)
├── Services/                   # Ядро бизнес-логики:
│   ├── DocumentChecker.cs      # Проверка оформления документов по ГОСТ через OpenXML
│   ├── CustomRulesService.cs   # Управление правилами ГОСТ, сохранение и автоподбор
│   ├── TemplateEngine.cs       # Разбор и заполнение Word-шаблонов
│   ├── TemplateSeeder.cs       # Автоматическое развертывание 9 эталонных шаблонов ГОСТ
│   ├── FileSaveService.cs      # Кроссплатформенное сохранение файлов и фоновая очистка кэша
│   ├── SettingsService.cs      # Управление настройками, путями и согласия пользователей
│   ├── ThemeService.cs         # Цветовые палитры и тёмная тема Slate Dark
│   ├── MrzParserService.cs     # Распознавание и детерминированный разбор паспортов (ICAO 9303)
│   ├── MrzSanitizer.cs         # Санация оптических искажений и гомоглифов
│   ├── DocumentDataExtractor.cs# Извлечение структурированных реквизитов из OCR
│   ├── SmartInputHelper.cs     # Умный ввод (TitleCase, маски дат, подбор клавиатуры)
│   ├── TransliterationHelper.cs# Транслитерация имён и фамилий с латиницы на русский
│   ├── PrivacyMasker.cs        # Сквозное маскирование ПДн по Закону РБ № 99-З
│   ├── AiAssistantService.cs   # Клиент ИИ-помощника и защитный Outbound Gateway
│   └── PdfExportService.cs     # Экспорт документов в PDF на движке QuestPDF
├── Views/                      # Страницы интерфейса:
│   ├── MainPage.xaml           # Главный экран и быстрый старт
│   ├── TemplatesPage.xaml      # Каталог шаблонов с категориями и поиском
│   ├── FieldsPage.xaml         # Заполнение полей документа, превью А4 и ИИ-чат
│   ├── CheckPage.xaml          # Проверка файлов на соответствие ГОСТ («Принять все»)
│   ├── CheckGroupVm.cs         # ViewModel карточек замечаний с фабричными командами
│   ├── RuleEditorPage.xaml     # Конструктор правил ГОСТ и экспертные панели
│   ├── ScanPage.xaml           # Видоискатель камеры, OCR/MRZ сканер и diff-экран
│   ├── ProfilePage.xaml        # Реквизиты организации и пользователя в SecureStorage
│   ├── SettingsPanel.xaml      # Боковая шторка настроек
│   └── Controls/               # UI-элементы (DateInputField, IconView, Toast, LoadingOverlay)
├── server/                     # Worker генерации текста и тесты
└── GosTek.Tests/               # Модульные тесты xUnit
```

---

## 📚 Документация проекта

- [`README.md`](./README.md) — обзор архитектуры, запуск и стек технологий.
- [`PRIVACY.md`](./PRIVACY.md) — детальная архитектура приватности, защита ПДн по Закону РБ № 99-З и Outbound Gateway.
- [`GOSTEK_FOR_BEGINNERS.md`](./GOSTEK_FOR_BEGINNERS.md) — подробный путеводитель по проекту для начинающих разработчиков простыми словами.
- [`Речь к презентации GosTek.md`](./Речь%20к%20презентации%20GosTek.md) — полный текст выступления для защиты перед жюри с таймингом слайдов.
- [`FIXES_LOG.md`](./FIXES_LOG.md) — сквозной хронологический журнал архитектурных решений и исправлений.
- [`docs/RELEASE.md`](./docs/RELEASE.md) — руководство по подготовке и публикации релизов (Windows MSIX, Android AAB/APK).
- [`docs/MANUAL_CHECKLIST.md`](./docs/MANUAL_CHECKLIST.md) — пошаговый регламент ручной проверки пользовательских сценариев.
- [`docs/STAGE_REPORT.md`](./docs/STAGE_REPORT.md) — детальный отчет по этапам разработки и аудита.
- [`docs/DECISIONS.md`](./docs/DECISIONS.md) — реестр архитектурных решений (D-001 — D-013).
- [`docs/PROJECT_AUDIT.md`](./docs/PROJECT_AUDIT.md) — комплексный отчёт аудита безопасности и архитектуры.
- [`docs/PROJECT_OVERVIEW.md`](./docs/PROJECT_OVERVIEW.md) — технический обзор модулей и структуры проекта.

---

## 📄 Лицензия

**License: Proprietary. All rights reserved.**  
Проприетарное программное обеспечение. Все права защищены. Использование, копирование, модификация и распространение исходного кода и бинарных файлов без предварительного письменного согласия правообладателя запрещены.
