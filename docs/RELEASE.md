# Руководство по релизной сборке и развертыванию GosTek (RELEASE.md)

Документ содержит пошаговую инструкцию по компиляции релизных пакетов приложения **GosTek** для Windows 10/11 и Android, развертыванию отказоустойчивого облачного воркера на Cloudflare Workers и контрольный чек-лист подготовки проекта к конкурсу.

---

## 1. Системные требования

### Инструменты разработчика
- **.NET SDK:** `10.0.403` или новее.
- **Рабочие нагрузки .NET MAUI:**
  ```powershell
  dotnet workload install maui-windows
  dotnet workload install maui-android
  ```
- **Node.js:** v18+ и npm (для деплоя Cloudflare Worker).
- **Среда сборки:** Windows 10 (версия 19041+) или Windows 11.

---

## 2. Сборка приложения для Windows 10/11

Приложение для Windows собирается на базе WinUI 3 (Windows App SDK) с целевой платформой `win-x64`.

### Релизная сборка бинарных файлов
```powershell
dotnet build GosTek.csproj -f net10.0-windows10.0.19041.0 -c Release -p:TreatWarningsAsErrors=true -warnaserror
```
Результат сборки находится в каталоге:
`bin\Release\net10.0-windows10.0.19041.0\win-x64\`

### Публикация автономного пакета (Self-Contained)
Для запуска на компьютерах без предустановленного .NET 10 SDK:
```powershell
dotnet publish GosTek.csproj -f net10.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=None -p:SelfContained=true -p:RuntimeIdentifier=win-x64
```
Исполняемый файл:
`bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\GosTek.exe`

---

## 3. Сборка приложения для Android

Приложение для Android поддерживает версии от Android 10 (API 29) до Android 15 (API 35).

### Релизная сборка APK с оптимизацией R8 и ILLink
```powershell
dotnet build GosTek.csproj -f net10.0-android -c Release -p:TreatWarningsAsErrors=true -warnaserror
```
Скомпилированный файл приложения:
`bin\Release\net10.0-android\com.companyname.gostek-Signed.apk` (или несигнированный `.apk`).

### Установка APK на устройство через ADB
```powershell
adb install -r bin\Release\net10.0-android\com.companyname.gostek-Signed.apk
```

---

## 4. Развертывание облачного воркера (Cloudflare Worker)

Серверная часть расположена в каталоге `server/` и обеспечивает отказоустойчивый шлюз моделей с нулевым логированием текста (Zero-Text Logging) и общим дедлайном 15 секунд.

### Быстрое развертывание через Wrangler
1. Перейдите в каталог сервера:
   ```bash
   cd server
   npm install -g wrangler
   wrangler login
   ```
2. Установите секретные ключи API:
   ```bash
   wrangler secret put GEMINI_API_KEY
   wrangler secret put DEEPSEEK_API_KEY
   wrangler secret put GROQ_API_KEY
   ```
3. Опубликуйте воркер:
   ```bash
   wrangler deploy
   ```
4. Настройте URL воркера в приложении:
   В файле `Services/AiAssistantService.cs` укажите адрес вашего развернутого воркера в константе `WorkerEndpointUrl`.
5. Полное руководство по настройке Cloudflare WAF Rate Limiting (15–20 запросов в минуту) см. в [`server/DEPLOY.md`](./server/DEPLOY.md).

---

## 5. Предконкурсный контрольный чек-лист (Pre-Flight Checklist)

Перед демонстрацией или передачей проекта экспертной комиссии конкурса проверьте выполнение всех пунктов:

- [x] **1. Зеленые unit-тесты:** Все 255 тестов успешно проходят (`dotnet test`).
- [x] **2. Чистая сборка без предупреждений:**
  - `dotnet build -f net10.0-windows10.0.19041.0 -c Release -warnaserror` -> 0 предупреждений, 0 ошибок.
  - `dotnet build -f net10.0-android -c Release -warnaserror` -> 0 предупреждений, 0 ошибок.
- [x] **3. Форматирование кода:** `dotnet format --verify-no-changes` -> 0 изменений.
- [x] **4. Безопасность и PII:** В репозитории отсутствуют реальные персональные данные граждан, секретные ключи или сертификаты. Все примеры в тестах и шаблонах используют исключительно синтетические тестовые реквизиты.
- [x] **5. Локальное хранение:** Папка `Templates/` содержит 10 предустановленных ГОСТ-шаблонов. Профиль хранится в системном `SecureStorage`.
- [x] **6. Резервные копии:** Файлы `data_extraction_rules.xml` и `backup_rules.xml` корректно исключают базу данных и кэш из облачных бэкапов Google Drive.
- [x] **7. Режим работы окна:** Окно Windows не сжимается меньше 800x600, заголовок плавно перетаскивается, кнопка сворачивания Navigation Rail доступна для кликов.
- [x] **8. Камера и сканер:** При переключении вкладок камера не крашится, ICAO 9303 MRZ-паспорт корректно валидирует контрольные суммы и предлагает diff-экран.
